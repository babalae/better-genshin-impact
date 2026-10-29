using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 在本进程的游戏中执行一个快照叶子节点或准备账号。主实例的本地节点与子实例执行的远程节点共用同一顺序：
/// 取租约 → 核验 UID → 建上下文 → 进入会话作用域 → 调用执行器；每次尝试都重新取租约，租约不跨重试持有。
/// </summary>
internal sealed class PuloniaLocalNodeRunner
{
    /// <summary>
    /// 游戏节点的任务锁、运行环境与任务模式。
    /// </summary>
    private readonly PuloniaGameTaskCoordinator _gameTaskCoordinator;

    /// <summary>
    /// 账号准备与核验；没有注册时不核验 UID，账号准备直接失败。
    /// </summary>
    private readonly IPuloniaGenshinAccountSwitcher? _accountSwitcher;

    /// <summary>
    /// 建立本地节点执行入口。
    /// </summary>
    /// <param name="gameTaskCoordinator">游戏节点协调器。</param>
    /// <param name="accountSwitcher">账号切换与核验。</param>
    public PuloniaLocalNodeRunner(PuloniaGameTaskCoordinator gameTaskCoordinator, IPuloniaGenshinAccountSwitcher? accountSwitcher)
    {
        _gameTaskCoordinator = gameTaskCoordinator;
        _accountSwitcher = accountSwitcher;
    }

    /// <summary>
    /// 执行一次节点尝试。
    /// </summary>
    /// <param name="executor">节点类型对应的执行器。</param>
    /// <param name="task">快照节点。</param>
    /// <param name="runId">所属运行 ID。</param>
    /// <param name="snapshot">运行快照。</param>
    /// <param name="attempt">从 1 开始的尝试次数。</param>
    /// <param name="requiresGameSession">节点是否需要游戏会话。</param>
    /// <param name="expectedUid">已准备账号的运行在游戏节点前要核验的 UID；为空时不核验。</param>
    /// <param name="ct">节点取消令牌。</param>
    /// <returns>执行器结果。</returns>
    /// <exception cref="PuloniaGenshinAccountException">取得输入后核验 UID 失败，未进入业务。</exception>
    public async Task<PuloniaTaskOutcome> ExecuteAsync(IPuloniaTaskExecutor executor, PuloniaTaskPreparedTask task,
        string runId, PuloniaTaskSnapshot snapshot, int attempt, bool requiresGameSession, string? expectedUid,
        CancellationToken ct)
    {
        // 1. 游戏节点取得输入所有权与运行环境；纯任务不触碰游戏
        using var lease = requiresGameSession ? await _gameTaskCoordinator.AcquireAsync(ct).ConfigureAwait(false) : null;
        // 2. 已准备账号的运行：取得输入后重新核验实际 UID，身份不符时不进入业务
        if (lease is not null && expectedUid is not null)
            await VerifyAsync(lease.Runtime, expectedUid, ct).ConfigureAwait(false);
        // 3. 游戏节点把租约记录的运行环境交给执行器，节点内的任务只使用它
        var context = new PuloniaTaskExecutionContext(runId, snapshot, attempt, task.TaskAddress, lease?.Runtime);
        // 4. 持有任务锁期间标记会话，执行器内部不能再通过 TaskRunner 启动任务
        using (lease is not null ? GameTaskSessionScope.Enter() : null)
        {
            return await executor.ExecuteAsync(task, context, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 在本进程的游戏中准备账号并返回完整核验 UID；续跑时核验历史 UID。
    /// </summary>
    /// <param name="account">运行快照中固定的账号资料；图片按快照路径读取，凭据按账号固定目标名读取最新值。</param>
    /// <param name="expectedUid">续跑时历史运行已核验的 UID；新运行为空。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>完整核验 UID。</returns>
    /// <exception cref="PuloniaGenshinAccountException">无法准备游戏环境、切换或核验账号。</exception>
    public async Task<string> PrepareAccountAsync(PuloniaGenshinAccountProfile account, string? expectedUid,
        CancellationToken ct)
    {
        var accountSwitcher = _accountSwitcher
                              ?? throw new PuloniaGenshinAccountException("当前实例没有账号切换能力。", new InvalidOperationException());
        // 1. 账号准备同样需要独占游戏输入；环境无法准备时按账号失败处理，不进入业务
        PuloniaGameTaskLease preparedLease;
        try { preparedLease = await _gameTaskCoordinator.AcquireAsync(ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { throw new PuloniaGenshinAccountException("无法准备账号的游戏环境：" + ex.Message, ex); }
        using var lease = preparedLease;
        // 2. 切换并核验；续跑默认账号也必须核验历史实际 UID，不能自动采用另一个当前登录账号
        try
        {
            if (expectedUid is not null)
            {
                if (!account.IsDefault) await accountSwitcher.PrepareAsync(lease.Runtime, account, ct).ConfigureAwait(false);
                await accountSwitcher.VerifyAsync(lease.Runtime, expectedUid, ct).ConfigureAwait(false);
                return expectedUid;
            }
            return await accountSwitcher.PrepareAsync(lease.Runtime, account, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 运行结果只写入执行记录，这里同时写入日志窗口，避免停止后界面上没有任何说明
            TaskControl.Logger.LogWarning("账号准备失败，未执行后续任务：{Message}", ex.Message);
            throw new PuloniaGenshinAccountException(ex.Message, ex);
        }
    }

    /// <summary>
    /// 核验当前登录 UID；没有账号切换能力时跳过（纯任务服务）。
    /// </summary>
    private async Task VerifyAsync(GameRuntime runtime, string expectedUid, CancellationToken ct)
    {
        if (_accountSwitcher is null) return;
        try { await _accountSwitcher.VerifyAsync(runtime, expectedUid, ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { throw new PuloniaGenshinAccountException(ex.Message, ex); }
    }
}
