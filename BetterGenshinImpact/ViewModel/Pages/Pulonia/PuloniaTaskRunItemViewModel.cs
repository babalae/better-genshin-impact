using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 把不可变 Pulonia 运行状态整理为页面可直接展示的只读条目。
/// </summary>
public sealed class PuloniaTaskRunItemViewModel : ObservableObject
{
    /// <summary>
    /// 只在选中详情时解析原快照，避免刷新列表时反复展开全部历史。
    /// </summary>
    private readonly Lazy<IReadOnlyList<PuloniaTaskNodeResultViewModel>> _nodeDetails;

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
        PuloniaTaskRunStatus.Succeeded => "已完成",
        PuloniaTaskRunStatus.Failed => "已失败",
        PuloniaTaskRunStatus.Cancelled => "已取消",
        PuloniaTaskRunStatus.TimedOut => "已超时",
        PuloniaTaskRunStatus.Interrupted => "已中断",
        PuloniaTaskRunStatus.NeedsAttention => "待处理",
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
    /// 类型专属的节点尝试详情。
    /// </summary>
    public IReadOnlyList<PuloniaTaskNodeResultViewModel> NodeDetails => _nodeDetails.Value;

    /// <summary>
    /// 来源、开始/结束、耗时和续跑关联等可追溯信息。
    /// </summary>
    public IReadOnlyList<PuloniaTaskHistoryField> OverviewFields =>
    [
        new("运行摘要", Message),
        new("运行 ID", Run.RunId.ToString("D")),
        new("请求 ID", RequestId.ToString("D")),
        new("来源 / 作用域", $"{Run.Source} · {ScopeText}"),
        new("提交 / 开始 / 结束", $"{SubmittedAtText} / {PuloniaTaskHistoryText.Time(Run.StartedAt)} / {PuloniaTaskHistoryText.Time(Run.FinishedAt)}"),
        new("执行耗时", PuloniaTaskHistoryText.Duration(Run.StartedAt, Run.FinishedAt)),
        new("续跑关联", Run.ResumedFromRunId is { } from
            ? $"来源运行：{from:D}\n起点：{Run.ResumeFromTaskAddress ?? "未完成节点"}" : "首次运行")
    ];

    /// <summary>
    /// 已结束尝试的业务结果汇总，重试尝试不冒充已完成的不同节点数。
    /// </summary>
    public string ResultSummary => $"{NodeResults.Count} 次尝试 · "
        + string.Join(" · ", NodeResults.GroupBy(item => item.OutcomeKind)
            .Select(group => $"{PuloniaTaskHistoryText.Outcome(group.Key)} {group.Count()}"));

    /// <summary>
    /// 存在不确定业务结果时显示人工核验提示。
    /// </summary>
    public bool NeedsVerification => Run.HasUncertainOperation || NodeResults.Any(item =>
        item.OutcomeKind is PuloniaTaskOutcomeKind.ExecutedUnverified or PuloniaTaskOutcomeKind.PartiallySucceeded
            or PuloniaTaskOutcomeKind.NeedsAttention);

    /// <summary>
    /// 本次运行的确认事件，即使对应节点在结果落盘前中断也可以追溯。
    /// </summary>
    public IReadOnlyList<PuloniaTaskHistoryField> ConfirmedEffectFields => Run.ConfirmedEffects.Count == 0
        ? [new("已确认副作用", "本次记录未留存确认事件；旧历史的空集合不表示实际未发生副作用。")]
        : Run.ConfirmedEffects.Select(PuloniaTaskNodeResultViewModel.CreateEffectField).ToArray();

    /// <summary>
    /// 当前状态是否仍可请求取消。
    /// </summary>
    public bool CanCancel => Run.Status is PuloniaTaskRunStatus.Queued
        or PuloniaTaskRunStatus.Running or PuloniaTaskRunStatus.Cancelling;

    /// <summary>
    /// 当前条目是否已经写入不可变历史。
    /// </summary>
    public bool IsHistorical => Run.IsHistorical;

    /// <summary>
    /// 当前历史是否可以沿用原快照继续。
    /// </summary>
    public bool CanResume => IsHistorical && Run.Status != PuloniaTaskRunStatus.Succeeded;

    /// <summary>
    /// 运行账号与世界作用域摘要。
    /// </summary>
    public string ScopeText => $"账号：{Run.AccountId ?? "默认"} · 世界：{Run.WorldOwnerAccountId ?? Run.AccountId ?? "默认"}";

    /// <summary>
    /// 条目来自当前状态还是不可变历史。
    /// </summary>
    public string StorageText => IsHistorical ? "本地历史" : "当前运行（状态已保存）";

    /// <summary>
    /// 建立页面运行条目。
    /// </summary>
    public PuloniaTaskRunItemViewModel(PuloniaTaskRunView run,
        IReadOnlyDictionary<string, string>? typeNames = null)
    {
        Run = run;
        _nodeDetails = new Lazy<IReadOnlyList<PuloniaTaskNodeResultViewModel>>(() => CreateNodeDetails(typeNames));
    }

    /// <summary>
    /// 把节点结果与当次快照匹配；快照解析失败不会让已保存的执行结果失去查看入口。
    /// </summary>
    private IReadOnlyList<PuloniaTaskNodeResultViewModel> CreateNodeDetails(IReadOnlyDictionary<string, string>? typeNames)
    {
        var tasks = new Dictionary<string, PuloniaTaskPreparedTask>(StringComparer.Ordinal);
        try
        {
            AddSnapshotTasks(PuloniaTaskJson.ReadSnapshot(Run.SnapshotJson).RootTask, tasks);
        }
        catch (Exception ex) when (ex is Newtonsoft.Json.JsonException or PuloniaTaskValidationException)
        {
            // 旧快照或不匹配节点只影响输入显示，不据当前编辑内容补造历史参数。
        }
        return NodeResults.Select(result => new PuloniaTaskNodeResultViewModel(result,
            tasks.GetValueOrDefault(result.TaskAddress),
            typeNames?.GetValueOrDefault(result.TaskType) ?? result.TaskType, Run.ConfirmedEffects)).ToArray();
    }

    /// <summary>
    /// 按稳定地址索引完整展开快照，重名节点和重试不会互相覆盖参数。
    /// </summary>
    private static void AddSnapshotTasks(PuloniaTaskPreparedTask task, Dictionary<string, PuloniaTaskPreparedTask> tasks)
    {
        tasks[task.TaskAddress] = task;
        foreach (var child in task.Children)
            AddSnapshotTasks(child, tasks);
    }
}
