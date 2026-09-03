using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoBuildCombo;

/// <summary>
/// XML 工具调用回退解析装饰器：部分模型会把 XML 格式的 tool_calls 塞进 reasoning_content 或文本里，
/// 而不走单独的 tool_calls 字段，导致 FunctionInvokingChatClient 认为对话已结束（无工具可执行），
/// 后续 builder.Build() 必然失败。此处解析出 XML 工具调用并注入单独的 tool_calls 字段（FunctionCallContent）进行回退解析；
/// 无法回退解析的（畸形 XML 或 JSON 等其他格式）按原因分类完整打印。
/// 放在 ConversationLoggingChatClient 外层：记录层看到的是回退解析前的原始响应（XML 原文保留在 reasoning/文本里），
/// 注入的 FunctionCallContent 则通过本类的回退解析告警日志体现
/// </summary>
internal class XmlToolCallFallbackParseChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    private int _round;

    /// <summary>匹配 LLM 塞在 reasoning/文本里的 XML 形式工具调用标签</summary>
    private static readonly Regex XmlToolCallPattern = new(
        @"<\s*/?\s*(tool_call|function_call|use_tool|invoke|tool|function)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>提取 <tool_call>...</tool_call> 块</summary>
    private static readonly Regex ToolCallBlockPattern = new(
        @"<tool_call>\s*(?<body>.*?)\s*</tool_call>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>解析块内的函数名，支持 <function=名称> 与 <function><name>名称</name> 两种形态</summary>
    private static readonly Regex FunctionNamePattern = new(
        @"<function\s*=\s*(?<name>[\w.\-]+)\s*>|<function>\s*<name>\s*(?<name>[\w.\-]+)\s*</name>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>解析 <parameter=键>值</parameter> 形式的参数</summary>
    private static readonly Regex ParameterPattern = new(
        @"<parameter\s*=\s*(?<key>[\w.\-]+)\s*>\s*(?<value>.*?)\s*</parameter>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>识别 JSON 等非 XML 形式的工具调用痕迹（如 DeepSeek-R1 把 {"name":...,"arguments":...} 写进正文；要求两键同时出现以降低误判），仅记录不回退解析</summary>
    private static readonly Regex JsonToolCallPattern = new(
        @"\{\s*""(name|function)""\s*:(?=.*""(arguments|parameters)""\s*:)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var round = Interlocked.Increment(ref _round);
        var response = await InnerClient.GetResponseAsync(messages, options, cancellationToken);

        FallbackParseHiddenToolCalls(response, round);

        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // 本任务未使用流式，直接透传
        await foreach (var update in InnerClient.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            yield return update;
        }
    }

    /// <summary>
    /// 拦截检测并回退解析：解析出 XML 工具调用注入本轮 assistant 消息，让外层 FunctionInvokingChatClient 正常执行工具调用
    /// </summary>
    private void FallbackParseHiddenToolCalls(ChatResponse response, int round)
    {
        // 本轮已存在走单独 tool_calls 字段的调用时循环会继续，藏在文本里的 XML 无害，跳过
        if (response.Messages.Any(m => m.Contents.Any(c => c is FunctionCallContent)))
        {
            return;
        }

        var fallbackCalls = new List<FunctionCallContent>();
        var unparsed = new StringBuilder();
        foreach (var message in response.Messages)
        {
            foreach (var content in message.Contents)
            {
                var text = content switch
                {
                    TextReasoningContent reasoning => reasoning.Text,
                    TextContent textContent => textContent.Text,
                    _ => null,
                };
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                var countBefore = fallbackCalls.Count;
                if (XmlToolCallPattern.IsMatch(text))
                {
                    foreach (Match block in ToolCallBlockPattern.Matches(text))
                    {
                        if (TryParseXmlToolCall(block.Groups["body"].Value, round, fallbackCalls.Count, out var call))
                        {
                            fallbackCalls.Add(call);
                        }
                    }
                }

                if (fallbackCalls.Count > countBefore)
                {
                    continue;
                }

                // 无法回退解析：区分畸形 XML 与 JSON 等其他意外格式，完整记录内容便于排查
                var reason = XmlToolCallPattern.IsMatch(text)
                    ? "XML 工具调用无法解析"
                    : JsonToolCallPattern.IsMatch(text)
                        ? "疑似 JSON 形式的工具调用（仅支持回退解析 XML）"
                        : null;
                if (reason != null)
                {
                    unparsed.AppendLine($"[{message.Role} · {DescribeContent(content)} · {reason}]");
                    unparsed.AppendLine(text);
                }
            }
        }

        if (fallbackCalls.Count > 0)
        {
            var assistantMessage = response.Messages.Last(m => m.Role == ChatRole.Assistant);
            foreach (var call in fallbackCalls)
            {
                assistantMessage.Contents.Add(call);
            }
            Logger.LogWarning(
                "── 第 {Round} 轮 · 回退解析：从 reasoning 文本中解析出 {Count} 个 XML 工具调用并注入单独的 tool_calls 字段：{Calls} ──",
                round, fallbackCalls.Count,
                string.Join("; ", fallbackCalls.Select(c => $"{c.Name}({string.Join(", ", c.Arguments?.Select(kv => $"{kv.Key}={kv.Value}") ?? [])})")));
        }
        else if (unparsed.Length > 0)
        {
            Logger.LogWarning(
                "── 第 {Round} 轮 · 检测到 LLM 在 reasoning 文本中输出未支持的工具调用格式（未走单独的 tool_calls 字段，本轮将提前结束）──\n{Hidden}",
                round, unparsed.ToString());
        }
    }

    /// <summary>
    /// 解析 <tool_call> 块内的 XML 内容，函数名必须形如标识符以过滤误匹配
    /// </summary>
    private static bool TryParseXmlToolCall(string body, int round, int index, out FunctionCallContent call)
    {
        call = null!;
        var nameMatch = FunctionNamePattern.Match(body);
        if (!nameMatch.Success)
        {
            return false;
        }

        var name = nameMatch.Groups["name"].Value.Trim();
        if (!Regex.IsMatch(name, @"^[A-Za-z_][\w.\-]*$"))
        {
            return false;
        }

        var arguments = new Dictionary<string, object?>();
        foreach (Match parameter in ParameterPattern.Matches(body))
        {
            arguments[parameter.Groups["key"].Value.Trim()] = ParseParameterValue(parameter.Groups["value"].Value.Trim());
        }

        call = new FunctionCallContent($"fallback_{round}_{index}", name, arguments);
        return true;
    }

    /// <summary>
    /// 回退解析出的参数值只能是字符串 "True"/"2" 之类的，而 AIFunction 按目标参数类型做强类型 JSON 绑定，因此尝试转换
    /// 但还是尽量用字符串参数吧
    /// </summary>
    private static object? ParseParameterValue(string value)
    {
        if (bool.TryParse(value, out var b))
        {
            return b;
        }

        if (long.TryParse(value, out var l))
        {
            return l;
        }

        if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d))
        {
            return d;
        }

        return value;
    }

    private static string DescribeContent(AIContent content) => content switch
    {
        TextReasoningContent => "reasoning_content",
        TextContent => "text",
        _ => content.GetType().Name,
    };
}
