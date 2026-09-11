using System;
using System.Threading;

namespace BetterGenshinImpact.GameTask.ExceptionRecovery;

/// <summary>
/// 「暂停脚本」信号：由异常弹窗恢复流程（L1）与断网看门狗（L2）写入，
/// 由任务线程在 <see cref="BetterGenshinImpact.GameTask.Common.TaskControl.TrySuspend"/> 中读取并等待。
///
/// 设计要点：
/// 1. **两个来源各自独立记名与独立计时**：任一来源解除都不会误解另一个；
///    超时也按来源分别判定，避免"新挂起继承旧挂起已用时长被提前放行"。
/// 2. **恢复流程自身的执行栈**通过 <see cref="EnterRecoveryScope"/> 标记，
///    其内部的 Delay/Sleep → TrySuspend 不会被自己的挂起挡住（防自锁）。
/// 3. **挂起只允许阻塞任务线程**：截图调度线程与 UI 线程绝不阻塞
///    （阻塞调度线程会导致不再截图 → 解除路径消失，形成无法自愈的循环依赖）。
/// 4. 带按来源的超时兜底与"功能已关闭即解除"，绝不允许永久挂住脚本。
/// </summary>
public static class ExceptionSuspendSignal
{
    /// <summary>恢复流程自身执行栈的标记（随 await 流转）。</summary>
    private static readonly AsyncLocal<bool> RecoveryScopeFlag = new();

    private static volatile bool _suspendedByNetwork;
    private static volatile bool _suspendedByRecovery;
    private static long _networkSuspendStartTicks;
    private static long _recoverySuspendStartTicks;
    private static long _lastForcedNetworkReleaseTicks;

    /// <summary>
    /// 挂起"回合"序号：每次由"无挂起"变为"有挂起"时自增。
    /// 供等待方判断"这是不是一次新的挂起"（例如只需在每次挂起的第一次让位时放开被按住的键）。
    /// </summary>
    private static int _suspendEpoch;

    /// <summary>当前挂起回合序号（见 <see cref="_suspendEpoch"/>）。</summary>
    public static int SuspendEpoch => Volatile.Read(ref _suspendEpoch);

    /// <summary>是否因网络异常挂起。</summary>
    public static bool IsSuspendedByNetwork => _suspendedByNetwork;

    /// <summary>是否因异常弹窗恢复挂起。</summary>
    public static bool IsSuspendedByRecovery => _suspendedByRecovery;

    /// <summary>是否有任一来源挂起。</summary>
    public static bool IsSuspended => _suspendedByNetwork || _suspendedByRecovery;

    /// <summary>任务线程是否应当阻塞等待（恢复流程自身执行栈返回 false）。</summary>
    public static bool ShouldBlockTask => IsSuspended && !RecoveryScopeFlag.Value;

    /// <summary>网络来源已挂起时长（未挂起为 <see cref="TimeSpan.Zero"/>）。</summary>
    public static TimeSpan NetworkSuspendedDuration =>
        _suspendedByNetwork ? ElapsedSince(_networkSuspendStartTicks) : TimeSpan.Zero;

    /// <summary>恢复来源已挂起时长（未挂起为 <see cref="TimeSpan.Zero"/>）。</summary>
    public static TimeSpan RecoverySuspendedDuration =>
        _suspendedByRecovery ? ElapsedSince(_recoverySuspendStartTicks) : TimeSpan.Zero;

    /// <summary>
    /// 最近一次"被强制放行"（超时兜底）的时刻（UTC）。
    /// 看门狗据此在冷却期内不再重新挂起，避免"每 30 分钟只放行几秒"。
    /// </summary>
    public static DateTime LastForcedNetworkReleaseAt =>
        _lastForcedNetworkReleaseTicks == 0
            ? DateTime.MinValue
            : new DateTime(Interlocked.Read(ref _lastForcedNetworkReleaseTicks), DateTimeKind.Utc);

    /// <summary>由断网看门狗调用。</summary>
    public static void SuspendByNetwork()
    {
        // 已在网络挂起中就不再刷新起始时刻：否则重复挂起（并发的检查、或短时间内多次判定）
        // 会把"按来源计时的超时上限"一次次推后，任务可能被冻得远比配置的时长更久。
        if (_suspendedByNetwork)
        {
            return;
        }

        // 只有"从完全没挂起 → 有挂起"才算新的一回合：两个来源先后建立挂起属于**同一次**挂起，
        // 不该让等待方以为又来了一次而重复放开键鼠（那会把用户自己按住的、与脚本无关的键也松掉）。
        if (!IsSuspended)
        {
            Interlocked.Increment(ref _suspendEpoch);
        }

        Interlocked.Exchange(ref _networkSuspendStartTicks, DateTime.UtcNow.Ticks);
        _suspendedByNetwork = true;
    }

    /// <summary>由断网看门狗在确认网络恢复后调用；forced 表示是超时兜底强制放行。</summary>
    public static void ResumeByNetwork(bool forced = false)
    {
        _suspendedByNetwork = false;
        if (forced)
        {
            Interlocked.Exchange(ref _lastForcedNetworkReleaseTicks, DateTime.UtcNow.Ticks);
        }
    }

    /// <summary>由异常弹窗恢复流程在开始恢复前调用。</summary>
    public static void SuspendByRecovery()
    {
        // 与 SuspendByNetwork 同口径：只有从"完全没挂起"变为"有挂起"时才算新回合。
        // 重复调用仍刷新起始时刻（保持原有语义）。
        if (!IsSuspended)
        {
            Interlocked.Increment(ref _suspendEpoch);
        }

        Interlocked.Exchange(ref _recoverySuspendStartTicks, DateTime.UtcNow.Ticks);
        _suspendedByRecovery = true;
    }

    /// <summary>由异常弹窗恢复流程在结束（无论成败）后调用。</summary>
    public static void ResumeByRecovery()
    {
        _suspendedByRecovery = false;
    }

    /// <summary>清空全部挂起（截图器停止这类真正的会话边界使用）。</summary>
    public static void ResumeAll()
    {
        _suspendedByNetwork = false;
        _suspendedByRecovery = false;

        // 冷却戳的寿命跟着"截图器会话"走：重开截图器即视为新会话，重新按实时状态检测网络。
        // 注意**不能**在任务边界（ClearTriggers）清戳——配置组会为每个项目调用一次，
        // 那样"强制放行 5 分钟冷却"会被反复抹掉，退回"刚放行就立刻重新挂起"。
        Interlocked.Exchange(ref _lastForcedNetworkReleaseTicks, 0);
    }

    /// <summary>需要被释放的挂起来源（按位组合）。</summary>
    [Flags]
    public enum SuspendRelease
    {
        None = 0,
        Network = 1,
        Recovery = 2
    }

    /// <summary>
    /// 按来源判定挂起是否到期，返回**应释放**的来源（只做判定，不改状态）。
    /// 两个来源各有自己的上限：恢复来源跟着 <c>PopupRecoveryTimeoutMinutes</c>（它自己的超时），
    /// 不能借用网络项的 <c>NetworkSuspendMaxMinutes</c>——那会让用户调大网络上限时，
    /// 连带把"恢复流程卡住"的兜底时间一起放大。
    /// 判定与释放分开，是为了让调用方在释放恢复来源前先"确认恢复流程已退出"。
    /// </summary>
    public static SuspendRelease EvaluateExpiredReleases(TimeSpan networkMaxSuspend, TimeSpan recoveryMaxSuspend)
    {
        var result = SuspendRelease.None;

        if (_suspendedByNetwork && networkMaxSuspend > TimeSpan.Zero && NetworkSuspendedDuration >= networkMaxSuspend)
        {
            result |= SuspendRelease.Network;
        }

        if (_suspendedByRecovery && recoveryMaxSuspend > TimeSpan.Zero && RecoverySuspendedDuration >= recoveryMaxSuspend)
        {
            result |= SuspendRelease.Recovery;
        }

        return result;
    }

    /// <summary>
    /// 对应功能已被用户关闭时，返回**应释放**的来源（只做判定，不改状态）。
    /// </summary>
    public static SuspendRelease EvaluateDisabledReleases(bool popupRecoveryEnabled, bool networkDetectionEnabled)
    {
        var result = SuspendRelease.None;

        if (_suspendedByRecovery && !popupRecoveryEnabled)
        {
            result |= SuspendRelease.Recovery;
        }

        if (_suspendedByNetwork && !networkDetectionEnabled)
        {
            result |= SuspendRelease.Network;
        }

        return result;
    }

    /// <summary>
    /// 进入"恢复流程自身执行栈"作用域。作用域内 <see cref="ShouldBlockTask"/> 恒为 false，
    /// 使恢复流程内部使用的 TaskControl.Delay/Sleep 不会因自身挂起而自锁。
    /// </summary>
    public static IDisposable EnterRecoveryScope()
    {
        var previous = RecoveryScopeFlag.Value;
        RecoveryScopeFlag.Value = true;
        return new RecoveryScope(previous);
    }

    /// <summary>
    /// 当前是否在"恢复流程自身执行栈"里。供 TaskControl 判断：恢复流程自己负责确认游戏前台
    /// （每次动作前 <c>EnsureGameFocus</c>），因此其中的等待不必再做焦点恢复——
    /// 而重登期间游戏窗口本来就不在前台，在那里抛 RetryException 只会被 NewRetry
    /// 放大成每次约 100 秒的无效等待，把恢复的超时预算吃光。
    /// </summary>
    public static bool InRecoveryScope => RecoveryScopeFlag.Value;

    private static TimeSpan ElapsedSince(long startTicks)
    {
        if (startTicks == 0)
        {
            return TimeSpan.Zero;
        }

        var elapsed = DateTime.UtcNow.Ticks - Interlocked.Read(ref startTicks);
        return elapsed <= 0 ? TimeSpan.Zero : TimeSpan.FromTicks(elapsed);
    }

    private sealed class RecoveryScope(bool previous) : IDisposable
    {
        public void Dispose()
        {
            RecoveryScopeFlag.Value = previous;
        }
    }
}
