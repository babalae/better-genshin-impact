using System;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>
/// “任务计划”页面的步骤 3 运行提交；状态查看、取消与续跑移至共用执行记录视图模型。
/// </summary>
public partial class PuloniaTaskPlanViewModel
{
    /// <summary>
    /// 页面默认提交的计划总时限，单位秒。
    /// </summary>
    [ObservableProperty]
    private double _runTimeoutSeconds = 600;

    /// <summary>
    /// 计划内与全局页面复用的执行记录模型，不依赖可编辑的计划内容。
    /// </summary>
    public PuloniaTaskHistoryViewModel History { get; }

    /// <summary>
    /// 当前计划是否可以提交运行。
    /// </summary>
    public bool CanRunPlan => !IsBusy && SelectedDocument is not null;

    /// <summary>
    /// 保存当前计划后提交运行；提交时固定快照，随后编辑不会影响本次运行。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRunPlan))]
    private async Task RunPlanAsync()
    {
        var document = SelectedDocument;
        if (document is null)
            return;
        if (document.IsDirty && !await SaveDocumentAsync(document))
            return;

        try
        {
            var requestId = await _taskService.EnqueueAsync(new PuloniaTaskRequest
            {
                PlanId = document.Id,
                TimeoutSeconds = RunTimeoutSeconds,
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
