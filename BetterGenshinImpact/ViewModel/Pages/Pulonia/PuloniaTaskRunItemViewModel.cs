using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 把不可变 Pulonia 运行状态整理为页面可直接展示的只读条目。
/// 列表只展示状态、时间与结果摘要；运行信息与技术详情在选中后按需生成。
/// </summary>
public sealed class PuloniaTaskRunItemViewModel : ObservableObject
{
    /// <summary>
    /// 只在选中详情时解析原快照，避免刷新列表时反复展开全部历史；解析失败时为空。
    /// </summary>
    private readonly Lazy<PuloniaTaskSnapshot?> _snapshot;

    /// <summary>
    /// 快照中按稳定地址索引的全部节点，供节点详情、当前任务和续跑起点查找名称。
    /// </summary>
    private readonly Lazy<IReadOnlyDictionary<string, PuloniaTaskPreparedTask>> _snapshotTasks;

    /// <summary>
    /// 只在选中详情时生成的节点详情，不含未启用的节点。
    /// </summary>
    private readonly Lazy<IReadOnlyList<PuloniaTaskNodeResultViewModel>> _nodeDetails;

    /// <summary>
    /// 执行器注册的任务类型显示名称。
    /// </summary>
    private readonly IReadOnlyDictionary<string, string>? _typeNames;

    /// <summary>
    /// 账户 ID → 当前名称，用于显示运行账户；为空时退回快照中的账户资料或账户 ID。
    /// </summary>
    private readonly IReadOnlyDictionary<string, string>? _accountNames;

    /// <summary>
    /// 最新的不可变运行状态。
    /// </summary>
    public PuloniaTaskRunView Run { get; }

    /// <summary>
    /// 运行 ID；选择、取消和续跑都按此身份定位。
    /// </summary>
    public string RunId => Run.RunId;

    /// <summary>
    /// 计划名称。
    /// </summary>
    public string PlanName => Run.PlanName;

    /// <summary>
    /// 当前状态的中文显示文本；主实例下发的运行在连接断开期间显示“连接断开，状态未知”（统筹设计 6.3）。
    /// </summary>
    public string StatusText => IsDisconnected ? "连接断开，状态未知" : PuloniaTaskHistoryText.Status(Run.Status);

    /// <summary>
    /// 状态的语义分类键，供列表与详情按类别着色：running=进行中、success=成功、failed=失败、attention=需关注，其余显示为中性灰。
    /// 归类规则由 <see cref="PuloniaTaskRunClassifier.Categorize"/> 决定，这里只转换为视图触发器使用的键；连接断开时状态未知，显示为需关注。
    /// </summary>
    public string StatusKind => IsDisconnected
        ? PuloniaTaskHistoryText.CategoryKey(PuloniaTaskRunStatusCategory.Attention)
        : PuloniaTaskHistoryText.CategoryKey(PuloniaTaskRunClassifier.Categorize(Run.Status));

    /// <summary>
    /// 状态图标；排队显示时钟，已停止或已过期显示减号圆圈，连接断开时按需关注显示。
    /// </summary>
    public SymbolRegular StatusIcon => PuloniaTaskHistoryText.CategoryIcon(
        IsDisconnected ? PuloniaTaskRunStatusCategory.Attention : PuloniaTaskRunClassifier.Categorize(Run.Status),
        Run.Status == PuloniaTaskRunStatus.Queued ? SymbolRegular.Clock24 : SymbolRegular.SubtractCircle24);

    /// <summary>
    /// 主实例下发、尚未结束的运行与执行实例的连接已断开，最后收到的状态可能已经过时。
    /// </summary>
    public bool IsDisconnected => IsActive && StopAccess == PuloniaRunStopAccess.Disconnected;

    /// <summary>
    /// 列表中的短时间：已开始的运行取开始时间，排队中取提交时间。
    /// </summary>
    public string TimeText => PuloniaTaskHistoryText.ShortTime(Run.StartedAt ?? Run.SubmittedAt);

    /// <summary>
    /// 当前状态摘要或最终原因。
    /// </summary>
    public string Message => Run.Message;

    /// <summary>
    /// 详情头部显示的原因：运行中为当前任务，结束后为失败或停止原因；成功结束时状态已说明一切，不重复“计划运行完成”。
    /// </summary>
    public string? DetailMessage => Run.Status == PuloniaTaskRunStatus.Succeeded || string.IsNullOrWhiteSpace(Run.Message)
        ? null
        : Run.Message;

    /// <summary>
    /// 已完成的节点尝试结果。
    /// </summary>
    public IReadOnlyList<PuloniaTaskNodeResult> NodeResults => Run.NodeResults;

    /// <summary>
    /// 节点尝试详情，不含因自身或父组关闭而未执行的任务；按执行顺序排列，重试的每次尝试各占一行。
    /// </summary>
    public IReadOnlyList<PuloniaTaskNodeResultViewModel> NodeDetails => _nodeDetails.Value;

    /// <summary>
    /// 未启用而没有列出的任务数。
    /// </summary>
    public int DisabledTaskCount => NodeResults.Count(IsDisabled);

    /// <summary>
    /// 节点列表下方的说明；没有未启用任务时为空。
    /// </summary>
    public string? DisabledTaskText => DisabledTaskCount > 0 ? $"另有 {DisabledTaskCount} 项任务未启用，未列出。" : null;

    /// <summary>
    /// 结果摘要：按每个任务最后一次尝试归类计数，重试不会被重复计为不同任务，例如“成功 5 · 失败 1 · 重试 2 次”。
    /// </summary>
    public string? SummaryText
    {
        get
        {
            var counted = NodeResults.Where(item => !IsDisabled(item)).ToArray();
            if (counted.Length == 0)
                return null;
            var finals = counted.GroupBy(item => item.TaskAddress, StringComparer.Ordinal).Select(group => group.Last()).ToArray();
            var parts = finals
                .GroupBy(item => PuloniaTaskHistoryText.AttemptStatus(item).Bucket)
                .OrderBy(group => Array.IndexOf(PuloniaTaskHistoryText.BucketOrder, group.Key) is var index and >= 0 ? index : int.MaxValue)
                .Select(group => $"{group.Key} {group.Count()}")
                .ToList();
            // 只有实际执行过的尝试才算重试；跳过与续跑沿用的结果没有尝试次数
            var retries = counted.Count(item => item.Attempt > 1);
            if (retries > 0)
                parts.Add($"重试 {retries} 次");
            return string.Join(" · ", parts);
        }
    }

    /// <summary>
    /// 列表与详情头部的副标题：触发方式、开始时间与用时。
    /// </summary>
    public string SubtitleText
    {
        get
        {
            var parts = new List<string> { PuloniaTaskHistoryText.Source(Run.Source) };
            if (Run.StartedAt is { } started)
            {
                parts.Add(PuloniaTaskHistoryText.Time(started) + " 开始");
                if (Run.FinishedAt is { } finished)
                    parts.Add("用时 " + PuloniaTaskHistoryText.Duration(finished - started));
            }
            else
                parts.Add(PuloniaTaskHistoryText.Time(Run.SubmittedAt) + " 提交");
            return string.Join(" · ", parts);
        }
    }

    /// <summary>
    /// 存在需要人工核对的结果（已执行未确认、部分完成或待处理）时显示提示。
    /// </summary>
    public bool NeedsVerification => PuloniaTaskRunClassifier.HasUncertainAttempt(NodeResults);

    /// <summary>
    /// 列表中的账户说明：显示运行账户名称，UID 比对不一致时附上游戏中读到的 UID 尾号；
    /// 未确定运行账户，或目录只有一个账户且按本实例当前账户正常运行时为空，不打扰只有一个账户的用户。
    /// </summary>
    public string? AccountText
    {
        get
        {
            if (Run.AccountId is not { } accountId)
                return null;
            if (Run.UidCheck == PuloniaTaskUidCheck.Mismatched)
                return $"{GetAccountName(accountId)} · {ObservedUidText}";
            if (Run.AccountSource == PuloniaTaskAccountSource.InstanceCurrent && _accountNames is not { Count: > 1 })
                return null;
            return GetAccountName(accountId);
        }
    }

    /// <summary>跟随当前账户的运行开始时 UID 比对不一致；列表用提示色标出这次运行。</summary>
    public bool HasUidMismatch => Run.UidCheck == PuloniaTaskUidCheck.Mismatched;

    /// <summary>
    /// 运行信息：触发方式、账号、实例、执行范围、续跑来源与各时间点；只列出与本次运行相关的项。
    /// </summary>
    public IReadOnlyList<PuloniaTaskHistoryField> InfoFields
    {
        get
        {
            var fields = new List<PuloniaTaskHistoryField>
            {
                new("触发方式", PuloniaTaskHistoryText.Source(Run.Source)),
                new("运行账户", DescribeAccount())
            };
            // 历史目录由所有实例共用，总是标明执行实例
            fields.Add(new PuloniaTaskHistoryField("运行实例", InstanceText));
            fields.Add(new PuloniaTaskHistoryField("执行范围", Run.TargetTaskId is { } target
                ? "仅“" + (FindTaskName(task => task.TaskId == target || task.SourceTaskId == target) ?? "部分任务") + "”"
                : "整个计划"));
            if (Run.ResumedFromRunId is not null)
                fields.Add(new PuloniaTaskHistoryField("续跑", Run.ResumeFromTaskAddress is { } from
                    ? $"沿用之前一次运行的设置，从“{(_snapshotTasks.Value.TryGetValue(from, out var start) ? start.Name : from)}”开始"
                    : "沿用之前一次运行的设置，继续未完成的任务"));
            if (Run.OccurrenceUtc is { } occurrence)
                fields.Add(new PuloniaTaskHistoryField("触发时间", PuloniaTaskHistoryText.Time(occurrence)));
            if (Run.DeadlineUtc is { } deadline)
                fields.Add(new PuloniaTaskHistoryField("最晚开始", PuloniaTaskHistoryText.Time(deadline)));
            fields.Add(new PuloniaTaskHistoryField("提交时间", PuloniaTaskHistoryText.Time(Run.SubmittedAt)));
            fields.Add(new PuloniaTaskHistoryField("开始时间", PuloniaTaskHistoryText.Time(Run.StartedAt)));
            fields.Add(new PuloniaTaskHistoryField("结束时间", PuloniaTaskHistoryText.Time(Run.FinishedAt)));
            fields.Add(new PuloniaTaskHistoryField("用时", PuloniaTaskHistoryText.Duration(Run.StartedAt, Run.FinishedAt)));
            return fields;
        }
    }

    /// <summary>
    /// 排查问题用的内部标识：运行 ID、触发器、续跑来源、当前节点地址与本次路线 CD 记录。
    /// </summary>
    public IReadOnlyList<PuloniaTaskHistoryField> TechnicalFields
    {
        get
        {
            var fields = new List<PuloniaTaskHistoryField> { new("运行 ID", Run.RunId), new("来源", Run.Source) };
            if (Run.TriggerId is { } trigger)
                fields.Add(new PuloniaTaskHistoryField("触发器 ID", trigger));
            if (Run.TargetTaskId is { } target)
                fields.Add(new PuloniaTaskHistoryField("执行范围节点 ID", target));
            if (Run.ResumedFromRunId is { } from)
                fields.Add(new PuloniaTaskHistoryField("续跑来源运行 ID", from));
            if (Run.ResumeFromTaskAddress is { } start)
                fields.Add(new PuloniaTaskHistoryField("续跑起点地址", start));
            if (Run.CurrentTaskAddress is { } current)
                fields.Add(new PuloniaTaskHistoryField("当前或最近结束的节点地址", current));
            fields.AddRange(RouteCompletionFields);
            return fields;
        }
    }

    /// <summary>
    /// 本次完成的路线；没有时为空。
    /// </summary>
    public IReadOnlyList<PuloniaTaskHistoryField> RouteCompletionFields
        => Run.RouteCompletions.Select(PuloniaTaskNodeResultViewModel.CreateCompletionField).ToArray();

    /// <summary>
    /// 运行是否仍未结束（排队、运行中或停止中）；未结束时详情显示“停止”按钮，能否点击见 <see cref="CanCancel"/>。
    /// </summary>
    public bool IsActive => PuloniaTaskRunClassifier.IsActive(Run.Status);

    /// <summary>
    /// 本实例能否停止这个运行：本实例的运行，或主实例下发、连接正常的运行。
    /// </summary>
    public PuloniaRunStopAccess StopAccess { get; }

    /// <summary>
    /// 当前状态是否仍可从本实例请求取消；其他实例自己的运行和连接已断开的下发运行不可停止。
    /// </summary>
    public bool CanCancel => IsActive && StopAccess is PuloniaRunStopAccess.Local or PuloniaRunStopAccess.Dispatched;

    /// <summary>
    /// “停止”按钮的说明；不可停止时说明原因和去哪里停止。
    /// </summary>
    public string StopToolTip => StopAccess switch
    {
        PuloniaRunStopAccess.Dispatched => $"请求“{InstanceText}”停止这次运行；执行实例结束运行后状态会自动更新",
        PuloniaRunStopAccess.Disconnected => $"与“{InstanceText}”的连接已断开，无法远程停止，请在该实例的界面停止",
        PuloniaRunStopAccess.OtherInstance => $"这次运行在“{InstanceText}”执行，请在该实例界面停止",
        _ => "停止这次运行，并等待当前任务退出"
    };

    /// <summary>
    /// 列表中账户标签的说明；UID 比对不一致时说明进度仍按运行账户记录。
    /// </summary>
    public string AccountToolTip => HasUidMismatch
        ? "运行开始时游戏中读到的 UID 与本实例当前账户不一致，本次进度仍按该账户记录。请确认游戏中登录的账户。"
        : "本次运行的运行账户";

    /// <summary>
    /// 当前条目是否已经写入不可变历史。
    /// </summary>
    public bool IsHistorical => Run.IsHistorical;

    /// <summary>
    /// 当前历史是否可以沿用原快照继续。
    /// </summary>
    public bool CanResume => IsHistorical && Run.Status != PuloniaTaskRunStatus.Succeeded;

    /// <summary>
    /// 执行实例的显示名：主实例、桌面分身或“云原神 · 实例名”。
    /// </summary>
    public string InstanceText => PuloniaInstanceKey.Describe(Run.Instance);

    /// <summary>
    /// 建立页面运行条目。
    /// </summary>
    /// <param name="run">运行状态。</param>
    /// <param name="typeNames">任务类型显示名称。</param>
    /// <param name="accountNames">账户 ID → 名称；为空时只用快照中的账户资料或账户 ID 显示。</param>
    /// <param name="stopAccess">未结束时能否从本实例停止；默认是本实例的运行。</param>
    public PuloniaTaskRunItemViewModel(PuloniaTaskRunView run,
        IReadOnlyDictionary<string, string>? typeNames = null, IReadOnlyDictionary<string, string>? accountNames = null,
        PuloniaRunStopAccess stopAccess = PuloniaRunStopAccess.Local)
    {
        Run = run;
        _typeNames = typeNames;
        _accountNames = accountNames;
        StopAccess = stopAccess;
        _snapshot = new Lazy<PuloniaTaskSnapshot?>(ReadSnapshot);
        _snapshotTasks = new Lazy<IReadOnlyDictionary<string, PuloniaTaskPreparedTask>>(IndexSnapshotTasks);
        _nodeDetails = new Lazy<IReadOnlyList<PuloniaTaskNodeResultViewModel>>(CreateNodeDetails);
    }

    /// <summary>
    /// 节点是否因自身或父组关闭而未执行。
    /// </summary>
    private static bool IsDisabled(PuloniaTaskNodeResult result) => result.ReasonCode == PuloniaTaskRunClassifier.DisabledReason;

    /// <summary>
    /// 运行账户说明：名称 · 来源（本实例当前账户或计划指定）· UID 核验或比对结果，
    /// 例如“乙 · 本实例当前账户 · UID 一致”“丙 · 计划指定 · 已核验 UID”“甲 · 本实例当前账户 · 游戏中读到尾号 2222”（统筹设计 9.6）。
    /// </summary>
    private string DescribeAccount()
    {
        if (Run.AccountId is not { } accountId)
            return Run.Status == PuloniaTaskRunStatus.Queued ? "运行开始时确定" : "未确定（本次运行不含游戏任务或未开始执行）";
        var parts = new List<string> { GetAccountName(accountId) };
        switch (Run.AccountSource)
        {
            case PuloniaTaskAccountSource.Specified:
                parts.Add("计划指定");
                parts.Add(Run.VerifiedUid is not null ? "已核验 UID" : "UID 未核验");
                break;
            case PuloniaTaskAccountSource.InstanceCurrent:
                parts.Add("本实例当前账户");
                parts.Add(Run.UidCheck switch
                {
                    PuloniaTaskUidCheck.Matched => "UID 一致",
                    PuloniaTaskUidCheck.Mismatched => ObservedUidText,
                    _ => "未比对 UID"
                });
                break;
        }
        return string.Join(" · ", parts);
    }

    /// <summary>
    /// UID 比对不一致时游戏中读到的 UID，只显示尾号，如“游戏中读到尾号 2222”；没有记录读到的 UID 时说明不一致。
    /// </summary>
    private string ObservedUidText => Run.ObservedUid is { Length: > 0 } observed
        ? "游戏中读到尾号 " + PuloniaAccountChoiceViewModel.UidTail(observed)
        : "游戏中读到的 UID 不一致";

    /// <summary>
    /// 运行账户的显示名称：优先使用账户目录中的当前名称，其次是快照固定的账户资料，最后显示账户 ID。
    /// </summary>
    /// <param name="accountId">运行账户 ID。</param>
    private string GetAccountName(string accountId)
    {
        if (_accountNames?.TryGetValue(accountId, out var name) == true)
            return name;
        try
        {
            if (_snapshot.Value?.GenshinAccountJson is { } json
                && PuloniaTaskJson.Read<PuloniaGenshinAccountProfile>(json) is { } account && account.Id == accountId)
                return account.Name;
        }
        catch (Exception ex) when (ex is Newtonsoft.Json.JsonException or PuloniaTaskValidationException)
        {
            // 账户资料无法读取时显示账户 ID，不影响其他运行信息。
        }
        return "账户 " + accountId;
    }

    /// <summary>
    /// 在快照中按条件查找第一个节点的名称；快照不可读时为空。
    /// </summary>
    private string? FindTaskName(Func<PuloniaTaskPreparedTask, bool> predicate)
        => _snapshotTasks.Value.Values.FirstOrDefault(predicate)?.Name;

    /// <summary>
    /// 读取当次快照；旧快照或解析失败只影响设置与名称显示，不据当前编辑内容补造历史参数。
    /// </summary>
    private PuloniaTaskSnapshot? ReadSnapshot()
    {
        try
        {
            return string.IsNullOrWhiteSpace(Run.SnapshotJson) ? null : PuloniaTaskJson.ReadSnapshot(Run.SnapshotJson);
        }
        catch (Exception ex) when (ex is Newtonsoft.Json.JsonException or PuloniaTaskValidationException)
        {
            return null;
        }
    }

    /// <summary>
    /// 按稳定地址索引完整展开快照，重名节点和重试不会互相覆盖参数。
    /// </summary>
    private IReadOnlyDictionary<string, PuloniaTaskPreparedTask> IndexSnapshotTasks()
    {
        var tasks = new Dictionary<string, PuloniaTaskPreparedTask>(StringComparer.Ordinal);
        if (_snapshot.Value is { } snapshot)
            AddSnapshotTasks(snapshot.RootTask, tasks);
        return tasks;
    }

    /// <summary>
    /// 把节点结果与当次快照匹配；快照解析失败不会让已保存的执行结果失去查看入口。
    /// </summary>
    private IReadOnlyList<PuloniaTaskNodeResultViewModel> CreateNodeDetails()
        => NodeResults.Where(result => !IsDisabled(result)).Select(result => new PuloniaTaskNodeResultViewModel(result,
            _snapshotTasks.Value.GetValueOrDefault(result.TaskAddress),
            result.TaskType == "genshin.account.prepare" ? "切换账户" : _typeNames?.GetValueOrDefault(result.TaskType) ?? result.TaskType,
            Run.RouteCompletions)).ToArray();

    /// <summary>
    /// 递归登记快照节点。
    /// </summary>
    private static void AddSnapshotTasks(PuloniaTaskPreparedTask task, Dictionary<string, PuloniaTaskPreparedTask> tasks)
    {
        tasks[task.TaskAddress] = task;
        foreach (var child in task.Children)
            AddSnapshotTasks(child, tasks);
    }
}
