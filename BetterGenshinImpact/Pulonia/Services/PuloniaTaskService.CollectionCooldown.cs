using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>地图追踪路线 CD 的判定、成功结算、清除与运行预览，复用同一份运行状态和账本。</summary>
public sealed partial class PuloniaTaskService
{
    /// <inheritdoc />
    public event EventHandler? LedgerChanged;

    /// <summary>
    /// 本次运行的路线 CD 作用域身份：切换到指定账号时为核验 UID；默认账号或没有账号准备时为空（共用默认身份）。
    /// </summary>
    private static string? GetCooldownUid(RunState state) => state.UsesSwitchedAccount ? state.VerifiedUid : null;

    /// <summary>读取已提交账本中的最新文件成功事实并判定状态，不依赖计划或资源版本；已提交状态不再修改，无需加锁。</summary>
    private PuloniaCollectionCooldownStatus GetCollectionCooldownStatus(RunState state, PuloniaCollectionCooldownPolicy policy)
        => PuloniaCollectionCooldown.GetStatus(policy,
            PuloniaCollectionCooldown.FindLatest(_runState.Current.Ledger,
                PuloniaCollectionCooldown.BuildScopeKey(GetCooldownUid(state)), PuloniaCollectionCooldown.GetEffectKey(policy)),
            DateTimeOffset.UtcNow, state.Request.ServerUtcOffsetMinutes);

    /// <summary>
    /// 完整成功后一次性提交节点结果和路线 CD 记录；落盘失败不修改内存，也不允许自动重试。
    /// </summary>
    /// <param name="state">本次运行。</param>
    /// <param name="task">成功的地图追踪节点。</param>
    /// <param name="policy">节点固定的规则。</param>
    /// <param name="result">已含路线 CD 说明的节点结果。</param>
    /// <param name="next">主流程按本次成功时间算出的下次可执行时间。</param>
    /// <param name="ct">节点令牌；只在进入提交前检查。</param>
    private async Task CommitCollectionSuccessAsync(RunState state, PuloniaTaskPreparedTask task,
        PuloniaCollectionCooldownPolicy policy, PuloniaTaskNodeResult result, DateTimeOffset? next, CancellationToken ct)
    {
        var evidenceData = PuloniaCollectionCooldown.Describe(policy, result.FinishedAt, next);
        evidenceData["file_identity"] = policy.FileIdentity;
        evidenceData["policy"] = JObject.FromObject(policy);
        var entry = new PuloniaTaskLedgerEntry
        {
            // 事件键直接使用可读的运行 ID 与节点地址，同一节点同一尝试重复结算不会重复记账。
            EventKey = $"pathing.file:{state.RunId}:{task.TaskAddress}:{result.Attempt}",
            ScopeKey = PuloniaCollectionCooldown.BuildScopeKey(GetCooldownUid(state)),
            EffectKey = PuloniaCollectionCooldown.GetEffectKey(policy),
            WindowKey = "file_success",
            OccurredAt = result.FinishedAt.ToUniversalTime(),
            NextEligibleAt = next,
            TaskRunId = $"{state.RunId}:{task.TaskAddress}",
            Evidence = new PuloniaTaskEvidence
            {
                Kind = "executor",
                Source = "pathing.file",
                OccurredAt = result.FinishedAt.ToUniversalTime(),
                Data = evidenceData
            }
        };
        var effect = new PuloniaTaskConfirmedEffect { TaskAddress = task.TaskAddress, Attempt = result.Attempt, Entry = entry };
        // 修改候选状态成功后才置位，用来区分“提交前检查失败”和“写盘失败”。
        var prepared = false;
        // 一旦开始写盘，使用独立时限把整次结算完成，不复用可能随时取消的节点令牌。
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            // 先按运行集合投影再追加，不能使用投影在最后的 PersistStateAsync，否则追加内容会被覆盖。
            await _runState.MutateAsync(candidate =>
            {
                // 1. 进入提交前仍须未取消
                ct.ThrowIfCancellationRequested();
                // 2. 活动运行记录由本次投影新建，直接追加结果与确认事件
                ProjectRuns(candidate, null);
                if (candidate.ActiveRun is not { } active || active.RunId != state.RunId)
                    throw new InvalidOperationException("文件成功结算时活动运行已变化。");
                active.NodeResults.Add(result);
                active.ConfirmedEffects.Add(effect);
                active.CompletedTaskAddresses.Add(task.TaskAddress);
                // 3. 账本每个（作用域，文件）只保留最新一条，明细留在运行历史的确认事件中
                PuloniaCollectionCooldown.PutLatest(candidate.Ledger, entry);
                prepared = true;
            }, cleanup.Token, committed: () =>
            {
                // 落盘成功后在同一状态门内更新内存，后续写入的活动运行一定包含这次成功。
                lock (_runsGate)
                {
                    state.NodeResults.Add(result);
                    state.ConfirmedEffects.Add(effect);
                    state.CompletedTaskAddresses.Add(task.TaskAddress);
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (prepared)
        {
            // 写盘失败（含超时）：熔断自动运行，成功不能被当作可重试的普通节点失败。
            _runState.ReportFailure(ex);
            var message = "路线成功记录写盘失败，已停止自动运行：" + ex.Message;
            lock (_runsGate)
                state.Message = message;
            throw new InvalidOperationException(message, ex);
        }
        RaiseRunChanged(state.RunId);
        LedgerChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public async Task<int> ClearCollectionCooldownAsync(IReadOnlyCollection<PuloniaCollectionCooldownKey> keys,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeStarted) != 0, this);
        ThrowIfNotCoordinator();
        await _initialization.WaitAsync(ct).ConfigureAwait(false);
        _runState.ThrowIfFailed();
        if (keys.Count == 0)
            return 0;
        var removed = 0;
        // 与其他写入一样经唯一入口提交；运行集合按当前状态重新投影，不影响正在执行的运行。
        await PersistStateAsync(ct, candidate => removed = PuloniaCollectionCooldown.RemoveEntries(candidate.Ledger, keys))
            .ConfigureAwait(false);
        _logger.LogInformation("已清除 {Count} 条 Pulonia 路线 CD 记录", removed);
        if (removed > 0)
            LedgerChanged?.Invoke(this, EventArgs.Empty);
        return removed;
    }

    /// <inheritdoc />
    public async Task<PuloniaTaskRunPreview> PreviewAsync(PuloniaTaskRequest request, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeStarted) != 0, this);
        ThrowIfNotCoordinator();
        await _initialization.WaitAsync(ct).ConfigureAwait(false);
        // 1. 与提交相同地固定快照：服务器时区、账号绑定、引用与目录展开都按当前配置
        var fixedRequest = CloneAndValidateRequest(request, serverUtcOffsetMinutes: PuloniaServerTime.GetUtcOffsetMinutes());
        var prepared = await PrepareRunAsync(fixedRequest, ct).ConfigureAwait(false);
        // 2. 作用域：切换到指定账号时按账号资料的 UID 预测，运行时以核验后的 UID 为准
        var scope = PuloniaCollectionCooldown.BuildScopeKey(prepared.Account is { IsDefault: false } account ? account.Uid : null);
        var index = new PuloniaCollectionCooldownIndex(_runState.Current.Ledger);
        var now = DateTimeOffset.UtcNow;
        // 3. 按执行顺序逐项判定；同一文件前面预计执行成功后，后面检查 CD 的节点会被跳过
        var executed = new Dictionary<string, string>(StringComparer.Ordinal);
        var items = new List<PuloniaTaskRunPreviewItem>();
        foreach (var task in EnumerateLeafTasks(prepared.Snapshot.RootTask))
        {
            if (!task.IsEnabled)
            {
                items.Add(new PuloniaTaskRunPreviewItem { TaskAddress = task.TaskAddress, Name = task.Name,
                    TaskType = task.TaskType, Outcome = PuloniaTaskPreviewOutcome.Disabled });
                continue;
            }
            if (task.TaskType != "pathing")
            {
                items.Add(new PuloniaTaskRunPreviewItem { TaskAddress = task.TaskAddress, Name = task.Name,
                    TaskType = task.TaskType, Outcome = PuloniaTaskPreviewOutcome.Execute });
                continue;
            }
            var policy = task.CollectionCooldown ?? PuloniaCollectionCooldown.Resolve(task.Path, task.Resource, task.Parameters);
            var key = PuloniaCollectionCooldown.GetEffectKey(policy);
            var status = PuloniaCollectionCooldown.GetStatus(policy, index.Find(scope, key), now, fixedRequest.ServerUtcOffsetMinutes);
            var outcome = status.State switch
            {
                PuloniaCollectionCooldownState.CoolingDown => PuloniaTaskPreviewOutcome.CoolingDown,
                PuloniaCollectionCooldownState.Completed => PuloniaTaskPreviewOutcome.Completed,
                _ when policy.IsChecked && executed.ContainsKey(key) => PuloniaTaskPreviewOutcome.RepeatedInRun,
                _ => PuloniaTaskPreviewOutcome.Execute
            };
            if (outcome == PuloniaTaskPreviewOutcome.Execute)
                executed.TryAdd(key, task.Name);
            items.Add(new PuloniaTaskRunPreviewItem
            {
                TaskAddress = task.TaskAddress, Name = task.Name, TaskType = task.TaskType, Outcome = outcome,
                Policy = policy, Cooldown = status,
                RepeatedTaskName = outcome == PuloniaTaskPreviewOutcome.RepeatedInRun ? executed[key] : null
            });
        }
        return new PuloniaTaskRunPreview(prepared.Plan.Name, scope, now, items);
    }
}
