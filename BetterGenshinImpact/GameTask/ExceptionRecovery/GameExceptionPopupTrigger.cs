using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.GameTask.ExceptionRecovery;

/// <summary>
/// 【L1】游戏异常弹窗触发器。
///
/// 识别「更新通知」（游戏热更）/「连接已断开」/「连接超时」/「无法登录服务器」这类弹窗，
/// 命中后挂起脚本（挂起期间调度器会跳过其它会操作游戏的触发器），再异步执行
/// <see cref="GamePopupRecoveryJob"/> 把弹窗点掉并回到主界面。
///
/// 关键约定：
/// * 外观模板 + OCR 文案双证据才动作；点击仅认白名单文案；
/// * 单飞 + 连续失败熔断 + 冷却，误判不会无限劫持任务；
/// * <see cref="OnCapture"/> 运行在截图调度线程上，因此**绝不阻塞**（异常也不外抛）；
/// * 恢复流程自身通过 <see cref="ExceptionSuspendSignal.EnterRecoveryScope"/> 免疫自己的挂起。
/// </summary>
public class GameExceptionPopupTrigger : ITaskTrigger
{
    public string Name => "GameExceptionPopup";

    /// <summary>
    /// 注意 setter **故意留空**：`GameTaskManager.ConvertToTriggerList(allEnabled: true)`
    /// 会把字典里所有触发器置 true（自动秘境/配置组/脚本 API 的 AddTrigger 都会走到），
    /// 若在 setter 里写配置，就会把用户"默认关闭"的选择静默改成开启并落盘。
    /// 写法参照 GameLoadingTrigger（`set {}`）；UI 直接绑定 Config.OtherConfig.*，
    /// getter 实时读配置，因此开关切换仍然即时生效。
    /// </summary>
    public bool IsEnabled
    {
        get => TaskContext.Instance().Config.OtherConfig.ExceptionRecoveryConfig.PopupRecoveryEnabled;
        set { }
    }

    /// <summary>低于 GameLoadingTrigger(999)：先让启动/进入游戏流程处理，避免抢动作。</summary>
    public int Priority => 900;

    public bool IsExclusive => false;

    /// <summary>需要游戏在前台（点击动作必须有焦点窗口），后台不参与。</summary>
    public bool IsBackgroundRunning => false;

    /// <summary>常驻：任务运行期间仍需工作，且是解除挂起的责任方之一。</summary>
    public bool AlwaysActive => true;

    private static readonly ILogger Logger = App.GetLogger<GameExceptionPopupTrigger>();

    /// <summary>探测间隔的允许范围（秒）与默认值来源见 OtherConfig。</summary>
    private const int MinProbeIntervalSeconds = 2;
    private const int MaxProbeIntervalSeconds = 60;

    /// <summary>
    /// 以下运行期状态全部是**静态**的：触发器实例会在任务结束时被整体替换
    /// （LoadInitialTriggers 重建字典），实例字段会随实例一起丢失——
    /// 最明显的后果是"上一次恢复失败后刚熔断，跑一次任务熔断就被清零"，
    /// 以及"在途恢复失败时把失败次数记在已经没人引用的旧实例上"。
    /// </summary>
    private static DateTime _lastProbeAt = DateTime.MinValue;

    private static long _breakerUntilTicks;
    private static int _consecutiveFailures;

    /// <summary>
    /// 连续"整轮都没能对游戏做出任何动作"的轮数（用于 <see cref="HandleRecoveryResult"/>）。
    /// 这类轮次本身不计入失败（慢速加载不该被熔断），但如果一直如此，每 30 秒就会把任务
    /// 冻住约 50 秒（占空比坍缩），必须有界退让。
    /// </summary>
    private static int _noActionRounds;

    /// <summary>
    /// 恢复流程"收尾看门狗"在恢复自身超时之外额外等待的宽限（与 TaskControl 的
    /// <c>RecoveryForceReleaseMargin</c> 对齐：那道网只对"有任务线程在等"的情况生效）。
    /// </summary>
    private static readonly TimeSpan RecoveryWatchdogMargin = TimeSpan.FromMinutes(2);

    /// <summary>
    /// 在途恢复的"所有者会话代号"（0 = 没有在途恢复）。
    /// 与 <see cref="_recoverySessionEpoch"/> 一起表达单飞：两者相等才算"本会话确有在途恢复"。
    /// 用代次而不是裸的 0/1，是为了让会话切换（截图器停止）能够立刻交棒——
    /// 既不留下"永久为 1、本进程内再也不发起恢复"的死状态，
    /// 也不会在旧恢复还活着时把它当成新恢复（那会造成两个操作者同时点游戏）。
    /// </summary>
    private static int _recoveryOwnerEpoch;

    /// <summary>恢复会话代号：每次 <see cref="ResetSession"/> 自增。起始为 1（0 保留给"无所有者"）。</summary>
    private static int _recoverySessionEpoch = 1;

    private static readonly object RecoveryLock = new();

    /// <summary>在途恢复流程的取消源，供 <see cref="CancelRunningRecovery"/> 取消。</summary>
    private static CancellationTokenSource? _recoveryCts;

    /// <summary>
    /// 解除恢复挂起前，等待在途恢复流程真正结束的宽限时长（毫秒）。
    /// 与茶包版把恢复流程放在同一调用栈里 await 的语义对齐：先关闭"独占窗口"，再让任务继续。
    /// </summary>
    public const int RecoveryJoinTimeoutMs = 3000;

    /// <summary>
    /// 被"外部要求取消"的那次恢复所属的会话代号（0 = 没有取消请求）。
    /// 用来把"取消"与"失败"分开记账：误把取消算成失败，用户按几次「停止」就会把功能自己熔断掉。
    /// 绑定代次（而不是一个裸 bool）是必要的：否则"新一次恢复尝试抢单飞失败"这类路径
    /// 会把仍在运行的那次恢复的取消请求抹掉。
    /// </summary>
    private static int _abortRequestedEpoch;

    // 探测异常的日志升级（避免素材缺失/持续性异常静默失效，手册 R6）
    private static DateTime _probeFailureLogAt = DateTime.MinValue;
    private static int _probeFailureCount;

    /// <summary>
    /// 本会话是否确有在途恢复：所有者代次必须等于当前会话代次。
    /// 上一代会话遗留的所有者（截图中断/卡住的旧恢复）不算在途，因此不会永久堵死新恢复。
    /// </summary>
    private static bool IsRecoveryInFlight =>
        Volatile.Read(ref _recoveryOwnerEpoch) == Volatile.Read(ref _recoverySessionEpoch);

    /// <summary>
    /// 截图会话是否已经收尾（<see cref="ResetSession"/> 之后、下一次会话开始之前）。
    /// 用于拒绝"会话已停止、但一次在途 Tick 才开始抢单飞位"的启动请求。
    /// **只能由 <see cref="OnCaptureSessionStarted"/> 复位**：Init() 也会被任务结束
    /// （TaskRunner.End）与 AddTrigger 调用，在那个位置复位会放行一次"会话已停止后的恢复"。
    /// </summary>
    private static volatile bool _sessionStopped;

    /// <summary>
    /// 截图会话开始（由 <see cref="TaskTriggerDispatcher.Start"/> 调用）：允许发起恢复。
    /// </summary>
    public static void OnCaptureSessionStarted()
    {
        _sessionStopped = false;
    }

    public void Init()
    {
        // 注意这里**不清零**失败计数与熔断：Init 会在每次任务结束（TaskRunner.End → LoadInitialTriggers）
        // 与每次 AddTrigger（脚本实时触发器 / 自动秘境 / 自动巡礼）时被调用，
        // 一旦在此清零，"连续失败熔断 + 冷却"这层保护就会被反复抹掉、形同虚设。
        // 清零只发生在四个明确的语义点：恢复成功、冷却到期进入新一轮、用户关闭功能、截图会话边界。
        _lastProbeAt = DateTime.MinValue;
        Interlocked.Exchange(ref _probeFailureCount, 0);
        _probeFailureLogAt = DateTime.MinValue;

        // 关闭功能时清理可能残留的挂起，并确保在途恢复流程一起停下
        // （只清挂起不取消恢复，会让"已恢复执行的脚本"和"还在点击的恢复流程"同时操作游戏）
        if (!IsEnabled)
        {
            CancelRunningRecovery();

            // 只有没有在途恢复时才清标志：有在途恢复时由它自己的收尾（TryReleaseOwnership）解除挂起，
            // 避免"任务已被放行、恢复流程还在点击"。Init 可能在调度线程上执行，不做阻塞等待。
            ResumeRecoverySuspensionIfIdle();
            ResetProtectionState();
        }
    }

    /// <summary>清零失败计数与熔断（恢复成功、冷却到期、用户关闭功能、截图会话边界时调用）。</summary>
    public static void ResetProtectionState()
    {
        Interlocked.Exchange(ref _consecutiveFailures, 0);
        Interlocked.Exchange(ref _breakerUntilTicks, 0);
        Interlocked.Exchange(ref _noActionRounds, 0);
    }

    /// <summary>
    /// 兜底强制解除恢复来源挂起，但**只在没有在途恢复时**才动手。
    /// 无条件解挂会抹掉"新一轮恢复刚立起的挂起"，让主任务与恢复流程并发操作游戏；
    /// 有在途恢复时它已被请求中止，由它自己的收尾去解挂。
    /// </summary>
    public static void ResumeRecoverySuspensionIfIdle()
    {
        lock (RecoveryLock)
        {
            if (_recoveryOwnerEpoch == _recoverySessionEpoch)
            {
                // 确有在途恢复：交给它自己的收尾解挂
                return;
            }

            // 解挂必须与上面的判定在同一临界区：否则放开锁之后新恢复可能刚把挂起立起来，
            // 这一句就会把它抹掉（正是本方法要避免的竞态）。
            ExceptionSuspendSignal.ResumeByRecovery();
        }
    }

    /// <summary>
    /// 供恢复流程在每次动作前确认"自己仍属于当前截图会话"。
    /// 会话切换（截图器停止、新会话接管）后旧流程必须立刻停手，
    /// 否则它会与新会话的恢复流程同时点击游戏（取消只是协作式请求，不能当作"已停止"）。
    /// </summary>
    internal static bool IsCurrentSession(int epoch) =>
        Volatile.Read(ref _recoverySessionEpoch) == epoch;

    public void OnCapture(CaptureContent content)
    {
        // 防御：触发器异常绝不能冒泡到截图调度循环（会连累同帧其它触发器）
        try
        {
            OnCaptureCore(content);
            Interlocked.Exchange(ref _probeFailureCount, 0);
        }
        catch (Exception ex)
        {
            var failures = Interlocked.Increment(ref _probeFailureCount);
            // 首次与每 30 秒升级为 Warning：素材缺失/持续异常时用户能在界面上看到
            if (failures == 1 || (DateTime.UtcNow - _probeFailureLogAt).TotalSeconds >= 30)
            {
                _probeFailureLogAt = DateTime.UtcNow;
                Logger.LogWarning(ex, "[异常恢复] 弹窗探测异常（第 {Count} 次），本功能可能未生效", failures);
            }
        }
    }

    private void OnCaptureCore(CaptureContent content)
    {
        var config = TaskContext.Instance().Config.OtherConfig.ExceptionRecoveryConfig;
        if (!config.PopupRecoveryEnabled)
        {
            return;
        }

        var now = DateTime.Now;
        var interval = Math.Clamp(config.PopupRecoveryProbeIntervalSeconds, MinProbeIntervalSeconds, MaxProbeIntervalSeconds);
        if (!ExceptionRecoveryDecisions.ShouldProbe(now, _lastProbeAt, interval))
        {
            return;
        }

        _lastProbeAt = now;

        if (!ExceptionRecoveryDecisions.ShouldStartRecovery(
                recoveryInFlight: IsRecoveryInFlight,
                now: now,
                breakerUntil: BreakerUntil))
        {
            return;
        }

        var ra = content.CaptureRectArea;

        // 证据 1（廉价）：弹窗外观，避免每帧都跑 OCR
        if (!HasExceptionDialogLook(ra))
        {
            return;
        }

        // 证据 2（精确）：OCR 文案必须命中异常关键词
        var evidence = FindPopupEvidence(ra, config);
        if (evidence == null)
        {
            return;
        }

        StartRecovery(evidence, config);
    }

    private static DateTime BreakerUntil
    {
        get
        {
            var ticks = Interlocked.Read(ref _breakerUntilTicks);
            return ticks == 0 ? DateTime.MinValue : new DateTime(ticks, DateTimeKind.Local);
        }
    }

    /// <summary>
    /// 弹窗外观判定：原实现对应用「确认按钮」与「右下角点击进入」两个模板，
    /// 这里再叠加上游原生的提示框星标判定，提升召回。
    /// </summary>
    private static bool HasExceptionDialogLook(ImageRegion ra)
    {
        using (var confirm = ra.Find(ElementRecognition.Get("PopupConfirmButton", ra)))
        {
            if (confirm.IsExist())
            {
                return true;
            }
        }

        using (var exitSwitch = ra.Find(ElementRecognition.Get("PopupExitSwitch", ra)))
        {
            if (exitSwitch.IsExist())
            {
                return true;
            }
        }

        return Bv.IsInPromptDialog(ra);
    }

    /// <summary>
    /// OCR 弹窗文本区域，返回命中的异常文案（无命中返回 null）。
    /// 区域比早期实现收窄（x 0.25~0.8W、y 0.25~0.8H，约为原区域的 55% 面积），
    /// 弹窗文字都落在屏幕中部，收窄后显著降低每秒一次的 OCR 代价。
    /// </summary>
    private static string? FindPopupEvidence(ImageRegion ra, Core.Config.OtherConfig.ExceptionRecovery config)
    {
        var keywords = ExceptionRecoveryDecisions.MergeKeywords(
            GamePopupRecoveryJob.DefaultPopupTexts,
            config.PopupRecoveryExtraKeywords);

        // FindMulti 返回的是仅含坐标与文本的 Region（Region.Derive，没有自己的 Mat，
        // Region.Dispose() 是空实现），图像本身由调用方的 using var 持有，这里无需释放。
        var textRegions = ra.FindMulti(RecognitionObject.Ocr(
            ra.Width * 0.25, ra.Height * 0.25, ra.Width * 0.55, ra.Height * 0.55));

        foreach (var region in textRegions)
        {
            if (ExceptionRecoveryDecisions.MatchesAny(region.Text, keywords))
            {
                return region.Text;
            }
        }

        return null;
    }

    private void StartRecovery(string evidence, Core.Config.OtherConfig.ExceptionRecovery config)
    {
        // 任务已被用户取消（或正在停止）时不要启动恢复，避免"停止后又自己动起来"
        if (CancellationContext.Instance.IsCancellationRequested)
        {
            return;
        }

        // 判定 + 抢单飞位 + 登记取消源 + 置挂起，全部收在同一个临界区里：
        // 1) 与 ResetSession（会话收尾：取消 + 清登记 + 代次自增 + 标记已收尾）互斥，
        //    两种先后顺序都安全——要么它看得见本次的 CTS 并取消，要么本次因"会话已收尾"被拒；
        // 2) 与 TryReleaseOwnership（归还单飞位 + 解挂）互斥，
        //    不会出现"新恢复刚立起的挂起被旧流程的 finally 抹掉"。
        CancellationTokenSource cts;
        CancellationToken taskToken;
        int epoch;
        lock (RecoveryLock)
        {
            epoch = _recoverySessionEpoch;

            // TaskContext.IsInitialized 的语义正是"截图器正在运行"：会话已收尾时不要再启动恢复
            if (_sessionStopped || !TaskContext.Instance().IsInitialized || !IsEnabled
                || _recoveryOwnerEpoch == epoch)
            {
                // 会话已收尾 / 截图器未运行 / 功能已被关闭 / 本会话已有在途恢复
                return;
            }

            // 先建取消源：它可能抛（CancellationContext 并发 Dispose），
            // 放在写所有权之前，异常时不会留下"永久占位的单飞位"。
            taskToken = GetTaskCancellationToken();
            cts = CreateTimeoutTokenSource(config, taskToken);

            _recoveryOwnerEpoch = epoch;
            _recoveryCts = cts;
            Interlocked.Exchange(ref _abortRequestedEpoch, 0);

            // 立刻挂起主任务：避免"主任务在点、恢复流程也在点"
            ExceptionSuspendSignal.SuspendByRecovery();
        }

        try
        {
            Logger.LogWarning("[异常恢复] 检测到异常弹窗（证据：{Evidence}），暂停脚本并尝试自动恢复", evidence);
            NotifyToast($"检测到游戏异常弹窗（{evidence}），已暂停脚本并尝试自动恢复");
            _ = Task.Run(() => RunRecoveryAsync(cts, config, epoch, taskToken));
        }
        catch (Exception ex)
        {
            // 抢到单飞位之后的任何异常都必须回滚：否则单飞位永久占着（本会话内再也不发起恢复），
            // 而且会留下一个没人负责解除的挂起。
            Logger.LogError(ex, "[异常恢复] 无法启动恢复流程，已回滚挂起状态");
            RollbackStart(cts);
        }
    }

    /// <summary>启动失败时的回滚：摘引用、释放（归还单飞位 + 解挂）、释放取消源。</summary>
    private static void RollbackStart(CancellationTokenSource cts)
    {
        lock (RecoveryLock)
        {
            // 与 TryReleaseOwnership 同口径：身份看"本次恢复自己的取消源"，不看会话代次
            // （同一会话内连续多次恢复的代次相同 → ABA，会误伤后续那一轮）。
            // 启动成功时 _recoveryCts 与 _recoveryOwnerEpoch 是在同一临界区内一起写的，
            // 因此这两个判定可以合并成一个条件。
            if (ReferenceEquals(_recoveryCts, cts))
            {
                _recoveryCts = null;
                _recoveryOwnerEpoch = 0;
                ExceptionSuspendSignal.ResumeByRecovery();
            }
        }

        try
        {
            cts.Dispose();
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "[异常恢复] 释放取消源失败（已忽略）");
        }
    }

    /// <summary>
    /// 归还单飞位并解除恢复来源的挂起，返回"归还时是否仍属于该会话（可参与记账）"。
    ///
    /// **身份判定用"本次恢复自己的取消源"，不用会话代次。** 同一截图会话内可以连续发生多次恢复
    /// （弹窗反复出现就是本功能的目标场景），而会话代次在它们之间**不会变化**——只有
    /// <see cref="ResetSession"/> 才自增。若用代次判定"这是不是我这一次"，上一轮卡死的流程在收尾时
    /// 会认领并清掉下一轮正在进行的恢复（ABA），造成主任务与新恢复并发操作游戏。
    /// 取消源每次恢复都是新实例、且在本方法内摘除，因此它是"这一次尝试"的唯一身份
    /// （同一写法见 <see cref="StartRecoveryWatchdog"/> 与 <see cref="AbortRunningRecovery"/>）。
    ///
    /// 摘引用与归还所有权必须在同一临界区：两者分开会让并发的 <see cref="AbortRunningRecovery"/>
    /// 在中间窗口里取消一个已经结束的恢复。返回值额外要求"会话未切换"——ResetSession 故意不清 owner，
    /// 只比身份会让旧会话的结果被记到新会话头上（清掉新会话的熔断、弹出无关提示）。
    /// </summary>
    private static bool TryReleaseOwnership(int epoch, CancellationTokenSource cts)
    {
        lock (RecoveryLock)
        {
            if (!ReferenceEquals(_recoveryCts, cts))
            {
                return false;
            }

            _recoveryCts = null;
            _recoveryOwnerEpoch = 0;
            ExceptionSuspendSignal.ResumeByRecovery();

            return _recoverySessionEpoch == epoch;
        }
    }

    private async Task RunRecoveryAsync(
        CancellationTokenSource cts,
        Core.Config.OtherConfig.ExceptionRecovery config,
        int epoch,
        CancellationToken taskToken)
    {
        var succeeded = false;
        var canceled = false;
        var acted = false;

        // 统一的"这次是不是被取消"判定：有取消戳（用户关功能 / 截图器停止 / 外部超时中止）
        // 或任务确实被取消 → 取消；否则（自身 CancelAfter 超时）按失败记账，熔断才会正常工作。
        // 用 taskToken 快照而不是 CancellationContext.Instance.IsCancellationRequested：
        // 后者在 CancellationContext.Clear()（Dispose）之后会变成恒 false，快照仍保留"已取消"。
        bool IsCanceledByRequest() =>
            Volatile.Read(ref _abortRequestedEpoch) == epoch || taskToken.IsCancellationRequested;

        try
        {
            // 收尾看门狗：与任务线程的等待循环无关，保证"卡住的恢复"也能在有限时间内
            // 归还单飞位并解除挂起（否则待机模式下会静默失效到截图器重启为止）。
            // **必须在 try 之内**：它是本次恢复唯一的"卡死兜底"，它自己起不来时更不能连带
            // 把 finally 里的归还逻辑（TryReleaseOwnership / cts.Dispose）一起跳过——
            // 那会让所有权与挂起永久泄漏，而没有任何人负责兜底。
            StartRecoveryWatchdog(cts, config);

            // 恢复流程自身执行栈：其中的 Delay/Sleep 不会被自己的挂起挡住
            using var scope = ExceptionSuspendSignal.EnterRecoveryScope();

            var job = new GamePopupRecoveryJob(epoch);
            try
            {
                succeeded = await job.RunAsync(cts.Token).ConfigureAwait(false);
            }
            finally
            {
                // acted 必须在**任何**退出路径上取回来（RunAsync 抛异常时也点过游戏）：
                // 否则"点了但最终失败/超时"会被当成"没做过任何动作"而不计入熔断。
                acted = job.Acted;
            }
        }
        catch (OperationCanceledException)
        {
            canceled = IsCanceledByRequest();
            Logger.LogInformation(canceled ? "[异常恢复] 恢复流程被取消" : "[异常恢复] 恢复流程超时");
        }
        catch (NormalEndException)
        {
            // TaskControl.Delay 在取消时抛这个（不是 OperationCanceledException）：
            // 属于"用户停止脚本"的正常取消，不能计入失败
            canceled = IsCanceledByRequest();
            Logger.LogInformation("[异常恢复] 恢复流程因任务取消而终止");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[异常恢复] 恢复流程异常");
        }
        finally
        {
            // 取消 ≠ 失败：RunAsync 在取消时是"返回 false"而不是抛异常，
            // 不在这里补判就会把用户按「停止」或关闭功能算成恢复失败，
            // 三次就把功能自己熔断了（与注释声明的语义相反）。
            // 超时（CTS 的 CancelAfter 到点）不算"被取消"，仍计为失败。
            // 注意这两句必须在 Dispose 之前读 cts.IsCancellationRequested。
            if (!canceled && cts.IsCancellationRequested && IsCanceledByRequest())
            {
                canceled = true;
            }

            // 归还单飞位：内含"摘掉 _recoveryCts"与"本次尝试"的身份校验，必须在 Dispose 之前完成——
            // 摘引用之后，并发的 AbortRunningRecovery 就不会再去取消一个即将释放的源。
            // 只有本次尝试仍是所有者、且仍属于本代会话时，才连带解除恢复挂起并允许这次结果参与记账。
            var releasedByThisAttempt = TryReleaseOwnership(epoch, cts);

            cts.Dispose();

            if (releasedByThisAttempt)
            {
                HandleRecoveryResult(succeeded, canceled, acted, config);
            }
            else
            {
                // 会话已切换 / 已被下一轮恢复接管 / 已被收尾看门狗接管：这一次的结果不该影响当前流程，
                // 尤其不能把失败计数与熔断写到别人头上，也不该弹与当前无关的提示。
                Logger.LogInformation("[异常恢复] 本次恢复流程已结束（所有权不在本流程，不影响当前状态）");
            }
        }
    }

    /// <summary>
    /// 请求中止在途恢复流程，并**有界等待**它真正结束（最多 <see cref="RecoveryJoinTimeoutMs"/> 毫秒）。
    ///
    /// 目的是让"解除恢复挂起"发生在恢复流程结束之后：否则任务会被放行去操作游戏，
    /// 而恢复流程还在点击（两者并发操作游戏）。这与茶包版把恢复流程放在同一个调用栈里
    /// `await` 的语义一致；只有恢复流程卡在不响应取消的同步调用里时才会走到宽限期兜底。
    /// </summary>
    /// <param name="countAsCancel">true 表示按"取消"记账（用户关闭功能）；false 表示按"失败"记账（超时放行）。</param>
    /// <returns>true 表示已确认没有在途恢复（或它在宽限期内结束）。</returns>
    public static bool AbortAndJoinRecovery(bool countAsCancel)
    {
        if (countAsCancel)
        {
            CancelRunningRecovery();
        }
        else
        {
            AbortRunningRecoveryForTimeout();
        }

        if (!IsRecoveryInFlight)
        {
            return true;
        }

        // "恢复流程已结束"的判据用的是所有权代次：恢复任务在收尾时（点击动作都已停止之后）
        // 通过 TryReleaseOwnership 归还所有权并解除挂起，因此所有权归零 == 它不会再操作游戏。
        var deadline = Environment.TickCount64 + RecoveryJoinTimeoutMs;
        while (IsRecoveryInFlight && Environment.TickCount64 < deadline)
        {
            Thread.Sleep(50);
        }

        return !IsRecoveryInFlight;
    }

    /// <summary>
    /// 取消在途的恢复流程（"取消"语义，不计入失败）。
    /// 恢复流程会点击游戏、必要时还会重登，截图器停止或用户关闭本功能后必须让它停下，
    /// 否则会与主任务并发操作游戏。
    /// </summary>
    public static void CancelRunningRecovery() => AbortRunningRecovery(stampAsCancel: true);

    /// <summary>
    /// 因"超时兜底放行"中止在途恢复流程：**不算取消**，仍按恢复失败记账。
    /// 自身超时与外部超时放行都说明这次恢复没能按时完成，不该被当作"用户取消"放过——
    /// 否则每次卡住都按取消结账，熔断永远不触发。
    /// </summary>
    /// <param name="expected">
    /// 调用方**校验过**的那一个取消源。收尾看门狗是先判身份、后取消的两步操作，
    /// 中间这一轮可能已正常收尾而下一轮已经启动——传了它才只取消自己那一个，
    /// 否则会把下一轮的取消源一起取消掉，并让它按"超时失败"误记一次失败。
    /// 传 null（如 <see cref="AbortAndJoinRecovery"/> 的调用）表示"取消当前在途的那一个"。
    /// </param>
    public static void AbortRunningRecoveryForTimeout(CancellationTokenSource? expected = null)
        => AbortRunningRecovery(stampAsCancel: false, expected: expected);

    private static void AbortRunningRecovery(bool stampAsCancel, CancellationTokenSource? expected = null)
    {
        CancellationTokenSource? cts;
        lock (RecoveryLock)
        {
            cts = _recoveryCts;

            // 身份校验必须留在临界区内：判定与"取走要取消的那个引用"分开，
            // 就会在两步之间被新一轮恢复挤进来，取消到别人头上（这是 ABA 的另一种形态——
            // 上一轮绝不动下一轮，取消这一步同样适用）。
            if (expected is not null && !ReferenceEquals(cts, expected))
            {
                return;
            }

            if (stampAsCancel)
            {
                // 标记"是被要求取消的"，供记账时区分"取消"与"失败"。
                // 绑定的是**当前所有者**的代次：没有在途恢复时写入 0（无意义），
                // 因此不会把一次"空取消"记到下一次恢复头上。
                Interlocked.Exchange(ref _abortRequestedEpoch, _recoveryOwnerEpoch);
            }
        }

        try
        {
            cts?.Cancel();
        }
        catch (Exception ex)
        {
            // 尽力取消：Cancel() 可能抛 ObjectDisposedException（恰好同时收尾）
            // 或 AggregateException（某个注册回调抛异常）。这里绝不能冒泡——
            // 调用方之一是 TaskTriggerDispatcher.Stop()，其后的截图器停止与钩子反注册不能被跳过。
            Logger.LogDebug(ex, "[异常恢复] 取消在途恢复流程时出现异常（已忽略）");
        }
    }

    /// <summary>
    /// 强制解除恢复来源挂起：宽限期内恢复流程仍未退出时的兜底放行。
    /// 与 <see cref="StartRecovery"/> 的"抢所有权 + 置挂起"使用同一把锁，
    /// 因此不存在"判定与清除之间被夹进一次新挂起"的中间态。
    /// 注意：这是**刻意**接受的例外——此时确实可能与恢复流程短暂重叠，调用方会打日志。
    /// </summary>
    public static void ForceResumeRecoverySuspension()
    {
        lock (RecoveryLock)
        {
            ExceptionSuspendSignal.ResumeByRecovery();
        }
    }

    /// <summary>
    /// 会话结束（截图器停止）时的兜底交棒。
    ///
    /// 只做两件事：请求取消 + 让本代会话作废（代次自增），从而
    /// 1. 旧恢复即使卡在不可取消的同步段，也不会再阻塞新会话发起恢复
    ///    （单飞判定是"所有者代次 == 当前代次"，旧代次自然失效）；
    /// 2. 旧恢复晚些结束时的 finally 不会去动新会话的挂起标志与单飞位。
    ///
    /// 注意**不能**直接清零"所有者代次"：那会让新旧两次恢复同时被认定为本会话的在途恢复，
    /// 反而破坏单飞（两个操作者同时点击游戏）。
    /// </summary>
    public static void ResetSession()
    {
        CancellationTokenSource? cts;
        lock (RecoveryLock)
        {
            cts = _recoveryCts;
            _recoveryCts = null;

            // 视为"取消"：截图器停止不是恢复失败
            Interlocked.Exchange(ref _abortRequestedEpoch, _recoveryOwnerEpoch);

            // 让本代会话作废，并拒绝"会话已收尾之后"才开始的恢复启动请求
            _recoverySessionEpoch++;
            _sessionStopped = true;
        }

        try
        {
            cts?.Cancel();
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "[异常恢复] 取消在途恢复流程时出现异常（已忽略）");
        }

        // 会话边界：保护状态归零（与断网看门狗 StopLoop 的语义对齐）。
        // 否则累计 3 次失败后，本进程内会长期停在"每 10 分钟只允许尝试一次"。
        ResetProtectionState();
    }

    /// <summary>
    /// 构造带超时的取消源。取任务 token 时做防御：CancellationContext.Clear() 会 Dispose CTS，
    /// 恰在此刻访问 .Token 会抛 ObjectDisposedException。
    /// </summary>
    private static CancellationTokenSource CreateTimeoutTokenSource(
        Core.Config.OtherConfig.ExceptionRecovery config,
        CancellationToken taskToken)
    {
        CancellationTokenSource cts;
        try
        {
            cts = CancellationTokenSource.CreateLinkedTokenSource(taskToken);
        }
        catch (ObjectDisposedException)
        {
            // taskToken 所属的 CTS 恰好在这一瞬间被 Dispose（CancellationContext.Clear 并发）
            cts = new CancellationTokenSource();
        }

        // 超时必须覆盖自动重登（ExitAndReloginJob 最坏约 6 分钟），否则重登会被拦腰取消
        cts.CancelAfter(TimeSpan.FromMinutes(Math.Clamp(config.PopupRecoveryTimeoutMinutes, 1, 30)));
        return cts;
    }

    /// <summary>
    /// 取当前任务的取消令牌快照（<see cref="CancellationToken"/> 是结构体快照：
    /// 即使随后 <c>CancellationContext.Clear()</c> 把单例状态清掉，快照仍能反映"已被取消"）。
    /// </summary>
    private static CancellationToken GetTaskCancellationToken()
    {
        try
        {
            return CancellationContext.Instance.Cts.Token;
        }
        catch (ObjectDisposedException)
        {
            return CancellationToken.None;
        }
    }

    private static void HandleRecoveryResult(bool succeeded, bool canceled, bool acted, Core.Config.OtherConfig.ExceptionRecovery config)
    {
        // 取消优先于成功：被取消 / 被超时中止的那一次不能算成功。
        // 恢复流程在被取消后仍可能因为"弹窗证据已消失"而返回 true（见 RunAsync 的阶段二点五/阶段四），
        // 若先判 succeeded，就会把"点掉弹窗后被用户停止"记成成功，清掉熔断与冷却戳，还弹一条假成功提示。
        if (canceled)
        {
            // 用户停止/任务取消：不计入失败，也不弹熔断提示
            Logger.LogInformation("[异常恢复] 恢复流程已取消，不计入失败");
            return;
        }

        if (succeeded)
        {
            Interlocked.Exchange(ref _consecutiveFailures, 0);
            Interlocked.Exchange(ref _breakerUntilTicks, 0);
            Interlocked.Exchange(ref _noActionRounds, 0);
            Logger.LogWarning("[异常恢复] 恢复成功，脚本继续执行");
            NotifyToast("游戏异常弹窗已处理，脚本继续执行");
            return;
        }

        if (!acted)
        {
            // 整轮都没有对游戏做出任何动作（画面里没有可点的白名单按钮）：
            // 常见于"游戏正在加载/下载资源"，那不是"反复尝试失败"，不该直接消耗熔断额度。
            // 但如果一直如此（例如弹窗一直在、按钮却始终点不动），每轮都会把任务冻住约 50 秒，
            // 占空比会坍缩——因此累计到阈值后仍然熔断，并明确提示用户。
            var rounds = Interlocked.Increment(ref _noActionRounds);

            // PopupRecoveryMaxAttempts = 0 表示"不熔断"（用户明确选择），此时这条兜底也要让位
            var noActionThreshold = config.PopupRecoveryMaxAttempts > 0
                ? Math.Max(3, Math.Clamp(config.PopupRecoveryMaxAttempts, 0, 100) * 2)
                : 0;

            if (noActionThreshold > 0 && rounds >= noActionThreshold)
            {
                var noActionCooldown = Math.Clamp(config.PopupRecoveryCooldownMinutes, 1, 24 * 60);
                Interlocked.Exchange(ref _breakerUntilTicks, DateTime.Now.AddMinutes(noActionCooldown).Ticks);
                Interlocked.Exchange(ref _noActionRounds, 0);
                Logger.LogError("[异常恢复] 连续 {Count} 轮都没能对游戏做出任何动作（画面中是否始终存在处理不了的弹窗？），进入 {Cooldown} 分钟冷却",
                    rounds, noActionCooldown);
                NotifyToast($"游戏异常恢复连续 {rounds} 轮无法处理，已暂停自动处理 {noActionCooldown} 分钟，请检查游戏状态");
                return;
            }

            if (noActionThreshold > 0)
            {
                Logger.LogWarning("[异常恢复] 本次未能对游戏做出任何动作（画面中无可点按钮，可能仍在加载），不计入失败（{Count}/{Threshold}）",
                    rounds, noActionThreshold);
            }
            else
            {
                Logger.LogWarning("[异常恢复] 本次未能对游戏做出任何动作（画面中无可点按钮，可能仍在加载），不计入失败");
            }

            return;
        }

        // 有过动作 = "连续无动作"的连击中断
        Interlocked.Exchange(ref _noActionRounds, 0);

        // 冷却已到期 = 新一轮：失败计数从头开始。
        // 否则"冷却结束 → 放行一次 → 又失败"会立刻再次熔断，功能退化成
        // "每 10 分钟只允许尝试一次"，用户再也回不到正常状态。
        var breakerUntil = BreakerUntil;
        if (breakerUntil != DateTime.MinValue && DateTime.Now >= breakerUntil)
        {
            Interlocked.Exchange(ref _consecutiveFailures, 0);
        }

        var failures = Interlocked.Increment(ref _consecutiveFailures);

        if (ExceptionRecoveryDecisions.ShouldTripBreaker(failures, config.PopupRecoveryMaxAttempts))
        {
            var cooldown = Math.Clamp(config.PopupRecoveryCooldownMinutes, 1, 24 * 60);
            Interlocked.Exchange(ref _breakerUntilTicks, DateTime.Now.AddMinutes(cooldown).Ticks);
            Logger.LogError("[异常恢复] 连续 {Count} 次恢复失败，进入 {Cooldown} 分钟冷却，期间不再自动点击",
                failures, cooldown);
            NotifyToast($"游戏异常恢复连续失败 {failures} 次，已暂停自动处理 {cooldown} 分钟，请检查游戏状态");
            return;
        }

        Logger.LogWarning("[异常恢复] 本次恢复未成功（连续失败 {Count} 次），脚本继续执行", failures);
    }

    /// <summary>
    /// 恢复流程的"收尾看门狗"。**与任务线程的等待循环完全独立**：
    /// 实时待机（没有任务在跑）时根本不存在等待方，恢复流程一旦卡在不可取消的同步调用里
    /// （例如对已挂起的游戏窗口 <c>SendMessage</c> 还原窗口、或上游 Job 里带忙等的截图重试），
    /// 既不会归还单飞位、也不会解除挂起——调度器会把所有非常驻触发器一律跳过
    /// （自动拾取 / 自动剧情 / 钓鱼静默失效，界面上没有任何提示），
    /// 而 <c>TaskControl</c> 的超时阶梯只在有人等待时才生效。
    ///
    /// 到点后强制归还单飞位并解挂，并**按一次失败记账**：否则下一次探针又会重来一轮，
    /// 变成"每 12 分钟冻一次"的无限循环。此刻确实可能与卡住的恢复流程短暂重叠，
    /// 这与等待循环里"最终兜底强制放行"的取舍一致。
    ///
    /// **身份判定用"本次恢复自己的取消源"，不用会话代次**：同一截图会话内可以连续发生多次恢复
    /// （弹窗反复出现就是本功能的目标场景），而会话代次在它们之间不会变化。用代次会让上一轮的
    /// 看门狗误认领下一轮（ABA）——它会取消一个正在正常进行的恢复并提前放行主任务，
    /// 正是本模块要避免的"两个操作者并发操作游戏"。取消源每次恢复都是新实例，
    /// 且在各条收尾路径（TryReleaseOwnership / RollbackStart / ResetSession）上被摘除，
    /// 因此能唯一标识"这一次尝试"。
    /// </summary>
    private static void StartRecoveryWatchdog(CancellationTokenSource cts, Core.Config.OtherConfig.ExceptionRecovery config)
    {
        var deadline = TimeSpan.FromMinutes(Math.Clamp(config.PopupRecoveryTimeoutMinutes, 1, 30)) + RecoveryWatchdogMargin;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(deadline).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 延迟失败也要继续往下做校验
            }

            // 已正常收尾 / 已被新会话接管 / 已被下一轮恢复取代 → 什么都不用做。
            // 判定与下面的"强夺"各自在同一临界区内完成：只在锁外读一次，会在判定与动作之间
            // 挤进新的一轮恢复。
            lock (RecoveryLock)
            {
                if (!ReferenceEquals(_recoveryCts, cts))
                {
                    return;
                }
            }

            Logger.LogError("[异常恢复] 恢复流程超过 {Minutes} 分钟仍未收尾，强制归还单飞位并解除挂起",
                deadline.TotalMinutes);

            // 按"超时中止"而不是"取消"处理：卡住的恢复仍要计入失败，否则熔断永远不触发。
            // **必须带上校验过的 cts**：上面那次判定已经出锁，这中间本轮可能刚好收尾、
            // 下一轮已经启动；不带身份就会取消到下一轮的取消源，并让它误记一次失败。
            AbortRunningRecoveryForTimeout(cts);

            lock (RecoveryLock)
            {
                if (!ReferenceEquals(_recoveryCts, cts))
                {
                    // 收尾与本次兜底撞车：谁先到谁负责
                    return;
                }

                _recoveryCts = null;
                _recoveryOwnerEpoch = 0;
                ExceptionSuspendSignal.ResumeByRecovery();
            }

            NotifyToast("游戏异常恢复未能按时结束，已强制恢复脚本执行");

            // acted 未知：按"确实尝试过"记账，避免每轮都重来一次
            HandleRecoveryResult(succeeded: false, canceled: false, acted: true, config);
        });
    }

    private static void NotifyToast(string message)
    {
        try
        {
            // 非阻塞投递：本方法可能在截图调度线程上被调用（该线程持有调度锁，绝不能等 UI 线程）
            UIDispatcherHelper.BeginInvoke(() => Toast.Information(message));
        }
        catch (Exception)
        {
            // 通知失败不影响主流程
        }
    }
}
