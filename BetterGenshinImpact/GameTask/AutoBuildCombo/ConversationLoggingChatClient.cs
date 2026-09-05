using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoBuildCombo;

/// <summary>
/// 装饰器：按轮次记录与 LLM 的完整对话（发给 LLM 的新增内容 ↔ LLM 发出的回复），
/// 用于观察 FunctionInvokingChatClient 的中间多轮过程。
/// 必须放在 FunctionInvokingChatClient 内层，这样工具调用循环的每次请求都会经过此处
/// </summary>
internal class ConversationLoggingChatClient(IChatClient innerClient, ILogger logger) : DelegatingChatClient(innerClient)
{
    private int _round;

    /// <summary>上一轮请求的消息数，用于每轮只记录新增的发出内容</summary>
    private int _previousRequestCount;

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var round = Interlocked.Increment(ref _round);
        logger.LogInformation("── 第 {Round} 轮 · 发给 LLM ──\n{Messages}", round, FormatRequestDelta(messages));

        var response = await InnerClient.GetResponseAsync(messages, options, cancellationToken);

        logger.LogInformation("── 第 {Round} 轮 · LLM 发出 ──\n{Messages}", round, FormatMessages(response.Messages));
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
}
