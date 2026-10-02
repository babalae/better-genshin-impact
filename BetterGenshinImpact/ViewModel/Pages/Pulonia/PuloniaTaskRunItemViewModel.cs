using System;
using System.Collections.Generic;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 把不可变 Pulonia 运行状态整理为页面可直接展示的只读条目。
/// </summary>
public sealed class PuloniaTaskRunItemViewModel
{
    /// <summary>
    /// 最新的不可变运行状态。
    /// </summary>
    public PuloniaTaskRunView Run { get; }

    /// <summary>
    /// 请求 ID。
    /// </summary>
    public Guid RequestId => Run.RequestId;

    /// <summary>
    /// 计划名称。
    /// </summary>
    public string PlanName => Run.PlanName;

    /// <summary>
    /// 当前状态的中文显示文本。
    /// </summary>
    public string StatusText => Run.Status switch
    {
        PuloniaTaskRunStatus.Queued => "排队中",
        PuloniaTaskRunStatus.Running => "运行中",
        PuloniaTaskRunStatus.Cancelling => "停止中",
        PuloniaTaskRunStatus.Succeeded => "已成功",
        PuloniaTaskRunStatus.Failed => "已失败",
        PuloniaTaskRunStatus.Cancelled => "已取消",
        PuloniaTaskRunStatus.TimedOut => "已超时",
        _ => Run.Status.ToString()
    };

    /// <summary>
    /// 按本地时区显示的提交时间。
    /// </summary>
    public string SubmittedAtText => Run.SubmittedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>
    /// 当前状态摘要。
    /// </summary>
    public string Message => Run.Message;

    /// <summary>
    /// 当前节点地址；空值显示为无。
    /// </summary>
    public string CurrentTaskAddress => Run.CurrentTaskAddress ?? "—";

    /// <summary>
    /// 已完成的节点尝试结果。
    /// </summary>
    public IReadOnlyList<PuloniaTaskNodeResult> NodeResults => Run.NodeResults;

    /// <summary>
    /// 当前状态是否仍可请求取消。
    /// </summary>
    public bool CanCancel => Run.Status is PuloniaTaskRunStatus.Queued
        or PuloniaTaskRunStatus.Running or PuloniaTaskRunStatus.Cancelling;

    /// <summary>
    /// 建立页面运行条目。
    /// </summary>
    public PuloniaTaskRunItemViewModel(PuloniaTaskRunView run)
    {
        Run = run;
    }
}
