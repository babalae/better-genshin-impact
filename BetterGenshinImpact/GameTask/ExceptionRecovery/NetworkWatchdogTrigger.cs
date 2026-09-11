using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.GameTask.ExceptionRecovery;

/// <summary>
/// 【L2】断网自动暂停触发器。
///
/// 与"把 ping 挂在截图回调里"的做法相比，关键差别是：
/// **ping 循环完全独立于截图调度器**。原因是挂起本身会阻塞任务的等待点，
/// 而截图回调又可能被触发器里的 TaskControl.Sleep 拖住——一旦 ping 依赖截图回调，
/// 就会出现"挂起后不再 ping → 永远解除不了"的循环依赖。
///
/// 现在的结构：
/// * <see cref="EnsureLoopRunning"/> 拉起一个进程级 <see cref="Timer"/>（每秒 tick，按配置间隔节流）；
/// * 循环自己检查配置：功能关闭即停止循环并解除挂起；
/// * ping 走异步（<see cref="Ping.SendPingAsync(System.Net.IPAddress,int)"/>）+ 解析结果缓存，
///   不再同步占用线程池线程，也不再把 DNS 解析算在超时之外；
/// * 主目标连续失败达阈值 → 备用目标复核 → 仍失败才挂起；
/// * 超时兜底被强制放行后进入冷却期，避免"每 30 分钟只放行几秒"。
/// </summary>
public class NetworkWatchdogTrigger : ITaskTrigger
{
    public string Name => "NetworkWatchdog";

    /// <summary>
    /// 注意 setter **故意留空**：`GameTaskManager.ConvertToTriggerList(allEnabled: true)`
    /// 会把字典里所有触发器置 true，而本开关直连用户持久化配置——若在 setter 里写配置，
    /// 跑一次自动秘境/配置组就会把用户"关闭"的选择静默改成开启并落盘。
    /// 写法参照 GameLoadingTrigger（`set {}`）；UI 直接绑定 Config.OtherConfig.*，
    /// getter 实时读配置，因此开关切换仍然即时生效。
    /// </summary>
    public bool IsEnabled
    {
        get => TaskContext.Instance().Config.OtherConfig.ExceptionRecoveryConfig.NetworkDetectionEnabled;
        set { }
    }

    /// <summary>不抢跑：网络检查与其它触发器无竞争关系。</summary>
    public int Priority => 5;

    public bool IsExclusive => false;

    /// <summary>
    /// 仍然按前台运行（不改变全局"非前台是否继续截图"的行为），
    /// 但 ping 由独立的定时循环负责，因此挂起解除不依赖截图器是否在跑。
    /// </summary>
    public bool IsBackgroundRunning => false;

    /// <summary>常驻：任务运行期间仍需工作，且是解除挂起的责任方之一。</summary>
    public bool AlwaysActive => true;

    private const string DefaultSecondaryHost = "www.qq.com";
    private const int PingTimeoutMs = 3000;

    /// <summary>超时兜底强制放行后的冷却时长：期间不再重新挂起。</summary>
    private static readonly TimeSpan ForcedReleaseCooldown = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan IpCacheLifetime = TimeSpan.FromMinutes(10);

    private static readonly ILogger Logger = App.GetLogger<NetworkWatchdogTrigger>();

    private static readonly object LoopLock = new();
    private static LoopSession? _session;
    private static DateTime _lastCheckAt = DateTime.MinValue;
    private static int _checkInFlight;
    private static int _consecutiveFailures;

    /// <summary>
    /// 一次看门狗循环会话。回调只处理"仍属于当前会话"的 tick：
    /// 旧会话的回调可能在上一次 Dispose 之后才被投递，若不校验就会把刚建好的新表关掉
    /// （循环静默消失，直到下一次 Init 才有机会恢复）。
    /// </summary>
    private sealed class LoopSession
    {
        public Timer? Timer { get; set; }
    }

    /// <summary>本次在途检查的开始时刻（ticks，0 表示空闲），用于识别"检查卡死"。</summary>
    private static long _checkStartedTicks;

    /// <summary>在途检查的代次。**单调递增且永不复用**，因此不会出现 ABA（见 StartCheckIfIdle 的 finally）。</summary>
    private static long _checkGeneration;

    /// <summary>已经提示过冷却的那枚戳记，避免冷却期内重复刷日志。</summary>
    private static long _cooldownLoggedTicks;

    /// <summary>在途检查允许的最长持续时间，超过即认为卡死并允许新检查抢占。</summary>
    private static readonly TimeSpan CheckStaleTimeout = TimeSpan.FromSeconds(60);

    /// <summary>单次网络检查（DNS + 两次 ICMP）的总超时。</summary>
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// 会话代号：每次 <see cref="StopLoop"/> 自增。在途的网络检查在真正挂起前会比对代号，
    /// 避免"会话已经清理干净了，检查才慢悠悠地把挂起标志又立起来"。
    /// </summary>
    private static int _sessionEpoch;

    /// <summary>
    /// 截图会话是否已经收尾（<see cref="StopLoop"/> 之后、下一次会话开始之前）。
    /// 与 <c>GameExceptionPopupTrigger._sessionStopped</c> 同一个作用：任务结束
    /// （<c>TaskRunner.End → LoadInitialTriggers → Init</c>）也会调用 <see cref="Init"/>，
    /// 若没有这道门槛，截图器已经停了还会被重新拉起一个每秒 tick 的 ping 循环。
    /// **只能由 <see cref="OnCaptureSessionStarted"/> 复位**（Init 也会被任务结束与 AddTrigger 调用）。
    /// </summary>
    private static volatile bool _sessionStopped;

    /// <summary>截图会话开始（由 <see cref="TaskTriggerDispatcher.Start"/> 调用）：允许拉起 ping 循环。</summary>
    public static void OnCaptureSessionStarted()
    {
        _sessionStopped = false;
    }

    /// <summary>解析结果缓存（整体一次赋值，避免 host/address/时间三字段各写各的）。</summary>
    private sealed record ResolvedAddress(string Host, IPAddress Address, DateTime ResolvedAt);

    private static volatile ResolvedAddress? _cachedAddress;

    public void Init()
    {
        _lastCheckAt = DateTime.MinValue;

        // 注意这里**不清零** _consecutiveFailures：Init 会在每次任务结束
        // （TaskRunner.End → LoadInitialTriggers）与每次 AddTrigger 时被调用，
        // 清零会让"连续失败达阈值才暂停"的判定反复从头开始（阈值永远攒不满）。
        // 计数只在网络恢复（检查成功）与会话停止（StopLoop）时清零。
        EnsureLoopRunning();
    }

    public void OnCapture(CaptureContent content)
    {
        // 真正的工作在独立定时循环里；这里只保活，避免截图器重启后循环缺失。
        EnsureLoopRunning();
    }

    /// <summary>拉起进程级 ping 循环（幂等）。</summary>
    public static void EnsureLoopRunning()
    {
        // 会话已收尾就不再拉起：任务结束时也会走到这里（Init），
        // 否则截图器已经停了还会留下一个每秒 tick 的 ping 循环。
        if (_sessionStopped)
        {
            return;
        }

        lock (LoopLock)
        {
            if (_session != null)
            {
                return;
            }

            var session = new LoopSession();
            session.Timer = new Timer(_ => OnLoopTick(session), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            _session = session;
        }
    }

    /// <summary>停止 ping 循环并解除网络挂起（截图器停止等会话边界调用）。</summary>
    public static void StopLoop()
    {
        LoopSession? session;
        lock (LoopLock)
        {
            // 代次自增放在锁内：CheckOnceAsync 在"校验代次 + 挂起"时也持有这把锁，
            // 否则清理与挂起之间存在 TOCTOU（会话已清理却立起一个没人解除的挂起）。
            Interlocked.Increment(ref _sessionEpoch);

            // 会话收尾：在 Init() 再次拉起循环之前，这里先关上"可以拉起"的门
            _sessionStopped = true;

            session = _session;
            _session = null;
        }

        session?.Timer?.Dispose();

        // 注意不要强制清零 _checkInFlight：那等于把"正在执行"的检查标记成空闲，
        // 之后任意一次检查都能与它并发跑（计数错乱、重复挂起）。
        // 在途检查会在 finally 里自己清，并被上面的会话代号挡住"事后挂起"。
        Interlocked.Exchange(ref _consecutiveFailures, 0);
        _lastCheckAt = DateTime.MinValue;
        ExceptionSuspendSignal.ResumeByNetwork();
    }

    /// <summary>
    /// 供任务等待循环调用：挂起期间由等待方主动复核连通性，
    /// 使"网络恢复后自动继续"不依赖截图器状态（每 5 秒最多一次）。
    /// </summary>
    public static void ProbeIfSuspended()
    {
        if (!ExceptionSuspendSignal.IsSuspendedByNetwork)
        {
            return;
        }

        if (DateTime.UtcNow - _lastCheckAt < TimeSpan.FromSeconds(5))
        {
            return;
        }

        _lastCheckAt = DateTime.UtcNow;
        StartCheckIfIdle();
    }

    private static void OnLoopTick(object? state)
    {
        // 代次校验：只处理"当前会话"的 tick。既防止旧回调关掉新表，
        // 也防止旧回调在新会话里继续调度检查。
        if (state is not LoopSession session)
        {
            return;
        }

        lock (LoopLock)
        {
            if (!ReferenceEquals(_session, session))
            {
                return;
            }
        }

        try
        {
            var config = TaskContext.Instance().Config.OtherConfig.ExceptionRecoveryConfig;
            if (!config.NetworkDetectionEnabled)
            {
                // 功能已关闭：先停表 + 作废本代会话，**再**解除挂起。顺序与 StopLoop 一致，不能颠倒：
                // 在途检查的"落挂起点"就在 LoopLock 里，若先解挂再作废会话，检查可以挤在两者之间
                // 把挂起立起来，而循环随后就停了、功能又是关闭状态，再没人会去解除它。
                var stopped = false;
                lock (LoopLock)
                {
                    if (ReferenceEquals(_session, session))
                    {
                        Interlocked.Increment(ref _sessionEpoch);
                        _session = null;
                        session.Timer?.Dispose();
                        stopped = true;
                    }
                }

                if (stopped)
                {
                    Interlocked.Exchange(ref _consecutiveFailures, 0);
                    _lastCheckAt = DateTime.MinValue;

                    // 解挂必须无条件下发（作废会话之前立起来的挂起也要清掉），
                    // 日志才按"确实处于挂起"来判断，避免误报。
                    var wasSuspended = ExceptionSuspendSignal.IsSuspendedByNetwork;
                    ExceptionSuspendSignal.ResumeByNetwork();
                    if (wasSuspended)
                    {
                        Logger.LogInformation("[断网看门狗] 检测已关闭，已解除网络挂起");
                    }
                }

                return;
            }

            // 按来源的超时兜底必须**脱离任务线程**存在：实时待机（没有任务在跑、也没有 JS 脚本在 sleep）时，
            // TaskControl 里那套释放阶梯根本不会被执行——挂起会一直留着，而挂起期间调度器会跳过所有
            // 非常驻触发器（自动拾取/自动剧情/钓鱼静默失效，界面上没有任何提示）。
            // 恢复来源已有自己的收尾看门狗（GameExceptionPopupTrigger.StartRecoveryWatchdog），
            // 这里给网络来源补上对等物：由这个 1 秒循环驱动，与该 tick 的检测间隔无关。
            ReleaseNetworkSuspensionIfExpired(config);

            var interval = Math.Clamp(config.NetworkDetectionInterval, 1, 600);
            if (DateTime.UtcNow - _lastCheckAt < TimeSpan.FromSeconds(interval))
            {
                return;
            }

            _lastCheckAt = DateTime.UtcNow;
            StartCheckIfIdle();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[断网看门狗] 检查调度异常");
        }
    }

    private static void StartCheckIfIdle()
    {
        var nowTicks = DateTime.UtcNow.Ticks;

        if (Interlocked.CompareExchange(ref _checkInFlight, 1, 0) != 0)
        {
            // 单飞；但如果上一次检查久到不正常（DNS/ping 卡住），允许抢占，
            // 否则 _checkInFlight 会永远停在 1，之后再也不做任何网络检查。
            // 注意 startedTicks == 0 表示"刚置位、还没写上开始时刻"，不能当作卡死
            // （否则会在正常路径上误报"超过 60 秒未结束"并多起一个检查）。
            var startedTicks = Interlocked.Read(ref _checkStartedTicks);
            if (startedTicks == 0 || (nowTicks - startedTicks) < CheckStaleTimeout.Ticks)
            {
                return;
            }

            Interlocked.Exchange(ref _checkInFlight, 1);
            Logger.LogWarning("[断网看门狗] 上一次网络检查已持续超过 {Seconds} 秒，强制开始新的检查",
                CheckStaleTimeout.TotalSeconds);
        }

        var generation = Interlocked.Increment(ref _checkGeneration);
        Interlocked.Exchange(ref _checkStartedTicks, nowTicks);

        _ = Task.Run(async () =>
        {
            // 单次检查的总超时：DNS 或 ICMP 卡住时也必须有限返回，
            // 不能依赖"60 秒后被下一次检查抢占"来兜底。
            using var checkCts = new CancellationTokenSource(CheckTimeout);
            try
            {
                await CheckOnceAsync(generation, checkCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 本次检查超时：不计入失败（下一次检查会重新判断），但要清掉单飞状态
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[断网看门狗] 网络检查异常");
            }
            finally
            {
                // 只有"仍是最新一次检查"才允许清理单飞状态。
                // 注意 _checkGeneration 是**单调递增且不复用**的：若像之前那样把代次重置为 0，
                // 后续自增会重新产生 1、2…，被抢占的旧检查就会拿到与新检查相同的代次（ABA），
                // 从而误清新检查的 _checkInFlight/_checkStartedTicks。
                if (Interlocked.Read(ref _checkGeneration) == generation)
                {
                    Interlocked.Exchange(ref _checkStartedTicks, 0);
                    Interlocked.Exchange(ref _checkInFlight, 0);
                }
            }
        });
    }

    /// <summary>
    /// 本次检查是否仍是"最新的一次"（未被抢占、也没有更新的检查）。
    /// 旧检查只允许读，不允许改失败计数、挂起状态与恢复状态。
    /// </summary>
    private static bool IsCurrentCheck(long generation) =>
        Interlocked.Read(ref _checkGeneration) == generation;

    private static async Task CheckOnceAsync(long generation, CancellationToken ct)
    {
        var epoch = Volatile.Read(ref _sessionEpoch);

        var config = TaskContext.Instance().Config.OtherConfig.ExceptionRecoveryConfig;
        if (!config.NetworkDetectionEnabled)
        {
            return;
        }

        // 地址没填（或只有空白）时既不探测也不计数：否则用户清空「Ping 地址」后会连续失败并挂起脚本，
        // 日志还写着"网络不可用"，根因完全看不出来。
        if (NormalizeHost(config.NetworkDetectionUrl).Length == 0)
        {
            Logger.LogError("[断网看门狗] 未配置有效的 Ping 地址，已跳过本次检测（请填写域名或 IP）");
            return;
        }

        var primarySucceeded = await PingHostAsync(config.NetworkDetectionUrl, ct).ConfigureAwait(false);

        if (ExceptionRecoveryDecisions.ShouldCountPingFailure(primarySucceeded))
        {
            // 本次检查已超时（取消中）就不动任何状态：结果不可信，交给下一次检查
            if (ct.IsCancellationRequested)
            {
                return;
            }

            // 被抢占的旧检查不再累加失败计数（否则会让暂停阈值提前达到）
            if (!IsCurrentCheck(generation) || Volatile.Read(ref _sessionEpoch) != epoch)
            {
                return;
            }

            var failures = Interlocked.Increment(ref _consecutiveFailures);

            // threshold <= 0 表示"不因网络暂停脚本"（由决策函数统一定义，这里不再强制 >= 1）
            var threshold = Math.Clamp(config.NetworkDetectionFailureThreshold, 0, 600);
            if (!ExceptionRecoveryDecisions.ShouldSuspendByNetwork(failures, threshold))
            {
                return;
            }

            // 复核：主目标不可达时再问一次备用目标，避免单点误判导致整机暂停
            var secondary = string.IsNullOrWhiteSpace(config.NetworkDetectionSecondaryUrl)
                ? DefaultSecondaryHost
                : config.NetworkDetectionSecondaryUrl.Trim();

            if (await PingHostAsync(secondary, ct).ConfigureAwait(false))
            {
                Logger.LogWarning("[断网看门狗] {Primary} 不可达但 {Secondary} 可达，判定网络正常",
                    config.NetworkDetectionUrl, secondary);

                // 复核期间检查可能被抢占、会话也可能已经停止：这里必须重新校验一次，
                // 与下面"主目标成功"分支同一口径（旧检查不该清零新检查的失败计数，
                // 也不该解除新检查刚建立起来的挂起）。
                if (!IsCurrentCheck(generation) || Volatile.Read(ref _sessionEpoch) != epoch)
                {
                    return;
                }

                Interlocked.Exchange(ref _consecutiveFailures, 0);

                // 备用目标可达同样说明"网络正常"：若此前已因断网挂起，这里必须解除。
                // 否则主目标持续不可达时，每次复核都判定"网络正常"，挂起却要一直等到
                // NetworkSuspendMaxMinutes 的超时兜底才被放行（默认 30 分钟）。
                ResumeNetworkSuspensionIfNeeded();
                return;
            }

            // 复核期间本次检查可能已经超时（取消）：结果不可信，不动任何状态。
            // 与上面"主目标失败但已取消"分支同一口径，否则慢 DNS/慢 ping 会在超时后又落一个挂起。
            if (ct.IsCancellationRequested)
            {
                return;
            }

            if (ExceptionSuspendSignal.IsSuspendedByNetwork)
            {
                return;
            }

            // 超时兜底刚强制放行过 → 冷却期内不再挂起，避免"每 30 分钟只放行几秒"
            var forcedAt = ExceptionSuspendSignal.LastForcedNetworkReleaseAt;
            if (forcedAt != DateTime.MinValue && DateTime.UtcNow - forcedAt < ForcedReleaseCooldown)
            {
                // 同一枚冷却戳只提示一次（否则冷却期内每 30 秒刷一条 Warning）
                if (Interlocked.Exchange(ref _cooldownLoggedTicks, forcedAt.Ticks) != forcedAt.Ticks)
                {
                    Logger.LogWarning("[断网看门狗] 网络仍不可用，但刚被强制放行过，冷却 {Minutes} 分钟内不再暂停脚本",
                        ForcedReleaseCooldown.TotalMinutes);
                }

                return;
            }

            // "本会话仍有效 + 自己仍是最新一次检查"才允许落挂起，两件事都放在 LoopLock 里：
            // * 与 StopLoop/自停表（都在锁内自增代次）互斥，消除"会话已清理却又立起挂起"的 TOCTOU；
            // * 被抢占的旧检查不再改挂起状态，避免与新检查的判定互相覆盖。
            lock (LoopLock)
            {
                if (Volatile.Read(ref _sessionEpoch) != epoch || !IsCurrentCheck(generation))
                {
                    return;
                }

                ExceptionSuspendSignal.SuspendByNetwork();
            }

            Logger.LogWarning("[断网看门狗] 网络不可用（连续 {Count} 次失败），暂停脚本等待网络恢复", failures);
            NotifyToast("网络不可用，已暂停脚本，等待网络恢复后自动继续");
            return;
        }

        // 成功分支同样只在"仍是最新检查 + 同一会话"时才动状态：
        // 被抢占的旧检查（例如 DNS 卡了很久的第一次检查）不该清零新检查刚累计的失败计数，
        // 也不该解除新检查刚建立起来的网络挂起。
        if (!IsCurrentCheck(generation) || Volatile.Read(ref _sessionEpoch) != epoch)
        {
            return;
        }

        Interlocked.Exchange(ref _consecutiveFailures, 0);
        ResumeNetworkSuspensionIfNeeded();
    }

    /// <summary>
    /// 网络来源的按来源超时兜底（与恢复来源的收尾看门狗对等）。
    ///
    /// 由 1 秒定时循环驱动，**不依赖任务线程**：实时待机时没有人会去执行
    /// <c>TaskControl.WaitWhileExceptionSuspended</c> 里的超时阶梯，若不在此处兜底，
    /// <c>NetworkSuspendMaxMinutes</c> 在这条路径上形同虚设，挂起只能靠"ping 重新成功"
    /// 或用户手动关开关/停截图器解除。到点后强放行并留下冷却戳，避免下一次检查立刻重新挂起。
    /// </summary>
    private static void ReleaseNetworkSuspensionIfExpired(Core.Config.OtherConfig.ExceptionRecovery config)
    {
        if (!ExceptionSuspendSignal.IsSuspendedByNetwork)
        {
            return;
        }

        var limit = ExceptionRecoveryDecisions.NetworkSuspendLimit(config.NetworkSuspendMaxMinutes);

        // 复用挂起信号自己的判定（与任务线程等待循环同一口径），只认网络来源：
        // 恢复来源传 TimeSpan.Zero 表示"本次不判定它"——它有自己的收尾看门狗。
        if (ExceptionSuspendSignal.EvaluateExpiredReleases(limit, TimeSpan.Zero)
            != ExceptionSuspendSignal.SuspendRelease.Network)
        {
            return;
        }

        Logger.LogError("[断网看门狗] 网络挂起已超过上限 {Minutes} 分钟仍未恢复，强制放行脚本",
            limit.TotalMinutes);

        // forced：写冷却戳。否则下一次检查（默认 30 秒后）会立刻重新挂起，兜底等于没做。
        ExceptionSuspendSignal.ResumeByNetwork(forced: true);
        NotifyToast("网络异常持续时间过长，已恢复脚本执行");
    }

    /// <summary>
    /// 确认网络可用后解除"断网挂起"（本来就没挂起时什么都不做）。
    /// 主目标成功与备用目标复核成功两条路径共用，避免两处的日志与提示文案走样。
    /// </summary>
    private static void ResumeNetworkSuspensionIfNeeded()
    {
        if (!ExceptionSuspendSignal.IsSuspendedByNetwork)
        {
            return;
        }

        ExceptionSuspendSignal.ResumeByNetwork();
        Logger.LogWarning("[断网看门狗] 网络已恢复，脚本继续执行");
        NotifyToast("网络已恢复，脚本继续执行");
    }

    /// <summary>
    /// 异步 ping：不再同步阻塞线程池线程；主机名先解析并缓存，避免每次 ping 都做 DNS。
    /// 解析与 ICMP 都接受本次检查的取消令牌（含单次检查的总超时）。
    /// </summary>
    private static async Task<bool> PingHostAsync(string host, CancellationToken ct)
    {
        var target = NormalizeHost(host);
        if (target.Length == 0)
        {
            return false;
        }

        try
        {
            using var ping = new Ping();
            var address = await ResolveAsync(target, ct).ConfigureAwait(false);
            var reply = address != null
                ? await ping.SendPingAsync(address, TimeSpan.FromMilliseconds(PingTimeoutMs), null, default, ct)
                    .ConfigureAwait(false)
                // 解析失败时退回"直接 ping 主机名"：这条重载不接受取消令牌，但它自带超时（PingTimeoutMs）
                : await ping.SendPingAsync(target, PingTimeoutMs).ConfigureAwait(false);

            return reply.Status == IPStatus.Success;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 本次检查的总超时（不是"网络不可达"）：必须抛出去让外层按"不确定"处理，
            // 否则慢 DNS/慢 ping 会被当成探测失败，凭空累加失败计数、甚至触发断网挂起。
            throw;
        }
        catch (Exception)
        {
            // DNS 解析失败/ICMP 被拦/网络不可达一律视为"本次探测失败"，
            // 是否暂停由连续失败阈值与备用目标复核共同决定。
            return false;
        }
    }

    /// <summary>
    /// 归一化探测目标：把用户可能填的 URL（http(s)://域名:端口/路径）解析成主机名或 IP。
    /// 用 <see cref="Uri"/> 解析而不是手写前缀裁剪——手写裁剪遇到"带端口"（如 <c>example.com:443</c>）
    /// 会把端口一起丢给 DNS，导致解析必然失败。
    /// </summary>
    private static string NormalizeHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return string.Empty;
        }

        var target = host.Trim();

        // 已经是 IP（含 IPv6）就直接用
        if (IPAddress.TryParse(target, out var literal))
        {
            return literal.ToString();
        }

        // 补协议头后交给 Uri 解析：拿到 Host（自动去掉端口、路径、查询串）
        var candidate = target.Contains("://", StringComparison.Ordinal)
            ? target
            : "http://" + target;

        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
        {
            return uri.Host;
        }

        // 兜底：去掉路径与查询串后原样返回（例如 Uri 不接受的下划线主机名）
        var cut = target.IndexOfAny(['/', '?', '#']);
        return cut >= 0 ? target[..cut] : target;
    }

    private static async Task<IPAddress?> ResolveAsync(string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var literal))
        {
            return literal;
        }

        var cached = _cachedAddress;
        if (cached != null
            && string.Equals(cached.Host, host, StringComparison.OrdinalIgnoreCase)
            && DateTime.UtcNow - cached.ResolvedAt < IpCacheLifetime)
        {
            return cached.Address;
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
            var address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                          ?? addresses.FirstOrDefault();

            if (address != null)
            {
                // 整体替换：读取方要么看到旧记录、要么看到新记录，不会读到"半新半旧"的三元组
                _cachedAddress = new ResolvedAddress(host, address, DateTime.UtcNow);
            }

            return address;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 同上：本次检查超时导致的取消，不能当成"解析失败 ⇒ 网络不可达"
            throw;
        }
        catch (Exception)
        {
            _cachedAddress = null;
            return null;
        }
    }

    private static void NotifyToast(string message)
    {
        try
        {
            // 非阻塞投递：本方法可能在等待循环的调用栈上执行，绝不能同步等 UI 线程
            UIDispatcherHelper.BeginInvoke(() => Toast.Information(message));
        }
        catch (Exception)
        {
            // 通知失败不影响主流程
        }
    }
}
