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
    /// <summary>可注入的 UTC 时钟，测试不依赖真实计时。</summary>
    private readonly TimeProvider _timeProvider;
    /// <summary>串行处理定时与热键事件，避免重复准备同一 occurrence。</summary>
    private readonly SemaphoreSlim _triggerGate = new(1, 1);

    /// <summary>取得独立的调度状态副本，用于界面显示。</summary>
    public async Task<IReadOnlyList<PuloniaTaskTriggerState>> ListTriggerStatesAsync(CancellationToken ct = default)
    {
        await _initialization.WaitAsync(ct).ConfigureAwait(false);
        await _stateGate.WaitAsync(ct).ConfigureAwait(false);
        try { return ClonePersistentState().TriggerStates; }
        finally { _stateGate.Release(); }
    }

    /// <summary>检查当前配置；每次恢复只合并有效窗口内最新一次，不补跑整段离线历史。</summary>
    public async Task CheckTriggersAsync(IReadOnlyList<PuloniaTaskPlan> plans, CancellationToken ct = default)
    {
        await _initialization.WaitAsync(ct).ConfigureAwait(false);
        ThrowIfPersistenceFailed();
        await _triggerGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = _timeProvider.GetUtcNow();
            foreach (var plan in plans)
            foreach (var trigger in plan.Triggers.Where(item => item.Enabled && item.Kind == PuloniaTaskTriggerKind.Schedule))
            {
                var cursor = await GetTriggerCursorAsync(plan, trigger, ct).ConfigureAwait(false);
                var floor = cursor.LastOccurrenceUtc ?? trigger.ActivatedAtUtc.AddTicks(-1);
                var due = PuloniaTaskSchedule.Latest(trigger, now, floor);
                if (!trigger.CatchUp && cursor.PendingOccurrenceUtc is { } observed
                    && observed > floor && observed <= now && observed > now.AddMinutes(-trigger.WindowMinutes))
                    due = observed;
                if (due is null)
                {
                    // 长时间离线只推进到现在；下次时间始终严格晚于已处理游标。
                    var next = PuloniaTaskSchedule.Next(trigger, floor);
                    if (next <= now)
                    {
                        cursor.LastOccurrenceUtc = now;
                        cursor.PendingOccurrenceUtc = null;
                        cursor.Status = "已过期";
                        cursor.Message = "遗漏的时间已超出有效窗口，未启动任务。";
                        cursor.NextOccurrenceUtc = PuloniaTaskSchedule.Next(trigger, now);
                        await SaveTriggerCursorAsync(cursor, ct).ConfigureAwait(false);
                    }
                    continue;
                }
                var alreadyObserved = cursor.PendingOccurrenceUtc == due;
                cursor.PendingOccurrenceUtc = due;
                cursor.NextOccurrenceUtc = PuloniaTaskSchedule.Next(trigger, due.Value);
                // 不补离线遗漏，但到点已观察到的事件仍可以在窗口内提交。
                if (!trigger.CatchUp && !alreadyObserved && now - due.Value > TimeSpan.FromSeconds(5))
                {
                    cursor.LastOccurrenceUtc = due;
                    cursor.PendingOccurrenceUtc = null;
                    cursor.Status = "已过期";
                    cursor.Message = "未启用补触发，已跳过遗漏时间。";
                    await SaveTriggerCursorAsync(cursor, ct).ConfigureAwait(false);
                    continue;
                }
                await SubmitTriggerAsync(plan, trigger, cursor, due.Value, ct).ConfigureAwait(false);
            }
        }
        finally { _triggerGate.Release(); }
    }

    /// <summary>热键仍使用相同准入和忙碌策略，不旁路快照、账本或单消费者。</summary>
    public async Task FireHotkeyAsync(string planId, string triggerId, CancellationToken ct = default)
    {
        await _initialization.WaitAsync(ct).ConfigureAwait(false);
        await _triggerGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var plan = await _store.LoadPlanAsync(planId, ct).ConfigureAwait(false);
            var trigger = plan?.Triggers.FirstOrDefault(item => item.Id == triggerId && item.Enabled
                && item.Kind == PuloniaTaskTriggerKind.Hotkey);
            if (plan is null || trigger is null) return;
            var cursor = await GetTriggerCursorAsync(plan, trigger, ct).ConfigureAwait(false);
            var now = _timeProvider.GetUtcNow();
            if (cursor.LastOccurrenceUtc is { } last && now - last < TimeSpan.FromSeconds(1)) return;
            await SubmitTriggerAsync(plan, trigger, cursor, now, ct).ConfigureAwait(false);
        }
        finally { _triggerGate.Release(); }
    }

    /// <summary>生成游标或在配置变更时重置；禁用前的等待不能误用于新配置。</summary>
    private async Task<PuloniaTaskTriggerState> GetTriggerCursorAsync(PuloniaTaskPlan plan,
        PuloniaTaskTrigger trigger, CancellationToken ct)
    {
        var states = await ListTriggerStatesAsync(ct).ConfigureAwait(false);
        var signature = PuloniaTaskSchedule.Signature(trigger);
        var cursor = states.FirstOrDefault(item => item.PlanId == plan.Id && item.TriggerId == trigger.Id
            && item.Signature == signature);
        if (cursor is not null) return cursor;
        cursor = new PuloniaTaskTriggerState
        {
            PlanId = plan.Id, TriggerId = trigger.Id, Signature = signature,
            LastOccurrenceUtc = trigger.ActivatedAtUtc.AddTicks(-1),
            NextOccurrenceUtc = PuloniaTaskSchedule.Next(trigger, trigger.ActivatedAtUtc.AddTicks(-1)),
            Status = "等待", Message = "配置已生效。"
        };
        await SaveTriggerCursorAsync(cursor, ct).ConfigureAwait(false);
        return cursor;
    }

    /// <summary>持久化等待、跳过或错误；相同状态不每秒重复写盘。</summary>
    private async Task SaveTriggerCursorAsync(PuloniaTaskTriggerState cursor, CancellationToken ct)
    {
        var existing = (await ListTriggerStatesAsync(ct).ConfigureAwait(false))
            .FirstOrDefault(item => item.PlanId == cursor.PlanId && item.TriggerId == cursor.TriggerId);
        if (existing is not null && PuloniaTaskJson.Write(existing) == PuloniaTaskJson.Write(cursor)) return;
        await PersistCurrentStateAsync(ct, state => PutCursor(state, cursor)).ConfigureAwait(false);
    }

    /// <summary>替换候选状态中的同一触发器游标。</summary>
    private static void PutCursor(PuloniaTaskState state, PuloniaTaskTriggerState cursor)
    {
        state.TriggerStates.RemoveAll(item => item.PlanId == cursor.PlanId && item.TriggerId == cursor.TriggerId);
        state.TriggerStates.Add(PuloniaTaskJson.Read<PuloniaTaskTriggerState>(PuloniaTaskJson.Write(cursor)));
    }

    /// <summary>同步最近请求的运行/结束状态，不覆盖较新发生时间的等待或合并反馈。</summary>
    private static void UpdateTriggerRunStatus(PuloniaTaskState state, PuloniaTaskRunRecord record)
    {
        var cursor = state.TriggerStates.FirstOrDefault(item => item.PlanId == record.Request.PlanId
            && item.TriggerId == record.Request.TriggerId && item.LastRequestId == record.RequestId
            && item.Signature == record.Request.TriggerSignature && item.Status is "已排队" or "运行中" or "停止中");
        if (cursor is null) return;
        cursor.Status = record.Status switch
        {
            PuloniaTaskRunStatus.Queued => "已排队", PuloniaTaskRunStatus.Running => "运行中",
            PuloniaTaskRunStatus.Cancelling => "停止中", PuloniaTaskRunStatus.Succeeded => "已完成",
            PuloniaTaskRunStatus.Failed => "已失败", PuloniaTaskRunStatus.Cancelled => "已取消",
            PuloniaTaskRunStatus.TimedOut => "已超时", PuloniaTaskRunStatus.Expired => "已过期",
            PuloniaTaskRunStatus.Interrupted => "已中断", _ => "待处理"
        };
        cursor.Message = record.Message;
    }

    /// <summary>检查忙碌策略，再把请求、去重键和游标一起提交。</summary>
    private async Task SubmitTriggerAsync(PuloniaTaskPlan plan, PuloniaTaskTrigger trigger,
        PuloniaTaskTriggerState cursor, DateTimeOffset occurrence, CancellationToken ct)
    {
        RunState[] busy;
        lock (_runsGate) busy = _runs.Values.Where(item => !item.IsTerminal()
            && item.Request.PlanId == plan.Id && item.Request.AccountId == trigger.AccountId).ToArray();
        if (busy.Length > 0 && (trigger.BusyPolicy == PuloniaTaskBusyPolicy.Skip
            || trigger.BusyPolicy != PuloniaTaskBusyPolicy.Skip
            && busy.Any(item => item.GetStatus() == PuloniaTaskRunStatus.Queued)))
        {
            cursor.Status = "已合并";
            cursor.Message = "同一计划/账号已有请求，按忙碌策略跳过重复触发。";
            cursor.LastOccurrenceUtc = occurrence;
            cursor.PendingOccurrenceUtc = null;
            await SaveTriggerCursorAsync(cursor, ct).ConfigureAwait(false);
            return;
        }
        try
        {
            var request = new PuloniaTaskRequest
            {
                PlanId = plan.Id, TargetTaskId = trigger.TargetTaskId, AccountId = trigger.AccountId,
                Source = trigger.Kind == PuloniaTaskTriggerKind.Hotkey ? "hotkey" : "schedule",
                TriggerId = trigger.Id, TriggerSignature = cursor.Signature, OccurrenceUtc = occurrence,
                DeadlineUtc = occurrence.AddMinutes(trigger.WindowMinutes), TimeoutSeconds = trigger.TimeoutSeconds,
                BusyPolicy = trigger.BusyPolicy
            };
            await EnqueueCoreAsync(request, (state, id) =>
            {
                var previous = state.TriggerStates.FirstOrDefault(item => item.PlanId == plan.Id
                    && item.TriggerId == trigger.Id && item.Signature == cursor.Signature);
                if (previous?.LastOccurrenceUtc >= occurrence)
                    throw new InvalidOperationException("该触发时间已提交，不能重复排队。");
                // 准备快照可能让出线程：在同一状态门内再复核并发提交的手动/续跑请求。
                RunState[] existing;
                lock (_runsGate) existing = _runs.Values.Where(item => !item.IsTerminal()
                    && item.Request.PlanId == plan.Id && item.Request.AccountId == trigger.AccountId).ToArray();
                if (existing.Length > 0 && (trigger.BusyPolicy == PuloniaTaskBusyPolicy.Skip
                    || existing.Any(item => item.GetStatus() == PuloniaTaskRunStatus.Queued)))
                    throw new TriggerBusyException();
                cursor.LastOccurrenceUtc = occurrence;
                cursor.PendingOccurrenceUtc = null;
                cursor.LastRequestId = id;
                cursor.Status = "已排队";
                cursor.Message = "请求与调度游标已一并保存。";
                PutCursor(state, cursor);
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ThrowIfPersistenceFailed();
            cursor.Status = ex is TriggerBusyException ? "已合并" : "准备失败";
            cursor.Message = ex.Message;
            cursor.LastOccurrenceUtc = occurrence;
            cursor.PendingOccurrenceUtc = null;
            await SaveTriggerCursorAsync(cursor, ct).ConfigureAwait(false);
        }
    }

    /// <summary>真正开始时复核配置和截止时间；过期请求归档而不是执行。</summary>
    private async Task<bool> AdmitQueuedRunAsync(RunState state)
    {
        if (state.IsTerminal()) return false;
        if (state.Request.TriggerId is null) return true;
        var now = _timeProvider.GetUtcNow();
        PuloniaTaskPlan? plan;
        try { plan = await _store.LoadPlanAsync(state.Request.PlanId, _shutdown.Token).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            lock (state.SyncRoot)
            {
                state.Status = PuloniaTaskRunStatus.NeedsAttention;
                state.Message = "无法复核触发配置，未启动任务：" + ex.Message;
                state.FinishedAt = now;
            }
            await CompleteStateAsync(state).ConfigureAwait(false);
            return false;
        }
        var trigger = plan?.Triggers.FirstOrDefault(item => item.Id == state.Request.TriggerId && item.Enabled);
        var invalid = trigger is null || PuloniaTaskSchedule.Signature(trigger) != state.Request.TriggerSignature;
        if (invalid || state.Request.DeadlineUtc <= now)
        {
            lock (state.SyncRoot)
            {
                if (state.Status != PuloniaTaskRunStatus.Queued) return false;
                state.Status = invalid ? PuloniaTaskRunStatus.Cancelled : PuloniaTaskRunStatus.Expired;
                state.Message = invalid ? "触发器已删除、禁用或调整，旧排队请求未启动。" : "已超过有效开始窗口，未启动任何执行器。";
                state.FinishedAt = now;
            }
            await CompleteStateAsync(state).ConfigureAwait(false);
            return false;
        }
        return true;
    }

    /// <summary>准入复核遇到并发忙碌时的受控跳过，不表示存储故障。</summary>
    private sealed class TriggerBusyException : Exception
    {
        /// <summary>提供可持久化的中文跳过原因。</summary>
        public TriggerBusyException() : base("同一计划/账号在快照准备期间进入忙碌，已合并本次触发。") { }
    }
}
