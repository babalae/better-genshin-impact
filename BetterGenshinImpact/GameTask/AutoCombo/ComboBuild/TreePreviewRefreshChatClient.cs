using BetterGenshinImpact.ViewModel.Windows;
using CsTrees.Display;
using CsTrees.MEAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoCombo.ComboBuild;

/// <summary>
/// 装饰器：中途行为树 preview 刷新。每轮 LLM 请求入口处计算当前 builder 预览 ASCII，
/// 更新 AutoComboTreeViewModel.Instance.LatestTreeAscii 供浮窗绑定展示，让用户实时看到 LLM 多轮建树过程。
/// 必须放在 FunctionInvokingChatClient 内层，这样工具调用循环的每次请求都会经过此处；
/// 此时 builder 反映的是上一轮工具执行后的状态（工具由外层在上一轮响应返回后执行）。
/// 树还未添加节点（仍处初始检查点，如首轮只调了 SetManualSkillCd 等非建树工具）时不渲染
/// </summary>
/// <param name="buildTools">工具宿主状态，用于判断 builder 是否仍处初始检查点（空树）</param>
/// <param name="mainBuilder">主树构建器（闭包引用捕获）；兜底建树阶段主树已定型，传 null 跳过主树刷新</param>
/// <param name="fallbackBuilder">兜底树构建器（根 Sequence 已预建）；主建树阶段兜底 builder 尚未创建时为 null，兜底建树阶段传入</param>
internal class TreePreviewRefreshChatClient(
    IChatClient innerClient,
    ILogger logger,
    IBuildToolsState buildTools,
    AutoComboBuildBuilder? mainBuilder,
    AutoComboBuildFallbackBuilder? fallbackBuilder = null) : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        RefreshPreview();
        return await InnerClient.GetResponseAsync(messages, options, cancellationToken);
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
    /// 计算当前 builder 预览并更新 AutoComboTreeViewModel.Instance.LatestTreeAscii
    /// 抛异常时不影响 LLM 调用：预览刷新失败仅丢失 UI 展示，不应中断建树
    /// </summary>
    private void RefreshPreview()
    {
        try
        {
            var vm = AutoComboTreeViewModel.Instance;

            // 主树：空树（初始检查点）时不渲染，避免无意义刷新；兜底建树阶段主树已定型（mainBuilder 为 null）不覆盖
            if (mainBuilder is not null && !buildTools.IsAtInitialCheckpoint)
            {
                vm.LatestTreeAscii = Display.AsciiTree(mainBuilder.Preview());
            }

            // 兜底树：根 Sequence 已预建，直接渲染；主建树阶段未传入则跳过
            if (fallbackBuilder is not null)
            {
                vm.LatestFallbackTreeAscii = Display.AsciiTree(fallbackBuilder.Preview());
            }
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "行为树 preview 刷新失败");
        }
    }
}
