using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System;
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
/// 装饰器：按轮次记录与 LLM 的完整对话（发给 LLM 的新增内容 ↔ LLM 发出的回复），
/// 用于观察 FunctionInvokingChatClient 的中间多轮过程。
/// 必须放在 FunctionInvokingChatClient 内层，这样工具调用循环的每次请求都会经过此处
/// </summary>
internal class ConversationLoggingChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    private int _round;

    /// <summary>上一轮请求的消息数，用于每轮只记录新增的发出内容</summary>
    private int _previousRequestCount;

    /// <summary>匹配 LLM 塞在 reasoning/文本里的 XML 形式工具调用标签</summary>
    private static readonly Regex XmlToolCallPattern = new(
        @"<\s*/?\s*(tool_call|function_call|use_tool|invoke|tool|function)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var round = Interlocked.Increment(ref _round);
        Logger.LogInformation("── 第 {Round} 轮 · 发给 LLM ──\n{Messages}", round, FormatRequestDelta(messages));

        var response = await InnerClient.GetResponseAsync(messages, options, cancellationToken);

        DetectAndLogHiddenToolCalls(response, round);

        Logger.LogInformation("── 第 {Round} 轮 · LLM 发出 ──\n{Messages}", round, FormatMessages(response.Messages));
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
    /// FunctionInvokingChatClient 每轮在上轮历史末尾追加消息（前缀不变），只格式化本轮新增部分；
    /// 其中的 assistant 消息（工具调用）已在上一轮"LLM 发出"记录过，跳过以免重复
    /// </summary>
    private string FormatRequestDelta(IEnumerable<ChatMessage> messages)
    {
        var requestMessages = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
        var startIndex = requestMessages.Count > _previousRequestCount ? _previousRequestCount : 0;
        _previousRequestCount = requestMessages.Count;

        var sb = new StringBuilder();
        for (var i = startIndex; i < requestMessages.Count; i++)
        {
            var message = requestMessages[i];
            if (startIndex > 0 && message.Role == ChatRole.Assistant)
            {
                continue;
            }
            sb.AppendLine($"[{message.Role}] {FormatContents(message)}");
        }
        return sb.ToString();
    }

    private static string FormatMessages(IEnumerable<ChatMessage> messages)
    {
        var sb = new StringBuilder();
        foreach (var message in messages)
        {
            sb.AppendLine($"[{message.Role}] {FormatContents(message)}");
        }
        return sb.ToString();
    }

    private static string FormatContents(ChatMessage message)
    {
        var parts = message.Contents.Select(content => content switch
        {
            TextReasoningContent reasoning => $"[reasoning_content] {reasoning.Text}",
            TextContent text => text.Text,
            FunctionCallContent call => $"调用工具 {call.Name}({string.Join(", ", call.Arguments?.Select(kv => $"{kv.Key}={kv.Value}") ?? [])})",
            FunctionResultContent result => $"工具结果：{result.Result}",
            _ => content.ToString() ?? string.Empty,
        });
        return string.Join(" ", parts.Where(p => !string.IsNullOrEmpty(p)));
    }

    /// <summary>
    /// 拦截检测：部分模型会把 XML 格式的 tool_calls 塞进 reasoning_content 或文本里，
    /// 而不发出真实工具调用，导致 FunctionInvokingChatClient 认为对话已结束（无工具可执行），
    /// 后续 builder.Build() 必然失败。此处识别该情况并完整打印被"藏起来"的调用内容
    /// </summary>
    private void DetectAndLogHiddenToolCalls(ChatResponse response, int round)
    {
        // 本轮存在真实工具调用时循环会继续，藏在文本里的 XML 无害，跳过
        if (response.Messages.Any(m => m.Contents.Any(c => c is FunctionCallContent)))
        {
            return;
        }

        var sb = new StringBuilder();
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
                if (!string.IsNullOrEmpty(text) && XmlToolCallPattern.IsMatch(text))
                {
                    sb.AppendLine($"[{message.Role} · {DescribeContent(content)}]");
                    sb.AppendLine(text);
                }
            }
        }

        if (sb.Length > 0)
        {
            Logger.LogWarning(
                "── 第 {Round} 轮 · 检测到 LLM 在 reasoning/文本中输出 XML 工具调用（未触发真实 tool_calls，本轮将提前结束）──\n{Hidden}",
                round, sb.ToString());
        }
    }

    private static string DescribeContent(AIContent content) => content switch
    {
        TextReasoningContent => "reasoning_content",
        TextContent => "text",
        _ => content.GetType().Name,
    };
}
