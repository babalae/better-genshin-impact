using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>地图追踪文件级 CD 的查询与成功结算，复用同一份运行状态和账本。</summary>
public sealed partial class PuloniaTaskService
{
    /// <summary>
    /// 本次运行的采集作用域身份：切换到指定账号时为核验 UID；默认账号或没有账号准备时为空（共用默认身份）。
    /// </summary>
    private static string? GetCooldownUid(RunState state) => state.UsesSwitchedAccount ? state.VerifiedUid : null;

    /// <summary>读取已提交账本中的最新文件成功事实，不依赖计划或资源版本；已提交状态不再修改，无需加锁。</summary>
    private PuloniaCollectionCooldownDecision EvaluateCollectionCooldown(RunState state,
        PuloniaCollectionCooldownPolicy policy, out JObject data)
        => PuloniaCollectionCooldown.Evaluate(policy, state.Request, _runState.Current.Ledger,
            DateTimeOffset.UtcNow, out data, GetCooldownUid(state));

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
            var message = "采集文件成功记录写盘失败，已停止自动运行：" + ex.Message;
            lock (_runsGate)
                state.Message = message;
            throw new InvalidOperationException(message, ex);
        }
        RaiseRunChanged(state.RunId);
    }
}
