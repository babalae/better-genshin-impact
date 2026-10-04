using System;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>
/// “任务计划”页面的步骤 3 运行提交；状态查看、取消与续跑移至共用执行记录视图模型。
/// </summary>
public partial class PuloniaTaskPlanViewModel
{
    /// <summary>
    /// 计划内与全局页面复用的执行记录模型，不依赖可编辑的计划内容。
    /// </summary>
    public PuloniaTaskHistoryViewModel History { get; }

    /// <summary>
    /// 当前计划是否可以提交运行。
    /// </summary>
    public bool CanRunPlan => !IsBusy && SelectedDocument is not null;

    /// <summary>卡片命令检查自身目标，兼容未提供参数时执行当前计划的调用。</summary>
    private bool CanRunDocument(PuloniaTaskPlanDocumentViewModel? document)
        => !IsBusy && (document ?? SelectedDocument) is not null;

    /// <summary>
    /// 保存当前计划后提交运行；提交时固定快照，随后编辑不会影响本次运行。
    /// 页面执行入口默认不限时；手动停止与节点自身的执行策略仍然生效。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRunDocument))]
    private async Task RunPlanAsync(PuloniaTaskPlanDocumentViewModel? document)
    {
        document ??= SelectedDocument;
        if (document is null)
            return;
        SelectedDocument = document;
        if (document.IsDirty && !await SaveDocumentAsync(document))
            return;

        try
        {
            await RefreshResourceVersionsAsync();
            // 禁用资源仍显示更新标签，但不会阻止其他已启用任务执行。
            var pendingUpdates = GetReferencedDocuments(document, GetResourceDocuments(), enabledOnly: true)
                .SelectMany(target => target.EnumerateNodes())
                .Count(node => node.IsEffectivelyEnabled && node.CanUpdateResourceVersion && node.HasResourceUpdate);
            if (pendingUpdates > 0)
                throw new InvalidOperationException($"“{document.Name}”有 {pendingUpdates} 项启用中的资源更新，请先点击计划卡片的确认更新按钮。");
            var requestId = await _taskService.EnqueueAsync(new PuloniaTaskRequest
            {
                PlanId = document.Id,
                Source = "ui"
            });
            await History.RevealRunAsync(requestId);
            StatusMessage = $"已提交“{document.Name}”，请求 {requestId:D} 已固定快照并进入串行队列。";
        }
        catch (Exception ex)
        {
            StatusMessage = "提交运行失败：" + ex.Message;
            await ThemedMessageBox.ErrorAsync(StatusMessage, "无法运行任务计划");
        }
    }
}
