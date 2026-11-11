using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 提供给页面和进程内调用方的不可变运行状态视图。
/// 包装一份由任务服务新建、此后不再修改的运行记录，属性直接读取记录与其中的请求，不逐字段平铺复制。
/// </summary>
public sealed class PuloniaTaskRunView
{
    /// <summary>
    /// 视图包装的运行记录；由任务服务为本视图新建（或取自不再变化的历史记录），任何一方都不修改。
    /// </summary>
    private readonly PuloniaTaskRunRecord _record;

    /// <summary>
    /// 常驻历史不保存快照正文，首次读取 <see cref="SnapshotJson"/> 时从历史文件加载；当前运行为空，直接使用记录中的快照。
    /// </summary>
    private readonly Lazy<string>? _snapshotJson;

    /// <summary>指定账户的运行在执行前核验的实际 UID；跟随当前账户、未确定运行账户或纯计算运行时为空。</summary>
    public string? VerifiedUid => _record.VerifiedUid;

    /// <summary>运行账户 ID；不含游戏节点或尚未确定时为空。</summary>
    public string? AccountId => _record.AccountId;

    /// <summary>运行账户的来源：实例当前账户或计划指定；未确定时为空。</summary>
    public PuloniaTaskAccountSource? AccountSource => _record.AccountSource;

    /// <summary>跟随当前账户的运行开始时的 UID 比对结果；指定账户或未确定时为空。</summary>
    public PuloniaTaskUidCheck? UidCheck => _record.UidCheck;

    /// <summary>UID 比对不一致时游戏中读到的 UID；其他情况为空。</summary>
    public string? ObservedUid => _record.ObservedUid;

    /// <summary>
    /// 本次运行 ID，提交时生成；查询、取消、历史和续跑都使用此身份关联。
    /// </summary>
    public string RunId => _record.RunId;

    /// <summary>
    /// 入口计划 ID。
    /// </summary>
    public string PlanId => _record.Request.PlanId;

    /// <summary>
    /// 入口计划名称，取自提交时固定的计划 JSON。
    /// </summary>
    public string PlanName => _record.PlanName;

    /// <summary>
    /// 调用来源短文本。
    /// </summary>
    public string Source => _record.Request.Source;
    /// <summary>触发器身份，人工请求为空。</summary>
    public string? TriggerId => _record.Request.TriggerId;
    /// <summary>本次请求限定的子树。</summary>
    public string? TargetTaskId => _record.Request.TargetTaskId;
    /// <summary>定时/热键实际发生时刻。</summary>
    public DateTimeOffset? OccurrenceUtc => _record.Request.OccurrenceUtc;
    /// <summary>自动请求的最晚开始时间。</summary>
    public DateTimeOffset? DeadlineUtc => _record.Request.DeadlineUtc;

    /// <summary>
    /// 当前运行状态。
    /// </summary>
    public PuloniaTaskRunStatus Status => _record.Status;

    /// <summary>
    /// 当前正在执行或最近结束的节点地址。
    /// </summary>
    public string? CurrentTaskAddress => _record.CurrentTaskAddress;

    /// <summary>
    /// 当前状态摘要或最终原因。
    /// </summary>
    public string Message => _record.Message;

    /// <summary>
    /// 请求提交并完成快照固定的时间，使用 UTC。
    /// </summary>
    public DateTimeOffset SubmittedAt => _record.SubmittedAt;

    /// <summary>
    /// 真正从串行队列开始执行的时间，尚未开始时为 null。
    /// </summary>
    public DateTimeOffset? StartedAt => _record.StartedAt;

    /// <summary>
    /// 确认执行器退出后的完成时间，未完成时为 null。
    /// </summary>
    public DateTimeOffset? FinishedAt => _record.FinishedAt;

    /// <summary>
    /// 提交时固定的完整运行快照 JSON，用于验证编辑隔离。
    /// 已归档历史首次读取时同步读取历史文件；文件已不可读时为空字符串。
    /// </summary>
    public string SnapshotJson => _snapshotJson?.Value ?? _record.SnapshotJson;

    /// <summary>
    /// 已结束的节点尝试结果。
    /// </summary>
    public IReadOnlyList<PuloniaTaskNodeResult> NodeResults { get; }

    /// <summary>
    /// 本次完成的路线，包括执行器随后异常或进程中断前已落盘的记录。完成记录的属性只能在初始化时设置，
    /// 与运行内部共用同一对象也不会被调用方修改。
    /// </summary>
    public IReadOnlyList<PuloniaRouteCompletion> RouteCompletions { get; }

    /// <summary>
    /// 来源历史运行 ID。
    /// </summary>
    public string? ResumedFromRunId => _record.ResumedFromRunId;

    /// <summary>
    /// 显式续跑起点。
    /// </summary>
    public string? ResumeFromTaskAddress => _record.ResumeFromTaskAddress;

    /// <summary>
    /// 是否已经写入不可变历史。
    /// </summary>
    public bool IsHistorical { get; }

    /// <summary>
    /// 执行实例键（<c>primary</c>、<c>child_session</c>、<c>webview:&lt;小写实例名&gt;</c>），界面用 <see cref="PuloniaInstanceKey.Describe"/> 显示。
    /// </summary>
    public string Instance => _record.Instance;

    /// <summary>
    /// 是否为主实例一步下发到本实例的运行（来源为 <see cref="PuloniaTaskRequest.DispatchSource"/>）。
    /// </summary>
    public bool IsDispatched => _record.Request.Source == PuloniaTaskRequest.DispatchSource;

    /// <summary>
    /// 固定后的调用请求，供服务内部（触发反馈、续跑）读取；调用方不能修改。
    /// </summary>
    internal PuloniaTaskRequest Request => _record.Request;

    /// <summary>
    /// 包装一份专属于本视图、此后不再修改的运行记录。
    /// </summary>
    /// <param name="record">运行记录；集合不能与仍在变化的运行状态共用。</param>
    /// <param name="isHistorical">是否已经写入不可变历史。</param>
    /// <param name="loadSnapshotJson">常驻历史没有快照正文时按需读取快照的方法；为空时使用记录中的快照。</param>
    internal PuloniaTaskRunView(PuloniaTaskRunRecord record, bool isHistorical, Func<string>? loadSnapshotJson = null)
    {
        _record = record;
        IsHistorical = isHistorical;
        // 只读包装：调用方不能增删节点结果；节点结果本身构造后不可变。
        NodeResults = new ReadOnlyCollection<PuloniaTaskNodeResult>(record.NodeResults);
        _snapshotJson = loadSnapshotJson is null ? null : new Lazy<string>(loadSnapshotJson);
        RouteCompletions = new ReadOnlyCollection<PuloniaRouteCompletion>(record.RouteCompletions);
    }
}
