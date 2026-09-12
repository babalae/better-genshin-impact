using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoCombo.ComboBuild;

/// <summary>
/// 截断检查装饰器：LLM 因上下文窗口耗尽或输出达到 max_tokens 被截断时，服务端不返回错误，
/// 而是正常结束响应并给出 finish_reason=length，调用方若不检查就会误以为模型正常完成。
/// 必须放在 FunctionInvokingChatClient 外层：中间轮被截断时该轮不会有工具调用，循环会提前结束，
/// 最终响应保留的正是最后一轮的 FinishReason，外层检查即可覆盖。
/// 暂不支持流式
/// </summary>
internal class LengthCutoffCheckChatClient(IChatClient innerClient, ILogger logger) : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var response = await InnerClient.GetResponseAsync(messages, options, cancellationToken);

        if (response.FinishReason == ChatFinishReason.Length)
        {
            logger.LogError("LLM 响应被截断（finish_reason=length）：上下文窗口耗尽或输出达到 max_tokens，任务无法继续");
            throw new Exception("LLM 响应被截断（finish_reason=length）：上下文窗口耗尽或输出达到 max_tokens，模型未能完成本次输出");
        }

        return response;
    }
}
