using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Model.Area;
using Fischless.GameCapture;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Vanara.PInvoke;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.ExceptionRecovery;

namespace BetterGenshinImpact.GameTask.Common;

public class TaskControl
{
    public static ILogger Logger { get; } = App.GetLogger<TaskControl>();

    public static readonly SemaphoreSlim TaskSemaphore = new(1, 1);


    public static void CheckAndSleep(int millisecondsTimeout)
    {
        TrySuspend();
        CheckAndActivateGameWindow();

        Thread.Sleep(millisecondsTimeout);
    }

    public static void Sleep(int millisecondsTimeout)
    {
        NewRetry.Do(() =>
        {
            TrySuspend();
            CheckAndActivateGameWindow();
        }, TimeSpan.FromSeconds(1), 100);
        Thread.Sleep(millisecondsTimeout);
    }

    private static bool IsKeyPressed(User32.VK key)
    {
        // 获取按键状态
        var state = User32.GetAsyncKeyState((int)key);

        // 检查高位是否为 1（表示按键被按下）
        return (state & 0x8000) != 0;
    }

    public static void TrySuspend()
    {
        TrySuspend(CancellationToken.None);
    }

    /// <summary>
    /// 等待挂起解除。<paramref name="ct"/> 为调用方自己的取消令牌（若有）：
    /// 它在等待期间被取消时同样要立刻让出，不能只依赖全局的"停止任务"状态。
    /// </summary>
    public static void TrySuspend(CancellationToken ct)
    {
        // 异常弹窗恢复（L1）/ 断网看门狗（L2）触发的挂起：在此等待，带超时兜底
        WaitWhileExceptionSuspended(ct);

        var first = true;
        //此处为了记录最开始的暂停状态
        var isSuspend = RunnerContext.Instance.IsSuspend;
        while (RunnerContext.Instance.IsSuspend)
        {
            // 调用方自己的令牌取消时立刻让出：任务正在被拆掉，不该被"用户暂停"再扣住。
            // 这里只返回（不抛），后续取消处理仍由调用方既有的检查负责。
            if (ct.IsCancellationRequested)
            {
                return;
            }

            if (first)
            {
                RunnerContext.Instance.StopAutoPick();
                //使快捷键本身释放
                Thread.Sleep(300);
                foreach (User32.VK key in Enum.GetValues(typeof(User32.VK)))
                {
                    // 检查键是否被按下
                    if (IsKeyPressed(key)) // 强制转换 VK 枚举为 int
                    {
                        Logger.LogWarning($"解除{key}的按下状态.");
                        Simulation.SendInput.Keyboard.KeyUp(key);
                    }
                }

                Logger.LogWarning("快捷键触发暂停，等待解除");
                foreach (var item in RunnerContext.Instance.SuspendableDictionary)
                {
                    item.Value.Suspend();
                }

                first = false;
            }

            Thread.Sleep(1000);
        }

        //从暂停中解除
        if (isSuspend)
        {
            Logger.LogWarning("暂停已经解除");
            RunnerContext.Instance.ResumeAutoPick();
            foreach (var item in RunnerContext.Instance.SuspendableDictionary)
            {
                item.Value.Resume();
            }
        }
    }

    /// <summary>
    /// 等待"异常弹窗恢复（L1）/ 断网看门狗（L2）"触发的挂起被解除。
    ///
    /// **只在任务线程上阻塞**：
    /// * UI 线程：绝不允许阻塞（否则用户连「停止」都点不动）；
    /// * 截图调度线程（触发器回调）：绝不允许阻塞——该线程一旦卡住就不再截图，
    ///   异常检测与网络复核都会停摆，形成无法自愈的循环依赖；
    ///   挂起期间调度器会跳过其它会操作游戏的触发器（见 ITaskTrigger.AlwaysActive）。
    /// 两者都只记录日志后立即让位，由任务线程承担等待。
    ///
    /// 任务线程的等待循环带三重保障：
    /// 1. 用户取消（停止任务）即退出；
    /// 2. 对应功能被关闭 / 按来源计时的上限到点 → **请求中止恢复流程并等它真正结束**后解除该来源的挂起
    ///    （与茶包版"恢复流程跑完才解除"同语义，避免任务与恢复流程并发操作游戏）；
    /// 3. "本次阻塞总时长"的最终兜底：到点仍等不到恢复流程退出时**强制放行**（打 Error 日志），
    ///    避免恢复流程卡死导致任务无限期停摆。
    /// 恢复流程自身执行栈不会被阻塞（见 <see cref="ExceptionSuspendSignal.EnterRecoveryScope"/>）。
    ///
    /// 等待期间**不做焦点恢复**：挂起时游戏本就处于异常态（弹窗/断网），
    /// 抢焦点应由恢复流程负责；而 CheckAndActivateGameWindow 内部的抢焦点循环没有次数上限，
    /// 放在这里会让取消与超时兜底全部失效。解除挂起后的下一次 Sleep/Delay 会照常自愈。
    /// </summary>
    /// <param name="ct">
    /// 调用方自己的取消令牌（可能不是"停止任务"那个）：它被取消时同样立刻结束等待，
    /// 否则带超时的子任务会在挂起期间一直卡到挂起上限。
    /// </param>
    private static void WaitWhileExceptionSuspended(CancellationToken ct)
    {
        if (!ExceptionSuspendSignal.ShouldBlockTask)
        {
            return;
        }

        // UI 线程与截图调度线程一律不让位阻塞
        if (IsUiThread() || TaskTriggerDispatcher.IsInTriggerCallback)
        {
            return;
        }

        Logger.LogWarning("检测到游戏异常（{Reason}），脚本已暂停，等待自动恢复",
            ExceptionSuspendSignal.IsSuspendedByNetwork ? "网络不可用" : "异常弹窗处理中");

        var lastLogAt = DateTime.UtcNow;
        var waitStartedAt = DateTime.UtcNow;

        // 本次挂起第一次进入等待时，先放开"被按住的键/鼠标键"：
        // 挂起会让调用方停在 KeyDown 与 KeyUp 之间（冲刺 KeyDown → Sleep → KeyUp、
        // 拖拽 LeftButtonDown → Delay → LeftButtonUp、走路时按住 W…），而挂起可能持续很久
        // （网络来源默认上限 30 分钟），不放开的话这段时间游戏会一直收到"按住"的输入。
        // 做法与上游"用户暂停"分支一致（见 TrySuspend）。
        if (_releasedKeysForSuspendEpoch != ExceptionSuspendSignal.SuspendEpoch)
        {
            _releasedKeysForSuspendEpoch = ExceptionSuspendSignal.SuspendEpoch;
            ReleaseHeldInputsOnSuspend();
        }

        while (ExceptionSuspendSignal.ShouldBlockTask)
        {
            // 0) 调用方自己的取消令牌（例如某个任务的局部超时）→ 立刻退出等待
            if (ct.IsCancellationRequested)
            {
                Logger.LogWarning("调用方已取消，结束异常恢复等待");
                return;
            }

            // 1) 用户停止任务 → 立刻退出等待（否则任务线程会占着信号量不释放）
            if (Core.Script.CancellationContext.Instance.IsCancellationRequested)
            {
                Logger.LogWarning("任务已取消，结束异常恢复等待");
                return;
            }

            var config = TaskContext.Instance().Config.OtherConfig.ExceptionRecoveryConfig;

            // 2) 功能被关闭 → 立即解除对应来源。
            //    forceRecoveryRelease: true —— 用户已经明确关掉功能，不能因为"恢复流程 3 秒内没退出"
            //    就继续冻结脚本（否则要一直冻到恢复来源的硬上限，默认 13 分钟）。
            var disabled = ExceptionSuspendSignal.EvaluateDisabledReleases(
                config.PopupRecoveryEnabled, config.NetworkDetectionEnabled);
            if (disabled != ExceptionSuspendSignal.SuspendRelease.None)
            {
                if (ShouldLogReleaseLadder())
                {
                    Logger.LogWarning("异常恢复功能已关闭，解除挂起（{Source}）", disabled);
                }

                ReleaseSuspensions(disabled, recoveryCountAsCancel: true, networkForced: false, forceRecoveryRelease: true);
            }

            // 3) 网络来源：等待方自己复核连通性，使"恢复后自动继续"不依赖截图器状态
            NetworkWatchdogTrigger.ProbeIfSuspended();

            // 4) 按来源分别判定超时（只释放到期的那一个）
            //    两个来源的上限各归各的：恢复来源用恢复流程自己的超时 + 1 分钟余量，
            //    不能借用网络上限（用户把网络上限调大时，不该连带放大恢复的兜底时间）。
            var networkMaxSuspend = ExceptionRecoveryDecisions.NetworkSuspendLimit(config.NetworkSuspendMaxMinutes);
            var recoveryMaxSuspend = TimeSpan.FromMinutes(Math.Clamp(config.PopupRecoveryTimeoutMinutes, 1, 30) + 1);
            var expired = ExceptionSuspendSignal.EvaluateExpiredReleases(networkMaxSuspend, recoveryMaxSuspend);
            if (expired != ExceptionSuspendSignal.SuspendRelease.None)
            {
                if (ShouldLogReleaseLadder())
                {
                    Logger.LogError("异常恢复挂起超过上限仍未解除，强制放行（{Source}）", expired);
                }

                // 超时放行恢复来源时同样要让恢复流程停下（用"超时中止"而不是"取消"：
                // 卡过超时的恢复仍要计入失败，否则熔断永远不会触发）。
                ReleaseSuspensions(expired, recoveryCountAsCancel: false, networkForced: true);
            }

            // 4.5) 恢复来源的**硬上限**：上面已经"请求中止 + 有界等待"，若恢复流程仍未退出，
            //      到点就强制放行。判据用"恢复来源自身的已挂起时长"，**不受网络上限配置影响**——
            //      否则用户把 NetworkSuspendMaxMinutes 调大时，卡死的恢复会把任务一直拖到那个上限
            //      （总时长兜底取的是两者的较大值，默认就是 30 分钟，配置上限可到 24 小时）。
            if (ExceptionSuspendSignal.IsSuspendedByRecovery
                && ExceptionSuspendSignal.RecoverySuspendedDuration >= recoveryMaxSuspend + RecoveryForceReleaseMargin)
            {
                Logger.LogError("恢复流程超过上限 {Minutes} 分钟仍未退出，强制解除恢复挂起",
                    (recoveryMaxSuspend + RecoveryForceReleaseMargin).TotalMinutes);
                GameExceptionPopupTrigger.ForceResumeRecoverySuspension();
            }

            // 5) 本次阻塞总时长的粗粒度兜底（不依赖挂起来源的计时）：只是"其他路径都没生效"时的最后一道网。
            //    取"两个来源各自的界（恢复来源含其强制放行宽限）中较大者 + 1 分钟"——
            //    这样它永远晚于按来源判定与 4.5 的硬上限，不会抢先替某个来源做决定
            //    （否则用户把网络上限调小时，它会比恢复来源的硬上限先到，把恢复的宽限反向缩短）。
            var recoveryBound = recoveryMaxSuspend + RecoveryForceReleaseMargin;
            var maxSuspend = (networkMaxSuspend > recoveryBound ? networkMaxSuspend : recoveryBound)
                             + TimeSpan.FromMinutes(1);
            if (DateTime.UtcNow - waitStartedAt >= maxSuspend)
            {
                Logger.LogError("异常恢复等待累计超过 {Minutes} 分钟，强制恢复脚本执行", maxSuspend.TotalMinutes);

                // 必须走"强制放行"：它会留下冷却戳，否则下一次网络检查（30 秒后）会立刻重新挂起，
                // 兜底等于没做（又要再等满一个挂起上限）。
                var allSuspended = ExceptionSuspendSignal.SuspendRelease.None;
                if (ExceptionSuspendSignal.IsSuspendedByNetwork)
                {
                    allSuspended |= ExceptionSuspendSignal.SuspendRelease.Network;
                }

                if (ExceptionSuspendSignal.IsSuspendedByRecovery)
                {
                    allSuspended |= ExceptionSuspendSignal.SuspendRelease.Recovery;
                }

                if (allSuspended != ExceptionSuspendSignal.SuspendRelease.None)
                {
                    // 最终兜底：到这里已经等了 max(networkMax, recoveryMax)（默认 30 分钟），
                    // 允许强制放行恢复来源，避免恢复流程卡死导致任务无限期停摆。
                    ReleaseSuspensions(allSuspended, recoveryCountAsCancel: false, networkForced: true, forceRecoveryRelease: true);
                }

                break;
            }

            Thread.Sleep(200);

            if ((DateTime.UtcNow - lastLogAt).TotalSeconds >= 30)
            {
                lastLogAt = DateTime.UtcNow;
                Logger.LogWarning("仍在等待游戏异常恢复（网络已挂起 {Network} 秒 / 弹窗恢复已进行 {Recovery} 秒）",
                    (int)ExceptionSuspendSignal.NetworkSuspendedDuration.TotalSeconds,
                    (int)ExceptionSuspendSignal.RecoverySuspendedDuration.TotalSeconds);
            }
        }
    }

    /// <summary>
    /// 解除指定来源的挂起。
    ///
    /// **恢复来源要先关闭"独占窗口"再解挂**：请求中止在途恢复流程，并**有界等待**它真正结束
    /// （最多 <see cref="GameExceptionPopupTrigger.RecoveryJoinTimeoutMs"/> 毫秒），确认结束后才清除挂起标志。
    /// 这与茶包版把恢复流程放在同一个调用栈里 `await` 的语义一致——任务恢复执行时，
    /// 恢复流程已经不会再点击游戏。
    ///
    /// 宽限期内没等到恢复流程退出时，本方法**不会**强放行（保持挂起，等恢复流程自己的
    /// <c>TryReleaseOwnership</c> 归还所有权并清标志），只有 <paramref name="forceRecoveryRelease"/>
    /// 为 true 的"最终兜底"路径才强制放行——否则恢复流程一旦卡死，任务会无限期停摆。
    /// </summary>
    /// <param name="sources">要解除的来源。</param>
    /// <param name="recoveryCountAsCancel">恢复来源的中止是否按"取消"记账（功能被用户关闭 = 取消；超时放行 = 失败）。</param>
    /// <param name="networkForced">网络来源是否按"强制放行"记账（写了冷却戳，冷却期内不再重新挂起）。</param>
    /// <param name="forceRecoveryRelease">恢复流程未按期退出时，是否强制放行（仅最终兜底路径为 true）。</param>
    private static void ReleaseSuspensions(
        ExceptionSuspendSignal.SuspendRelease sources,
        bool recoveryCountAsCancel,
        bool networkForced,
        bool forceRecoveryRelease = false)
    {
        if ((sources & ExceptionSuspendSignal.SuspendRelease.Recovery) != 0)
        {
            if (GameExceptionPopupTrigger.AbortAndJoinRecovery(recoveryCountAsCancel))
            {
                // "确认没有在途恢复 + 解挂"必须在同一临界区内完成：
                // 否则截图调度线程可能刚好在两步之间启动新一轮恢复并立起新挂起，
                // 这里就会把它的挂起抹掉，造成主任务与新恢复并发操作游戏。
                GameExceptionPopupTrigger.ResumeRecoverySuspensionIfIdle();
            }
            else if (forceRecoveryRelease)
            {
                // 唯一的最终兜底：任务已经等了远超恢复流程自身超时的时间，再等就是无限期停摆，
                // 因此强制放行并打 Error 日志（此刻确实可能与恢复流程短暂重叠）。
                Logger.LogError("恢复流程长时间未退出，强制解除恢复挂起（可能与恢复流程短暂重叠）");
                GameExceptionPopupTrigger.ForceResumeRecoverySuspension();
            }
            else
            {
                // 等待路径：保持挂起，由恢复流程自己的收尾（TryReleaseOwnership）解挂。
                // 日志节流：等待循环每 200ms 一轮，这里最多每 30 秒提示一次。
                if ((DateTime.UtcNow - _lastRecoveryJoinWaitLogAt).TotalSeconds >= 30)
                {
                    _lastRecoveryJoinWaitLogAt = DateTime.UtcNow;
                    Logger.LogWarning("恢复流程尚未退出，继续保持挂起等待其收尾（上限 {Seconds} 秒后进入最终兜底）",
                        GameExceptionPopupTrigger.RecoveryJoinTimeoutMs / 1000.0);
                }
            }
        }

        if ((sources & ExceptionSuspendSignal.SuspendRelease.Network) != 0)
        {
            ExceptionSuspendSignal.ResumeByNetwork(forced: networkForced);
        }
    }

    /// <summary>"仍在等待恢复流程退出"提示的上次输出时刻（日志节流用）。</summary>
    private static DateTime _lastRecoveryJoinWaitLogAt = DateTime.MinValue;

    /// <summary>释放阶梯日志（功能关闭 / 超时放行）上次输出时刻：等待循环每 200ms 一轮，必须节流。</summary>
    private static DateTime _lastReleaseLadderLogAt = DateTime.MinValue;

    /// <summary>已经为哪一次挂起放开过"被按住的键"（避免同一次挂起反复松键并刷日志）。</summary>
    private static int _releasedKeysForSuspendEpoch;

    /// <summary>释放阶梯日志的 30 秒节流（同一轮里可能有多次判定，不能每轮各刷一条）。</summary>
    private static bool ShouldLogReleaseLadder()
    {
        if ((DateTime.UtcNow - _lastReleaseLadderLogAt).TotalSeconds < 30)
        {
            return false;
        }

        _lastReleaseLadderLogAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>
    /// 挂起前放开所有"被按住的键与鼠标键"。
    ///
    /// 挂起会让调用方**停在 KeyDown 与 KeyUp 之间**（冲刺、走路按住 W、拖拽按住左键…），
    /// 若不放开，脚本被冻多久这些键就被按住多久（网络来源默认上限 30 分钟，可配到 24 小时），
    /// 期间游戏会一直收到"按住"的输入。上游"用户暂停"分支已经这么做（见 <see cref="TrySuspend"/>），
    /// 这里保持一致，只是额外补上鼠标键（上游那处用键盘接口发 VK_LBUTTON，实际松不开鼠标）。
    /// </summary>
    private static void ReleaseHeldInputsOnSuspend()
    {
        try
        {
            foreach (User32.VK key in Enum.GetValues(typeof(User32.VK)))
            {
                if (!IsKeyPressed(key))
                {
                    continue;
                }

                switch (key)
                {
                    case User32.VK.VK_LBUTTON:
                        Simulation.SendInput.Mouse.LeftButtonUp();
                        break;
                    case User32.VK.VK_RBUTTON:
                        Simulation.SendInput.Mouse.RightButtonUp();
                        break;
                    case User32.VK.VK_MBUTTON:
                        Simulation.SendInput.Mouse.MiddleButtonUp();
                        break;
                    case User32.VK.VK_XBUTTON1:
                        Simulation.SendInput.Mouse.XButtonUp(0x0001);
                        break;
                    case User32.VK.VK_XBUTTON2:
                        Simulation.SendInput.Mouse.XButtonUp(0x0002);
                        break;
                    default:
                        Simulation.SendInput.Keyboard.KeyUp(key);
                        break;
                }

                Logger.LogWarning("异常恢复挂起：解除 {Key} 的按下状态", key);
            }
        }
        catch (Exception ex)
        {
            // 放开按键失败不影响挂起本身
            Logger.LogDebug(ex, "异常恢复挂起：放开按键时出现异常（已忽略）");
        }
    }

    /// <summary>
    /// 恢复来源在"按来源超时 + 请求中止 + 有界等待"之后，仍允许等待的额外宽限：
    /// 超过"恢复上限 + 本宽限"就强制解除恢复挂起（打 Error 日志）。
    /// 用恢复来源自身的时限计算，避免被网络挂起上限的配置拖长。
    /// </summary>
    private static readonly TimeSpan RecoveryForceReleaseMargin = TimeSpan.FromMinutes(2);

    /// <summary>
    /// 供"不在 TaskControl 调用链上"的等待点使用（目前是 JS 脚本的 <c>sleep()</c>）：
    /// 只等待异常恢复挂起解除，不做焦点恢复、也不处理用户暂停（各自的既有语义保持不变）。
    /// 没有这一步，JS 脚本（一条龙 / 脚本组）在挂起期间仍会持续按键点击，与恢复流程并发操作游戏。
    /// </summary>
    public static void WaitWhileGameSuspendedByException()
    {
        WaitWhileExceptionSuspended(CancellationToken.None);
    }

    private static bool IsUiThread()
    {
        try
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            return dispatcher != null && dispatcher.CheckAccess();
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void CheckAndActivateGameWindow()
    {
        // 恢复流程自身执行栈：它每次动作前都会自己确认游戏前台（GamePopupRecoveryJob.EnsureGameFocus），
        // 而重登期间游戏窗口本来就不在前台——在这里抛 RetryException 会被 NewRetry 放大成
        // 每次约 100 秒的无效等待（且不可取消），把恢复的超时预算吃光。
        if (ExceptionSuspendSignal.InRecoveryScope)
        {
            return;
        }

        if (!TaskContext.Instance().Config.OtherConfig.RestoreFocusOnLostEnabled)
        {
            if (!SystemControl.IsGenshinImpactActiveByProcess())
            {
                var name = SystemControl.GetActiveByProcess();
                Logger.LogWarning($"当前获取焦点的窗口为: {name}，不是原神，暂停");
                throw new RetryException("当前获取焦点的窗口不是原神");
            }
        }

        var count = 0;
        //未激活则尝试恢复窗口
        while (!SystemControl.IsGenshinImpactActiveByProcess())
        {
            if (count >= 10 && count % 10 == 0)
            {
                Logger.LogInformation("多次尝试未恢复，尝试最小化后激活窗口！");
                SystemControl.MinimizeAndActivateWindow(TaskContext.Instance().GameHandle);
            }
            else
            {
                var name = SystemControl.GetActiveByProcess();
                Logger.LogInformation("当前获取焦点的窗口为: {Name}，不是原神，尝试恢复窗口", name);
                SystemControl.FocusWindow(TaskContext.Instance().GameHandle);
            }

            count++;
            Thread.Sleep(1000);
        }
    }

    public static void Sleep(int millisecondsTimeout, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            throw new NormalEndException("取消自动任务");
        }

        if (millisecondsTimeout <= 0)
        {
            return;
        }

        NewRetry.Do(() =>
        {
            if (ct.IsCancellationRequested)
            {
                throw new NormalEndException("取消自动任务");
            }

            TrySuspend(ct);
            if (ct.IsCancellationRequested)
            {
                // Sleep 的取消语义本来就是 NormalEndException（方法进入与返回处都是），这里保持一致；
                // 不要改成 ThrowIfCancellationRequested——那会改变本方法既有的异常契约。
                throw new NormalEndException("取消自动任务");
            }

            CheckAndActivateGameWindow();
        }, TimeSpan.FromSeconds(1), 100);
        Thread.Sleep(millisecondsTimeout);
        if (ct.IsCancellationRequested)
        {
            throw new NormalEndException("取消自动任务");
        }
    }

    public static async Task Delay(int millisecondsTimeout, CancellationToken ct)
    {
        if (ct is { IsCancellationRequested: true })
        {
            throw new NormalEndException("取消自动任务");
        }

        if (millisecondsTimeout <= 0)
        {
            return;
        }

        NewRetry.Do(() =>
        {
            if (ct is { IsCancellationRequested: true })
            {
                throw new NormalEndException("取消自动任务");
            }

            TrySuspend(ct);
            if (ct.IsCancellationRequested)
            {
                // 注意这里**必须保持 OperationCanceledException 语义**（不能用 NormalEndException）：
                // 调用方的 ct 可能是"局部超时令牌"而不是"用户停止任务"，上层依赖异常类型区分两者
                // （例如 TpTask 会把 OCE + 局部超时令牌转成 TimeoutException 按传送失败处理）。
                // 这也与原来 `await Task.Delay(ms, ct)` 在等待期间被取消时抛出的异常类型一致。
                ct.ThrowIfCancellationRequested();
            }

            CheckAndActivateGameWindow();
        }, TimeSpan.FromSeconds(1), 100);
        await Task.Delay(millisecondsTimeout, ct);
        if (ct is { IsCancellationRequested: true })
        {
            throw new NormalEndException("取消自动任务");
        }
    }

    /// <summary>
    /// 模拟长按指定动作。使用 try/finally 块确保在任务被取消或发生异常时，按键也能安全释放，防止卡键。
    /// </summary>
    /// <param name="action">需要模拟的游戏动作（如元素战技、普通攻击等）</param>
    /// <param name="holdMs">长按持续的时间（毫秒）</param>
    /// <param name="ct">用于监控任务取消的取消令牌</param>
    public static async Task SimulateHoldActionAsync(GIActions action, int holdMs, CancellationToken ct)
    {
        try
        {
            Simulation.SendInput.SimulateAction(action, KeyType.KeyDown);
            await Delay(holdMs, ct);
        }
        finally
        {
            Simulation.SendInput.SimulateAction(action, KeyType.KeyUp);        
        }
    }

    /// <summary>
    /// 模拟长按元素战技（如万叶长E）。包含释放前摇、长按以及释放后的缓冲延时。
    /// </summary>
    /// <param name="holdMs">元素战技按住的时间（毫秒）</param>
    /// <param name="ct">用于监控任务取消的取消令牌</param>
    /// <param name="releaseLeftMouseBefore">是否在按下元素战技前先松开鼠标左键，避免输入冲突，默认 true</param>
    /// <param name="releaseLeftMouseDelayMs">松开鼠标左键后的缓冲时间（毫秒），默认 10ms</param>
    /// <param name="postKeyUpDelayMs">元素战技释放后的缓冲时间（毫秒），默认 50ms</param>
    public static async Task SimulateHoldElementalSkillAsync(
        int holdMs,
        CancellationToken ct,
        bool releaseLeftMouseBefore = true,
        int releaseLeftMouseDelayMs = 10,
        int postKeyUpDelayMs = 50)
    {
        if (releaseLeftMouseBefore)
        {
            Simulation.SendInput.Mouse.LeftButtonUp();
            await Delay(releaseLeftMouseDelayMs, ct);
        }

        await SimulateHoldActionAsync(GIActions.ElementalSkill, holdMs, ct);   
        await Delay(postKeyUpDelayMs, ct);
    }

    /// <summary>
    /// 模拟鼠标左键连续点击循环（如万叶长E后的下落攻击）。双层 try/finally 设计以确保无论在循环的哪个阶段发生取消或异常，鼠标左键都会被强制释放。
    /// </summary>
    /// <param name="repeatCount">需要循环点击的次数</param>
    /// <param name="ct">用于监控任务取消的取消令牌</param>
    /// <param name="preUpDelayMs">每次点击前，预先抬起左键后的缓冲延时（毫秒），默认 10ms</param>
    /// <param name="downHoldMs">鼠标左键按下的保持时间（毫秒），默认 35ms</param>
    /// <param name="postUpDelayMs">每次点击完成后的等待时间（毫秒），默认 50ms</param>
    public static async Task SimulateMouseLeftClickLoopAsync(
        int repeatCount,
        CancellationToken ct,
        int preUpDelayMs = 10,
        int downHoldMs = 35,
        int postUpDelayMs = 50)
    {
        try
        {
            for (var i = 0; i < repeatCount; i++)
            {
                Simulation.SendInput.Mouse.LeftButtonUp();
                await Delay(preUpDelayMs, ct);
                Simulation.SendInput.Mouse.LeftButtonDown();
                try
                {
                    await Delay(downHoldMs, ct);
                }
                finally
                {
                    Simulation.SendInput.Mouse.LeftButtonUp();
                }

                await Delay(postUpDelayMs, ct);
            }
        }
        finally
        {
            Simulation.SendInput.Mouse.LeftButtonUp();
        }
    }

    public static Mat CaptureGameImage(IGameCapture? gameCapture)
    {
        var captureFrame = gameCapture?.Capture();
        var image = captureFrame?.Frame;
        if (image == null)
        {
            captureFrame?.Dispose();
            Logger.LogWarning("截图失败!");
            // 重试3次
            for (var i = 0; i < 3; i++)
            {
                captureFrame = gameCapture?.Capture();
                image = captureFrame?.Frame;
                if (image != null)
                {
                    return image;
                }

                captureFrame?.Dispose();
                Sleep(30);
            }

            throw new Exception("尝试多次后,截图失败!");
        }
        else
        {
            return image;
        }
    }

    public static Mat? CaptureGameImageNoRetry(IGameCapture? gameCapture)
    {
        return gameCapture?.Capture()?.Frame;
    }

    /// <summary>
    /// 自动判断当前运行上下文中截图方式，并选择合适的截图方式返回
    /// </summary>
    /// <returns></returns>
    public static ImageRegion CaptureToRectArea(bool forceNew = false)
    {
        var image = CaptureGameImage(TaskTriggerDispatcher.GlobalGameCapture);
        var content = new CaptureContent(image, 0, 0);
        return content.CaptureRectArea;
    }
}
