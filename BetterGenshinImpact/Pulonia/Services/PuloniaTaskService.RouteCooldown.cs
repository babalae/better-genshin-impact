using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 地图追踪路线 CD 的判定、成功结算与运行预览。完成记录属于运行账户的进度，经账户服务读写；
/// 节点结果与本次完成的路线写入本实例的运行状态。
/// </summary>
public sealed partial class PuloniaTaskService
{
    /// <summary>
    /// 按运行账户的完成记录判定路线 CD：判定前由账户服务比较文件修改时间，其他实例写入或清除后会重新读取。
    /// </summary>
    /// <param name="state">本次运行。</param>
    /// <param name="rule">节点固定的刷新规则。</param>
    /// <param name="ct">运行取消令牌。</param>
    /// <exception cref="InvalidOperationException">本次运行没有确定运行账户。</exception>
    private async Task<PuloniaRouteCooldownStatus> GetRouteCooldownStatusAsync(RunState state, PuloniaRouteRefreshRule rule,
        CancellationToken ct)
    {
        var index = await _accounts.GetRouteCompletionIndexAsync(GetRouteAccountId(state), ct).ConfigureAwait(false);
        return PuloniaRouteCooldown.GetStatus(rule, index.Find(rule.FileIdentity), DateTimeOffset.UtcNow,
            state.Request.ServerUtcOffsetMinutes);
    }

    /// <summary>
    /// 路线 CD 使用的运行账户。地图追踪节点需要游戏，含它的运行在开始时一定已确定运行账户；没有时说明流程出错，不能继续。
    /// </summary>
    /// <param name="state">本次运行。</param>
    private static string GetRouteAccountId(RunState state)
        => state.AccountId ?? throw new InvalidOperationException("地图追踪节点需要按运行账户判定和记录路线 CD，但本次运行没有确定运行账户。");

    /// <summary>
    /// 整份路线成功后按固定顺序提交：先写运行账户的完成记录（耐久），再把节点结果和本次完成的路线写入本实例的运行状态。
    /// 两步之间崩溃时完成记录已经存在，续跑或下次运行按 CD 跳过；任一步写盘失败都熔断，成功不能被当作可重试的普通节点失败。
    /// </summary>
    /// <param name="state">本次运行。</param>
    /// <param name="task">成功的地图追踪节点。</param>
    /// <param name="rule">节点固定的刷新规则。</param>
    /// <param name="result">已含路线 CD 说明的节点结果。</param>
    /// <param name="ct">节点令牌；只在进入提交前检查。</param>
    private async Task CommitRouteSuccessAsync(RunState state, PuloniaTaskPreparedTask task,
        PuloniaRouteRefreshRule rule, PuloniaTaskNodeResult result, CancellationToken ct)
    {
        // 1. 进入提交前仍须未取消；之后不再响应节点令牌
        ct.ThrowIfCancellationRequested();
        var accountId = GetRouteAccountId(state);
        var completion = new PuloniaRouteCompletion
        {
            Route = rule.FileIdentity,
            CompletedAt = result.FinishedAt.ToUniversalTime(),
            RunId = state.RunId,
            TaskAddress = task.TaskAddress,
            Attempt = result.Attempt,
            Instance = state.Instance,
            Rule = PuloniaRouteCompletionRule.From(rule)
        };
        // 一旦开始写盘，使用独立时限把整次结算完成，不复用可能随时取消的节点令牌。
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        // 2. 第 1 步：在写锁内读改写运行账户的完成记录；同一运行、节点、尝试重复写入时幂等
        try
        {
            await _accounts.RecordRouteCompletionAsync(accountId, completion, cleanup.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw ReportRouteCommitFailure(state, "路线完成记录写盘失败，已停止自动运行：", ex);
        }
        // 3. 第 2 步：节点结果、本次完成的路线与已完成地址在同一次替换中写入 state.json
        try
        {
            // 先按运行集合投影再追加，不能使用投影在最后的 PersistStateAsync，否则追加内容会被覆盖。
            await _runState.MutateAsync(candidate =>
            {
                ProjectRuns(candidate, null);
                if (candidate.ActiveRun is not { } active || active.RunId != state.RunId)
                    throw new InvalidOperationException("路线成功结算时活动运行已变化。");
                active.NodeResults.Add(result);
                active.RouteCompletions.Add(completion);
                active.CompletedTaskAddresses.Add(task.TaskAddress);
            }, cleanup.Token, committed: () =>
            {
                // 落盘成功后在同一状态门内更新内存，后续写入的活动运行一定包含这次成功。
                lock (_runsGate)
                {
                    state.NodeResults.Add(result);
                    state.RouteCompletions.Add(completion);
                    state.CompletedTaskAddresses.Add(task.TaskAddress);
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 完成记录已经写入：下次运行会按 CD 跳过这份路线，本实例停止自动运行等待处理
            throw ReportRouteCommitFailure(state, "路线完成记录已写入，但节点结果写盘失败，已停止自动运行：", ex);
        }
        RaiseRunChanged(state.RunId);
    }

    /// <summary>熔断自动运行并把原因写入运行消息，返回要抛出的异常。</summary>
    /// <param name="state">本次运行。</param>
    /// <param name="prefix">消息前缀。</param>
    /// <param name="error">写盘失败原因。</param>
    private InvalidOperationException ReportRouteCommitFailure(RunState state, string prefix, Exception error)
    {
        _runState.ReportFailure(error);
        var message = prefix + error.Message;
        lock (_runsGate)
            state.Message = message;
        return new InvalidOperationException(message, error);
    }

    /// <inheritdoc />
    public async Task<PuloniaTaskRunPreview> PreviewAsync(PuloniaTaskRequest request, string? accountId = null,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeStarted) != 0, this);
        await _initialization.WaitAsync(ct).ConfigureAwait(false);
        // 1. 与提交相同地固定快照：运行账户设置、引用与目录展开都按当前配置
        var fixedRequest = CloneAndValidateRequest(request, serverUtcOffsetMinutes: PuloniaServerTime.GetUtcOffsetMinutes());
        var prepared = await PrepareRunAsync(fixedRequest, ct).ConfigureAwait(false);
        // 2. 选账户：调用方指定 → 该账户；计划指定账户 → 该账户；跟随当前账户 → 本实例的当前账户（未确定时退回列表第一个）
        var (account, notice) = await ResolvePreviewAccountAsync(prepared.Snapshot, accountId, ct).ConfigureAwait(false);
        var offset = PuloniaServerTime.GetUtcOffsetMinutes(account.ServerTimeZone);
        var index = await _accounts.GetRouteCompletionIndexAsync(account.Id, ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        // 3. 按执行顺序逐项判定；同一路线前面预计执行成功后，后面检查 CD 的节点会被跳过
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
            var rule = task.RefreshRule ?? PuloniaRouteCooldown.Resolve(task.Path, task.Resource, task.Parameters);
            var route = rule.FileIdentity;
            var status = PuloniaRouteCooldown.GetStatus(rule, index.Find(route), now, offset);
            var outcome = status.State switch
            {
                PuloniaRouteCooldownState.CoolingDown => PuloniaTaskPreviewOutcome.CoolingDown,
                PuloniaRouteCooldownState.Completed => PuloniaTaskPreviewOutcome.Completed,
                _ when rule.IsChecked && executed.ContainsKey(route) => PuloniaTaskPreviewOutcome.RepeatedInRun,
                _ => PuloniaTaskPreviewOutcome.Execute
            };
            if (outcome == PuloniaTaskPreviewOutcome.Execute)
                executed.TryAdd(route, task.Name);
            items.Add(new PuloniaTaskRunPreviewItem
            {
                TaskAddress = task.TaskAddress, Name = task.Name, TaskType = task.TaskType, Outcome = outcome,
                Rule = rule, Cooldown = status,
                RepeatedTaskName = outcome == PuloniaTaskPreviewOutcome.RepeatedInRun ? executed[route] : null
            });
        }
        return new PuloniaTaskRunPreview(prepared.Plan.Name, account.Id, account.Name, notice, offset, now, items);
    }

    /// <summary>
    /// 运行预览读取哪个账户的完成记录：调用方指定的账户；否则按计划的运行账户设置——指定账户为该账户，
    /// 跟随当前账户为本实例的当前账户，未确定时退回账户列表第一个并给出说明。实际运行以运行开始时确定的运行账户为准。
    /// </summary>
    /// <param name="snapshot">预览准备的快照。</param>
    /// <param name="accountId">调用方要查看的账户 ID；为空时按计划设置选择。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>预览账户与说明（正常选出时为空）。</returns>
    private async Task<(PuloniaGenshinAccountProfile Account, string? Notice)> ResolvePreviewAccountAsync(
        PuloniaTaskSnapshot snapshot, string? accountId, CancellationToken ct)
    {
        var accounts = (await _accounts.LoadAccountsAsync(ct).ConfigureAwait(false)).Accounts;
        // 1. 界面临时切换查看的账户
        if (accountId is not null)
            return (accounts.FirstOrDefault(account => account.Id == accountId)
                    ?? throw new PuloniaTaskValidationException("preview/account_id", "要查看的账户已不存在，请重新选择。"), null);
        // 2. 计划指定账户：优先用目录中的最新资料（名称、服务器），已被删除时用快照固定的资料
        if (snapshot.GenshinAccountJson is { } accountJson)
        {
            var specified = PuloniaTaskJson.Read<PuloniaGenshinAccountProfile>(accountJson);
            return (accounts.FirstOrDefault(account => account.Id == specified.Id) ?? specified, null);
        }
        // 3. 跟随当前账户：本实例的当前账户（运行总在提交它的实例执行）
        if (await _accounts.GetCurrentAccountAsync(_accounts.CurrentInstanceKey, ct).ConfigureAwait(false) is { } current)
            return (current, null);
        var first = accounts[0];
        return (first, $"本实例当前账户未确定，按账户列表第一个账户“{first.Name}”预测；实际运行前需要先设置本实例当前账户。");
    }
}
