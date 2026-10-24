using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BetterGenshinImpact.Pulonia.Services;

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

    /// <summary>
    /// 首次读取时深复制确认事件：其中的账本条目与运行内部共用，调用方修改返回对象不能污染已落盘的事实。
    /// </summary>
    private readonly Lazy<IReadOnlyList<PuloniaTaskConfirmedEffect>> _confirmedEffects;

    /// <summary>执行前核验的实际 UID，未准备账号或纯计算运行时为空。</summary>
    public string? VerifiedUid => _record.VerifiedUid;

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
    /// 本次运行逐次确认的副作用，包括执行器随后异常或进程中断前已提交的事实。
    /// </summary>
    public IReadOnlyList<PuloniaTaskConfirmedEffect> ConfirmedEffects => _confirmedEffects.Value;

    /// <summary>
    /// 本次运行使用的账号资料 ID。
    /// </summary>
    public string? AccountId => _record.Request.AccountId;

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
    /// 执行实例；为空表示在主实例本地执行。
    /// </summary>
    public PuloniaTaskExecutionTarget? ExecutionTarget => _record.Request.ExecutionTarget;

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
        _confirmedEffects = new Lazy<IReadOnlyList<PuloniaTaskConfirmedEffect>>(() =>
            new ReadOnlyCollection<PuloniaTaskConfirmedEffect>(record.ConfirmedEffects
                .Select(item => PuloniaTaskJson.Read<PuloniaTaskConfirmedEffect>(PuloniaTaskJson.Write(item))).ToArray()));
    }
}
