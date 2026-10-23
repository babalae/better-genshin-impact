using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>地图追踪文件级 CD 的查询与成功结算，复用同一份运行状态和账本。</summary>
public sealed partial class PuloniaTaskService
{
    /// <summary>在状态锁内读取最新文件成功事实，不依赖计划或资源版本。</summary>
    private async Task<(PuloniaTaskAvailabilityDecision Decision, JObject Data)> EvaluateCollectionCooldownAsync(
        RunState state, PuloniaCollectionCooldownPolicy policy, CancellationToken ct)
    {
        await _stateGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var decision = PuloniaCollectionCooldown.Evaluate(policy, state.Request, _persistentState.Ledger,
                DateTimeOffset.UtcNow, out var data, state.VerifiedUid);
            return (decision, data);
        }
        finally { _stateGate.Release(); }
    }

    /// <summary>
    /// 完整成功后一次性提交节点结果和采集事实；落盘失败不修改内存，也不允许自动重试。
    /// </summary>
    private async Task CommitCollectionSuccessAsync(RunState state, PuloniaTaskPreparedTask task,
        PuloniaCollectionCooldownPolicy policy, PuloniaTaskNodeResult result, CancellationToken ct)
    {
        var next = PuloniaCollectionCooldown.GetNextEligibleAt(policy, result.FinishedAt, state.Request.ServerUtcOffsetMinutes);
        var evidenceData = PuloniaCollectionCooldown.Describe(policy, result.FinishedAt, next);
        evidenceData["file_identity"] = policy.FileIdentity;
        evidenceData["policy"] = JObject.FromObject(policy);
        var entry = new PuloniaTaskLedgerEntry
        {
            EventKey = $"pathing.file:{state.RunId:N}:{PuloniaTaskResourceFingerprint.ComputeTextVersion(task.TaskAddress)}:{result.Attempt}",
            ScopeKey = PuloniaTaskAvailability.BuildScopeKey(state.Request, PuloniaTaskScopeKind.World, state.VerifiedUid),
            EffectKey = PuloniaCollectionCooldown.GetEffectKey(policy),
            WindowKey = "file_success",
            OccurredAt = result.FinishedAt.ToUniversalTime(),
            NextEligibleAt = next,
            TaskRunId = $"{state.RunId:N}:{task.TaskAddress}",
            Evidence = new PuloniaTaskEvidence
            {
                Kind = "executor",
                Source = "pathing.file",
                OccurredAt = result.FinishedAt.ToUniversalTime(),
                Data = evidenceData
            }
        };
        var effect = new PuloniaTaskConfirmedEffect { TaskAddress = task.TaskAddress, Attempt = result.Attempt, Entry = entry };
        await _stateGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // 进入提交前仍须未取消；一旦开始写盘，使用独立时限把整次结算完成。
            ct.ThrowIfCancellationRequested();
            var candidate = ClonePersistentState();
            if (candidate.Ledger.Exists(item => item.EventKey == entry.EventKey)) return;
            var active = state.ToRecord();
            if (FindActiveRecord(candidate, state.RunId) is null)
                throw new InvalidOperationException("文件成功结算时活动运行已变化。");
            active.NodeResults.Add(result);
            active.ConfirmedEffects.Add(effect);
            active.CompletedTaskAddresses.Add(task.TaskAddress);
            ReplaceActiveRecord(candidate, active);
            candidate.Ledger.Add(entry);
            candidate.Sequence = checked(candidate.Sequence + 1);
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await _store.SaveStateAsync(candidate, cleanup.Token).ConfigureAwait(false); }
            catch (Exception ex)
            {
                Volatile.Write(ref _persistenceFailure, ex);
                lock (state.SyncRoot)
                {
                    state.RequiresAttention = true;
                    state.HasFinalFailure = true;
                    state.Message = "采集文件成功记录写盘失败，已停止自动运行：" + ex.Message;
                }
                throw new InvalidOperationException(state.Message, ex);
            }
            _persistentState = candidate;
            lock (state.SyncRoot)
            {
                state.NodeResults.Add(result);
                state.ConfirmedEffects.Add(effect);
                state.CompletedTaskAddresses.Add(task.TaskAddress);
            }
        }
        finally { _stateGate.Release(); }
        RaiseRunChanged(state.RequestId);
    }
}
