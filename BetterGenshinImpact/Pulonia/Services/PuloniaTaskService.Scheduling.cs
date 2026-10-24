using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>与执行队列共享 state.json 的触发准入与游标推进。</summary>
public sealed partial class PuloniaTaskService
{
    /// <summary>首轮检查（程序刚启动或调度中断后）仍按准点处理的最大迟到。</summary>
    private static readonly TimeSpan OnTimeTolerance = TimeSpan.FromSeconds(5);
    /// <summary>两轮检查之间允许的最大空档；超过视为系统睡眠或调度停顿，期间到点的日程按离线遗漏处理。</summary>
    private static readonly TimeSpan MaxPollingGap = TimeSpan.FromSeconds(30);
    /// <summary>同一热键两次触发的最小间隔，毫秒。</summary>
    private const long HotkeyDebounceMilliseconds = 1000;

    /// <summary>可注入的 UTC 时钟，测试不依赖真实计时。</summary>
    private readonly TimeProvider _timeProvider;
    /// <summary>串行处理定时与热键事件，避免重复准备同一 occurrence；也是触发游标的唯一写入路径。</summary>
    private readonly SemaphoreSlim _triggerGate = new(1, 1);
    /// <summary>
    /// 各触发器最近一次处理的运行期反馈，只在内存中维护；由持有 <see cref="_triggerGate"/> 的代码写入，
    /// 读写字典本身时锁定该字典。
    /// </summary>
    private readonly Dictionary<(string PlanId, string TriggerId), PuloniaTaskTriggerStatus> _triggerStatuses = [];
    /// <summary>
    /// 已按运行记录同步过反馈的触发配置版本，只在 <see cref="_triggerGate"/> 内访问；
    /// 避免从未触发过的热键每秒都扫描一遍运行记录。
    /// </summary>
    private readonly Dictionary<(string PlanId, string TriggerId), DateTimeOffset> _syncedTriggerVersions = [];
    /// <summary>各热键最近一次被接受时的单调计时（<see cref="Environment.TickCount64"/>），只在 <see cref="_triggerGate"/> 内访问。</summary>
    private readonly Dictionary<(string PlanId, string TriggerId), long> _hotkeyAcceptedAt = [];
    /// <summary>上一轮成功完成的定时检查所用的检查时刻；本进程尚未完成检查时为空。</summary>
    private DateTimeOffset? _lastScanAt;
    /// <summary>上一轮定时检查结束时的时钟读数，用于判断两轮之间是否持续轮询。</summary>
    private DateTimeOffset _lastScanEndedAt;

    /// <summary>取得触发器运行期反馈的独立副本；关联运行的状态与消息按当前运行记录读取，不访问 state.json。</summary>
    public IReadOnlyList<PuloniaTaskTriggerStatus> GetTriggerStatuses()
    {
        PuloniaTaskTriggerStatus[] statuses;
        lock (_triggerStatuses)
            statuses = _triggerStatuses.Values.ToArray();
        return Array.ConvertAll(statuses, ResolveRunProgress);
    }

    /// <summary>检查当前配置；每次恢复只合并仍可延迟启动的最新一次，不补跑整段离线历史。</summary>
    /// <param name="plans">当前全部可读取的计划；不在其中的计划或定时触发器的游标会被剔除，调用方不能只传部分计划。</param>
    /// <param name="unreadablePlanIds">本轮读取失败（文件损坏）的计划 ID；它们的游标与运行反馈保留不动，修复后不会重放已处理的时刻。</param>
    /// <param name="ct">取消信号。</param>
    public async Task CheckTriggersAsync(IReadOnlyList<PuloniaTaskPlan> plans,
        IReadOnlyCollection<string>? unreadablePlanIds = null, CancellationToken ct = default)
    {
        ThrowIfNotCoordinator();
        await _initialization.WaitAsync(ct).ConfigureAwait(false);
        _runState.ThrowIfFailed();
        var unreadable = unreadablePlanIds is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : unreadablePlanIds.ToHashSet(StringComparer.Ordinal);
        // 在等待触发门之前记录请求时刻：热键或其他触发占用触发门的时间属于在线忙碌，不算调度空档。
        var requestedAt = _timeProvider.GetUtcNow();
        await _triggerGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = _timeProvider.GetUtcNow();
            // 与上一轮之间持续轮询时，上一轮检查时刻之后才到点的日程发生在程序在线期间，本轮因忙碌晚到也不算遗漏。
            var observedSince = _lastScanAt is { } lastScan && requestedAt - _lastScanEndedAt <= MaxPollingGap
                ? lastScan
                : (DateTimeOffset?)null;
            var cursors = ReadTriggerCursors();
            await PruneOrphanCursorsAsync(plans, unreadable, cursors, ct).ConfigureAwait(false);
            SyncTriggerStatuses(plans, unreadable);
            foreach (var plan in plans)
            foreach (var trigger in plan.Triggers.Where(item => item.Enabled && item.Kind == PuloniaTaskTriggerKind.Schedule))
            {
                // 生成游标或在配置变更时重置；禁用前的等待不能误用于新配置。
                // 新游标只在处理了发生时刻后才写盘，重启时会按同一生效时间重建出相同的起点。
                if (!cursors.TryGetValue((plan.Id, trigger.Id), out var cursor)
                    || cursor.TriggerActivatedAtUtc != trigger.ActivatedAtUtc)
                    cursor = new PuloniaTaskTriggerState
                    {
                        PlanId = plan.Id, TriggerId = trigger.Id, TriggerActivatedAtUtc = trigger.ActivatedAtUtc
                    };
                var floor = cursor.LastOccurrenceUtc ?? trigger.ActivatedAtUtc.AddTicks(-1);
                var due = PuloniaTaskSchedule.Latest(trigger, now, floor);
                if (due is null)
                {
                    // 长时间离线只推进到现在；下次时间始终严格晚于已处理游标。
                    var next = PuloniaTaskSchedule.Next(trigger, floor);
                    if (next <= now)
                    {
                        await AdvanceCursorAsync(cursor, now, ct).ConfigureAwait(false);
                        SetTriggerStatus(plan, trigger, PuloniaTaskTriggerOutcome.Missed,
                            "遗漏的时间已超过允许延迟启动时间，未启动任务。", PuloniaTaskSchedule.Next(trigger, now));
                    }
                    else
                        SetNextOccurrence(plan, trigger, next);
                    continue;
                }
                // 不补离线遗漏，但程序在线期间到点的事件仍会提交。
                if (!trigger.CatchUp && IsMissedWithoutCatchUp(due.Value, now, observedSince))
                {
                    await AdvanceCursorAsync(cursor, due.Value, ct).ConfigureAwait(false);
                    SetTriggerStatus(plan, trigger, PuloniaTaskTriggerOutcome.Missed,
                        "未启用补触发，已跳过遗漏时间。", PuloniaTaskSchedule.Next(trigger, due.Value));
                    continue;
                }
                await SubmitTriggerAsync(plan, trigger, cursor, due.Value, ct).ConfigureAwait(false);
            }
            _lastScanAt = now;
            _lastScanEndedAt = _timeProvider.GetUtcNow();
        }
        finally { _triggerGate.Release(); }
    }

    /// <summary>热键仍使用相同准入和忙碌策略，不旁路快照、账本或单消费者；热键不写调度游标。</summary>
    public async Task FireHotkeyAsync(string planId, string triggerId, CancellationToken ct = default)
    {
        ThrowIfNotCoordinator();
        // 1 秒去抖只在内存里按单调计时判断：时钟回拨不会吞掉热键，也不为去抖写 state.json。
        var pressedAt = Environment.TickCount64;
        await _initialization.WaitAsync(ct).ConfigureAwait(false);
        await _triggerGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var key = (planId, triggerId);
            if (_hotkeyAcceptedAt.TryGetValue(key, out var last) && pressedAt - last < HotkeyDebounceMilliseconds) return;
            var plan = await _store.LoadPlanAsync(planId, ct).ConfigureAwait(false);
            var trigger = plan?.Triggers.FirstOrDefault(item => item.Id == triggerId && item.Enabled
                && item.Kind == PuloniaTaskTriggerKind.Hotkey);
            if (plan is null || trigger is null) return;
            _hotkeyAcceptedAt[key] = pressedAt;
            await SubmitTriggerAsync(plan, trigger, null, _timeProvider.GetUtcNow(), ct).ConfigureAwait(false);
        }
        finally { _triggerGate.Release(); }
    }

    /// <summary>
    /// 判断未启用补触发时发生时刻是否算遗漏：本轮检查晚于发生时刻不超过容差，
    /// 或发生在持续轮询的上一轮检查之后，都按在线到点处理。
    /// </summary>
    /// <param name="due">本轮发现的发生时刻。</param>
    /// <param name="now">本轮检查时刻。</param>
    /// <param name="observedSince">持续轮询时上一轮的检查时刻；首轮或调度中断（如系统睡眠）后为空。</param>
    internal static bool IsMissedWithoutCatchUp(DateTimeOffset due, DateTimeOffset now, DateTimeOffset? observedSince)
        => now - due > OnTimeTolerance && !(observedSince is { } since && due > since);

    /// <summary>
    /// 复制当前已提交的游标，不为每秒检查整份克隆 state.json；已提交状态不再修改，无需等待状态门。
    /// 游标只由持有触发门的代码修改，本轮内读到的游标不会被其他路径改写。
    /// </summary>
    private Dictionary<(string PlanId, string TriggerId), PuloniaTaskTriggerState> ReadTriggerCursors()
        => _runState.Current.TriggerStates.ToDictionary(item => (item.PlanId, item.TriggerId), item => item.Clone());

    /// <summary>
    /// 剔除已删除计划或触发器（含旧版写下的热键游标）的游标，避免长期使用后超过上限；只在确有孤儿时写盘。
    /// 停用的定时触发器保留游标：撤销编辑恢复旧配置版本时仍能防止重放。
    /// 本轮读取失败的计划无法判断其触发器是否还在，游标一律保留。
    /// </summary>
    private async Task PruneOrphanCursorsAsync(IReadOnlyList<PuloniaTaskPlan> plans, IReadOnlySet<string> unreadablePlanIds,
        Dictionary<(string PlanId, string TriggerId), PuloniaTaskTriggerState> cursors, CancellationToken ct)
    {
        var valid = plans.SelectMany(plan => plan.Triggers.Where(trigger => trigger.Kind == PuloniaTaskTriggerKind.Schedule)
            .Select(trigger => (plan.Id, trigger.Id))).ToHashSet();
        bool IsOrphan((string PlanId, string TriggerId) key) => !valid.Contains(key) && !unreadablePlanIds.Contains(key.PlanId);
        var orphans = cursors.Keys.Where(IsOrphan).ToArray();
        if (orphans.Length == 0) return;
        await PersistStateAsync(ct, state => state.TriggerStates.RemoveAll(item
            => IsOrphan((item.PlanId, item.TriggerId)))).ConfigureAwait(false);
        foreach (var key in orphans) cursors.Remove(key);
    }

    /// <summary>
    /// 让内存反馈与当前配置一致：删除已删除或停用的触发器；新触发器或新配置版本从运行记录推导最近一次请求，
    /// 定时触发器没有运行记录时显示等待，热键在首次触发前没有反馈。读取失败的计划保留原有反馈。
    /// </summary>
    private void SyncTriggerStatuses(IReadOnlyList<PuloniaTaskPlan> plans, IReadOnlySet<string> unreadablePlanIds)
    {
        var enabled = plans.SelectMany(plan => plan.Triggers.Where(trigger => trigger.Enabled)
            .Select(trigger => (Plan: plan, Trigger: trigger))).ToArray();
        var keys = enabled.Select(item => (item.Plan.Id, item.Trigger.Id)).ToHashSet();
        bool IsStale((string PlanId, string TriggerId) key) => !keys.Contains(key) && !unreadablePlanIds.Contains(key.PlanId);
        foreach (var key in _syncedTriggerVersions.Keys.Where(IsStale).ToArray())
            _syncedTriggerVersions.Remove(key);
        lock (_triggerStatuses)
        {
            foreach (var key in _triggerStatuses.Keys.Where(IsStale).ToArray())
                _triggerStatuses.Remove(key);
        }
        foreach (var (plan, trigger) in enabled)
        {
            var key = (plan.Id, trigger.Id);
            if (_syncedTriggerVersions.TryGetValue(key, out var synced) && synced == trigger.ActivatedAtUtc)
                continue;
            _syncedTriggerVersions[key] = trigger.ActivatedAtUtc;
            lock (_triggerStatuses)
            {
                // 热键可能已按存储中的新配置触发过，保留与当前配置版本一致的反馈。
                if (_triggerStatuses.TryGetValue(key, out var current)
                    && current.TriggerActivatedAtUtc == trigger.ActivatedAtUtc)
                    continue;
                _triggerStatuses.Remove(key);
            }
            if (FindLatestTriggerRunId(plan.Id, trigger) is { } runId)
                SetTriggerStatus(plan, trigger, PuloniaTaskTriggerOutcome.Submitted, string.Empty, null, runId);
            else if (trigger.Kind == PuloniaTaskTriggerKind.Schedule)
                SetTriggerStatus(plan, trigger, PuloniaTaskTriggerOutcome.Waiting, "配置已生效。", null);
        }
    }

    /// <summary>记录触发器最近一次处理结果，替换旧反馈。</summary>
    private void SetTriggerStatus(PuloniaTaskPlan plan, PuloniaTaskTrigger trigger, PuloniaTaskTriggerOutcome outcome,
        string message, DateTimeOffset? nextOccurrence, string? runId = null)
    {
        var status = new PuloniaTaskTriggerStatus
        {
            PlanId = plan.Id, TriggerId = trigger.Id, TriggerActivatedAtUtc = trigger.ActivatedAtUtc,
            Outcome = outcome, Message = message, RunId = runId, NextOccurrenceUtc = nextOccurrence
        };
        lock (_triggerStatuses)
            _triggerStatuses[(plan.Id, trigger.Id)] = status;
    }

    /// <summary>只更新下次时间，保留最近一次处理结果。</summary>
    private void SetNextOccurrence(PuloniaTaskPlan plan, PuloniaTaskTrigger trigger, DateTimeOffset? nextOccurrence)
    {
        lock (_triggerStatuses)
        {
            if (_triggerStatuses.TryGetValue((plan.Id, trigger.Id), out var status) && status.NextOccurrenceUtc != nextOccurrence)
                _triggerStatuses[(plan.Id, trigger.Id)] = status with { NextOccurrenceUtc = nextOccurrence };
        }
    }

    /// <summary>重启或配置版本变化后，从当前运行与历史记录找出该触发配置最近一次提交的运行。</summary>
    private string? FindLatestTriggerRunId(string planId, PuloniaTaskTrigger trigger)
    {
        string? latestRunId = null;
        DateTimeOffset? latestOccurrence = null;
        lock (_runsGate)
        {
            var candidates = _runs.Values.Select(run => (run.RunId, run.Request))
                .Concat(_history.Values.Select(record => (record.RunId, record.Request)));
            foreach (var (runId, request) in candidates)
            {
                if (request.PlanId != planId || request.TriggerId != trigger.Id
                    || request.TriggerActivatedAtUtc != trigger.ActivatedAtUtc)
                    continue;
                // 发生时刻相同时保留先遇到的一条
                if (latestRunId is null || request.OccurrenceUtc > latestOccurrence)
                {
                    latestRunId = runId;
                    latestOccurrence = request.OccurrenceUtc;
                }
            }
        }
        return latestRunId;
    }

    /// <summary>用关联运行的当前状态和消息补全反馈；运行不存在时保留原反馈。</summary>
    private PuloniaTaskTriggerStatus ResolveRunProgress(PuloniaTaskTriggerStatus status)
    {
        if (status.RunId is not { } runId) return status;
        lock (_runsGate)
        {
            if (_runs.TryGetValue(runId, out var run))
                return status with { RunStatus = run.Status, Message = run.Message };
            if (_history.TryGetValue(runId, out var record))
                return status with { RunStatus = record.Status, Message = record.Message };
        }
        return status;
    }

    /// <summary>推进定时游标并与当前队列一起写盘；热键没有游标，直接返回。</summary>
    private async Task AdvanceCursorAsync(PuloniaTaskTriggerState? cursor, DateTimeOffset occurrence, CancellationToken ct)
    {
        if (cursor is null) return;
        cursor.LastOccurrenceUtc = occurrence;
        await PersistStateAsync(ct, state => PutCursor(state, cursor)).ConfigureAwait(false);
    }

    /// <summary>替换候选状态中的同一触发器游标。</summary>
    private static void PutCursor(PuloniaTaskState state, PuloniaTaskTriggerState cursor)
    {
        state.TriggerStates.RemoveAll(item => item.PlanId == cursor.PlanId && item.TriggerId == cursor.TriggerId);
        state.TriggerStates.Add(cursor.Clone());
    }

    /// <summary>
    /// 检查忙碌策略，再把请求与定时游标一起提交；只由持有 <see cref="_triggerGate"/> 的调用方进入。
    /// 手动和续跑请求不经过触发门，与这里的忙碌检查之间仍有窄竞态，最坏多排一个请求，可以接受。
    /// </summary>
    /// <param name="plan">触发器所属计划。</param>
    /// <param name="trigger">触发配置。</param>
    /// <param name="cursor">定时游标；热键为空，不写游标。</param>
    /// <param name="occurrence">本次发生时刻。</param>
    /// <param name="ct">取消信号。</param>
    private async Task SubmitTriggerAsync(PuloniaTaskPlan plan, PuloniaTaskTrigger trigger,
        PuloniaTaskTriggerState? cursor, DateTimeOffset occurrence, CancellationToken ct)
    {
        // 一条龙的触发器遵循配置绑定，避免手动运行与定时使用不同账号。
        var triggerAccountId = plan.Purpose == PuloniaTaskPlanPurpose.OneDragon && _accountSwitcher is not null
            ? plan.Accounts.FirstOrDefault(account => account.Enabled)?.AccountId : trigger.AccountId;
        var next = PuloniaTaskSchedule.Next(trigger, occurrence);
        bool isBusy;
        bool hasQueued;
        lock (_runsGate)
        {
            var busy = _runs.Values.Where(item => !IsTerminal(item.Status)
                && item.Request.PlanId == plan.Id && item.Request.AccountId == triggerAccountId).ToArray();
            isBusy = busy.Length > 0;
            hasQueued = busy.Any(item => item.Status == PuloniaTaskRunStatus.Queued);
        }
        if (isBusy && (trigger.BusyPolicy == PuloniaTaskBusyPolicy.Skip || hasQueued))
        {
            await AdvanceCursorAsync(cursor, occurrence, ct).ConfigureAwait(false);
            SetTriggerStatus(plan, trigger, PuloniaTaskTriggerOutcome.Merged,
                "同一计划/账号已有请求，按忙碌策略跳过重复触发。", next);
            return;
        }
        try
        {
            var request = new PuloniaTaskRequest
            {
                PlanId = plan.Id, TargetTaskId = trigger.TargetTaskId, AccountId = triggerAccountId,
                Source = trigger.Kind == PuloniaTaskTriggerKind.Hotkey ? "hotkey" : "schedule",
                TriggerId = trigger.Id, TriggerActivatedAtUtc = trigger.ActivatedAtUtc, OccurrenceUtc = occurrence,
                DeadlineUtc = occurrence.AddMinutes(trigger.WindowMinutes),
                BusyPolicy = trigger.BusyPolicy
            };
            // 防重复只依赖触发门串行化与游标：本轮读到的游标不会被其他路径改写，Latest 只返回严格晚于游标的时刻，
            // 重启和时钟回拨都不会重放同一时刻，因此提交时不再复核“该时刻已提交”。
            if (cursor is not null) cursor.LastOccurrenceUtc = occurrence;
            var runId = await EnqueueCoreAsync(request,
                cursor is null ? null : (state, _) => PutCursor(state, cursor), ct).ConfigureAwait(false);
            SetTriggerStatus(plan, trigger, PuloniaTaskTriggerOutcome.Submitted, "请求与调度游标已一并保存。", next, runId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _runState.ThrowIfFailed();
            await AdvanceCursorAsync(cursor, occurrence, ct).ConfigureAwait(false);
            SetTriggerStatus(plan, trigger, PuloniaTaskTriggerOutcome.PrepareFailed, ex.Message, next);
        }
    }

    /// <summary>真正开始时复核配置和截止时间；过期请求归档而不是执行。</summary>
    private async Task<bool> AdmitQueuedRunAsync(RunState state)
    {
        lock (_runsGate)
        {
            if (IsTerminal(state.Status)) return false;
        }
        if (state.Request.TriggerId is null) return true;
        var now = _timeProvider.GetUtcNow();
        PuloniaTaskPlan? plan;
        try { plan = await _store.LoadPlanAsync(state.Request.PlanId, _shutdown.Token).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            lock (_runsGate)
            {
                // 复核期间已被取消的请求由取消流程收尾，不重复结束。
                if (state.Status != PuloniaTaskRunStatus.Queued) return false;
                state.Status = PuloniaTaskRunStatus.NeedsAttention;
                state.Message = "无法复核触发配置，未启动任务：" + ex.Message;
                state.FinishedAt = now;
            }
            await CompleteStateAsync(state).ConfigureAwait(false);
            return false;
        }
        var trigger = plan?.Triggers.FirstOrDefault(item => item.Id == state.Request.TriggerId && item.Enabled);
        var invalid = trigger is null || trigger.ActivatedAtUtc != state.Request.TriggerActivatedAtUtc;
        if (plan?.Purpose == PuloniaTaskPlanPurpose.OneDragon && _accountSwitcher is not null)
        {
            var bindings = plan.Accounts.Where(account => account.Enabled).ToArray();
            invalid |= bindings.Length > 1 || (bindings.FirstOrDefault()?.AccountId ?? PuloniaGenshinAccountProfile.DefaultId)
                != (state.Request.AccountId ?? PuloniaGenshinAccountProfile.DefaultId);
        }
        if (invalid || state.Request.DeadlineUtc <= now)
        {
            lock (_runsGate)
            {
                if (state.Status != PuloniaTaskRunStatus.Queued) return false;
                state.Status = invalid ? PuloniaTaskRunStatus.Cancelled : PuloniaTaskRunStatus.Expired;
                state.Message = invalid ? "触发器已删除、禁用或调整，旧排队请求未启动。" : "已超过允许延迟启动时间，未启动任何执行器。";
                state.FinishedAt = now;
            }
            await CompleteStateAsync(state).ConfigureAwait(false);
            return false;
        }
        return true;
    }
}
