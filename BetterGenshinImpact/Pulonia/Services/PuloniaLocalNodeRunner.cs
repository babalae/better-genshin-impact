using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 在本进程的游戏中执行一个快照叶子节点、准备账户或读取当前 UID。所有运行（含主实例下发到本实例的运行）都按同一顺序执行：
/// 取租约 → 核验 UID → 建上下文 → 进入会话作用域 → 调用执行器；每次尝试都重新取租约，租约不跨重试持有。
/// </summary>
internal sealed class PuloniaLocalNodeRunner
{
    /// <summary>
    /// 账户准备与核验；没有注册时不核验 UID，账户准备直接失败。
    /// </summary>
    private readonly IPuloniaGenshinAccountSwitcher? _accountSwitcher;

    /// <summary>
    /// 取得游戏输入所有权与运行环境的入口；默认使用游戏任务协调器，单元测试可替换为不触碰游戏的租约。
    /// </summary>
    internal Func<CancellationToken, Task<PuloniaGameTaskLease>> AcquireLeaseAsync { get; set; }

    /// <summary>
    /// 建立本地节点执行入口。
    /// </summary>
    /// <param name="gameTaskCoordinator">游戏节点协调器。</param>
    /// <param name="accountSwitcher">账户切换与核验。</param>
    public PuloniaLocalNodeRunner(PuloniaGameTaskCoordinator gameTaskCoordinator, IPuloniaGenshinAccountSwitcher? accountSwitcher)
    {
        AcquireLeaseAsync = gameTaskCoordinator.AcquireAsync;
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
    /// <param name="expectedUid">指定账户的运行在游戏节点前要核验的 UID；为空时不核验。</param>
    /// <param name="accountId">本次运行的运行账户 ID；未确定时为空。</param>
    /// <param name="ct">节点取消令牌。</param>
    /// <returns>执行器结果。</returns>
    /// <exception cref="PuloniaGenshinAccountException">取得输入后核验 UID 失败，未进入业务。</exception>
    public async Task<PuloniaTaskOutcome> ExecuteAsync(IPuloniaTaskExecutor executor, PuloniaTaskPreparedTask task,
        string runId, PuloniaTaskSnapshot snapshot, int attempt, bool requiresGameSession, string? expectedUid,
        string? accountId, CancellationToken ct)
    {
        // 1. 游戏节点取得输入所有权与运行环境；纯任务不触碰游戏
        using var lease = requiresGameSession ? await AcquireLeaseAsync(ct).ConfigureAwait(false) : null;
        // 2. 指定账户的运行：取得输入后重新核验实际 UID，身份不符时不进入业务
        if (lease is not null && expectedUid is not null)
            await VerifyAsync(lease.Runtime, expectedUid, ct).ConfigureAwait(false);
        // 3. 游戏节点把租约记录的运行环境交给执行器，节点内的任务只使用它
        var context = new PuloniaTaskExecutionContext(runId, snapshot, attempt, task.TaskAddress, lease?.Runtime, accountId);
        // 4. 持有任务锁期间标记会话，执行器内部不能再通过 TaskRunner 启动任务
        using (lease is not null ? GameTaskSessionScope.Enter() : null)
        {
            return await executor.ExecuteAsync(task, context, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 在本进程的游戏中准备指定账户并返回完整核验 UID；续跑时核验历史 UID。
    /// </summary>
    /// <param name="account">运行快照中固定的账户资料；图片按快照路径读取，凭据按账户固定目标名读取最新值。</param>
    /// <param name="expectedUid">续跑时历史运行已核验的 UID；新运行为空。</param>
    /// <param name="switchStarted">切号器即将离开主界面去登出或登录时调用；之后失败时调用方据此把实例当前账户改为“未确定”。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>完整核验 UID。</returns>
    /// <exception cref="PuloniaGenshinAccountException">无法准备游戏环境、切换或核验账户。</exception>
    public async Task<string> PrepareAccountAsync(PuloniaGenshinAccountProfile account, string? expectedUid,
        Action switchStarted, CancellationToken ct)
    {
        var accountSwitcher = _accountSwitcher
                              ?? throw new PuloniaGenshinAccountException("当前实例没有账户切换能力。", new InvalidOperationException());
        // 1. 账户准备同样需要独占游戏输入；环境无法准备时按账户失败处理，不进入业务
        PuloniaGameTaskLease preparedLease;
        try { preparedLease = await AcquireLeaseAsync(ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { throw new PuloniaGenshinAccountException("无法准备账户的游戏环境：" + ex.Message, ex); }
        using var lease = preparedLease;
        // 2. 切换并核验；续跑必须核验历史实际 UID，不能自动采用另一个当前登录账户
        try
        {
            var uid = await accountSwitcher.PrepareAsync(lease.Runtime, account, switchStarted, ct).ConfigureAwait(false);
            if (expectedUid is null)
                return uid;
            await accountSwitcher.VerifyAsync(lease.Runtime, expectedUid, ct).ConfigureAwait(false);
            return expectedUid;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 失败原因由调用方（任务服务或远程节点宿主）写入日志窗口和执行记录，这里只转换为账户失败。
            throw new PuloniaGenshinAccountException(ex.Message, ex);
        }
    }

    /// <summary>
    /// 在本进程的游戏中只读地读取当前 UID，供跟随当前账户的 UID 比对：不按键，最多约 3 秒。
    /// 没有切号器、取不到游戏环境或识别失败时都返回 null（记为未比对），不阻止运行。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>完整 UID；读不到时为 null。</returns>
    public async Task<string?> TryReadUidAsync(CancellationToken ct)
    {
        if (_accountSwitcher is null)
            return null;
        try
        {
            using var lease = await AcquireLeaseAsync(ct).ConfigureAwait(false);
            return await _accountSwitcher.TryReadUidAsync(lease.Runtime, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 读不到 UID 只是不比对；游戏环境问题由随后的游戏节点按原有规则报告。
            return null;
        }
    }

    /// <summary>
    /// 核验当前登录 UID；没有账户切换能力时跳过（纯任务服务）。
    /// </summary>
    private async Task VerifyAsync(GameRuntime runtime, string expectedUid, CancellationToken ct)
    {
        if (_accountSwitcher is null) return;
        try { await _accountSwitcher.VerifyAsync(runtime, expectedUid, ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { throw new PuloniaGenshinAccountException(ex.Message, ex); }
    }
}
