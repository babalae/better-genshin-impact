using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.GameTask.ExceptionRecovery;

/// <summary>
/// 【L1】游戏异常弹窗自动处理（更新通知 / 连接已断开 / 连接超时 / 无法登录服务器 这类弹窗）。
///
/// 设计要点（刻意保持最简）：
/// 1. **单线程帧驱动状态机**：状态只在截图调度线程上读写（<see cref="OnCapture"/> 由
///    <see cref="TaskTriggerDispatcher"/> 串行调用），因此不需要锁、取消源、单飞位、会话代次与看门狗。
///    每次回调最多走一小步（最多点一次；点击本身在 SendInput 层会阻塞约 100ms）后立即返回。
///    处理阶段内还按 <see cref="StageStepIntervalMs"/> 节流，避免**每一帧**都做模板匹配与 OCR。
///    ⚠️ 但要注意它只是**最小间隔**、不是单步耗时的上限：一步里最坏会有 2 次 OCR（约 0.55W×0.55H 区域）
///    + 3 次模板匹配 + 1 次点击，可能长于 500ms——所以不能声称"不会占用截图节拍"，
///    只能说"不会每帧都做重活"。
/// 2. 识别用"外观模板 + OCR 文案"双证据；点击只认白名单文案，命不中时退回确认按钮**图形模板**
///    （文案随客户端语言变化、按钮图形不随语言变化）——照抄既有做法：GameLoading 的「适龄提示」自动关闭、
///    <c>Bv.ClickConfirmButton</c>。
/// 3. **不抢焦点、不还原窗口、不选账号、不重登**：窗口不在前台或已最小化就跳过本次点击；
///    点击只认白名单按钮文案与上游进门流程用到的素材（「点击进入」「进入游戏」、适龄提示的白色确认按钮、
///    月卡/原石提示的空白处），既不选账号也不输密码。
/// 4. **两个阶段，预算是分开的**：阶段一"点掉弹窗"（<see cref="DismissBudgetMs"/>）；弹窗证据消失后
///    若游戏还没回到可玩状态，进入阶段二"把游戏推回可玩状态"（<see cref="EnterGameBudgetMs"/>），
///    按上游的进门顺序推进。这一步原先是上游 <c>GameLoadingTrigger</c> 的职责，但它不是常驻触发器：
///    任务运行期间它不在触发列表里，成功一次或超过 5 分钟还会自毁，所以任务期间必须由常驻触发器补上
///    （见 <see cref="EnterGameRecovery"/> 的类注释）。
/// 5. 预算 + 退避：点不掉时在日志与提示里明确告知需要人工处理，并逐次拉长探测间隔，
///    既不会无限点击，也不会无限刷提示。
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
        get => TaskContext.Instance().Config.OtherConfig.PopupRecoveryConfig.Enabled;
        set { }
    }

    /// <summary>低于 GameLoadingTrigger(999)：先让启动/进入游戏流程处理，避免抢动作。</summary>
    public int Priority => 900;

    public bool IsExclusive => false;

    /// <summary>需要游戏在前台（点击动作必须有焦点窗口），后台不参与。</summary>
    public bool IsBackgroundRunning => false;

    /// <summary>常驻：任务运行期间仍需工作（任务启动会清空实时触发器，见 <see cref="GameTaskManager.ClearTriggers"/>）。</summary>
    public bool AlwaysActive => true;

    private static readonly ILogger Logger = App.GetLogger<GameExceptionPopupTrigger>();

    //===== 探测与预算 =====

    /// <summary>探测间隔下限（秒）。</summary>
    private const int MinProbeIntervalSeconds = 5;

    /// <summary>探测间隔上限（秒）。</summary>
    private const int MaxProbeIntervalSeconds = 60;

    /// <summary>连续失败后的退避上限（秒）。</summary>
    private const int MaxBackoffSeconds = 300;

    /// <summary>"点掉弹窗"阶段的预算（毫秒）：这段时间里都点不掉就不再点，改为提示人工处理。</summary>
    private const long DismissBudgetMs = 30_000;

    /// <summary>两次点击之间的最小间隔（毫秒）：给游戏留出响应时间，也避免连点。</summary>
    private const long ClickIntervalMs = 1500;

    /// <summary>
    /// 处理阶段内每步之间的最小间隔（毫秒）。**阶段内必须节流**：截图调度是 50ms 一帧，
    /// 不节流就会变成每帧 2~4 次模板匹配，在自动钓鱼/快速传送这类独占场景里白抢时间片。
    /// </summary>
    private const long StageStepIntervalMs = 500;

    /// <summary>
    /// "把游戏推回可玩状态"阶段的预算（毫秒）。量级与上游一致：上游等「进入游戏」按钮出现/消失
    /// 用的就是 120 次 × 1000ms（`GameTask/Common/Job/ExitAndReloginJob.cs:81`、`:94`），
    /// 而热更后的加载画面几十秒不出现主界面是已知情形。
    /// </summary>
    private const long EnterGameBudgetMs = 120_000;

    /// <summary>
    /// 判定"已回到可玩状态"要求**连续**命中的采样次数。与"弹窗是否已消失"同样要求连续两次的理由：
    /// 单帧证据撑不起"脚本继续执行"这句提示。两次采样之间还隔着
    /// <see cref="StageStepIntervalMs"/>，是真实等待，不是背靠背采样。
    /// </summary>
    private const int PlayableConfirmSamples = 2;

    /// <summary>
    /// 阶段二两次执行之间超过这个空档（毫秒）就**重新起算预算**：说明这段时间本触发器根本没有被调度——
    /// 游戏被切到后台（<see cref="IsBackgroundRunning"/> 为 false 时调度器不调度它）、功能开关被关掉、
    /// 或者截图会话停过。那些时间不该算进"我花了多久在尝试"里。
    /// 不算的话会有两个假的失败出口：回到前台/重新打开开关的**第一帧**就带着过期时间戳，
    /// 直接报"未能在 120 秒内回到可玩界面（已点击 N 次）"——而那 120 秒根本没花出去。
    /// 单次 tick 的正常间隔是 50ms（截图节拍）、阶段内还有 500ms 节流，所以 5 秒已是很宽的空档判据。
    /// 注意它只让**时间**重新起算，<see cref="_enterGameClickCount"/> 不清零：点击次数是累计副作用，
    /// 由 <see cref="MaxEnterGameClicks"/> 单独封顶。
    /// </summary>
    private const long EnterGameGapRestartMs = 5_000;

    /// <summary>
    /// 阶段二一次最多点几下（**副作用硬上限**）。80 ≈ <see cref="EnterGameBudgetMs"/> / <see cref="ClickIntervalMs"/>。
    /// 它是"点击次数有界"的最后一道保证：即使空档判据让预算反复重新起算，点击也不会无限增长。
    /// </summary>
    private const int MaxEnterGameClicks = 80;

    /// <summary>两项证据允许的最大中心距（1080P 下的像素）：超过就认为不属于同一个弹窗，不点。</summary>
    private const double MaxEvidenceDistance = 420;

    /// <summary>允许点击的按钮文案（白名单，避免误点）。</summary>
    private static readonly string[] ClickableButtonTexts = ["确认", "确定", "点击进入", "知道了"];

    /// <summary>按钮文案没命中时依次尝试的确认按钮模板（覆盖黑白两种提示框与本功能自带的弹窗确认按钮）。</summary>
    private static readonly string[] ConfirmButtonTemplates = ["PopupConfirmButton", "BtnWhiteConfirm", "BtnBlackConfirm"];

    /// <summary>判定"这是异常弹窗"的文案；命中其中之一才允许动作。</summary>
    private static readonly string[] DefaultPopupTexts =
    [
        "连接超时",
        "连接已断开",
        "与服务器断开连接",
        "网络错误",
        "无法登录服务器",
        "更新通知",
        "更新公告",
        "游戏已更新",
        "重新连接"
    ];

    private enum Stage
    {
        /// <summary>等待探测（按间隔节流）。</summary>
        Idle,

        /// <summary>正在点掉异常弹窗。</summary>
        Dismissing,

        /// <summary>弹窗证据已消失、但游戏还没回到可玩状态：正在点「点击进入 / 进入游戏」。</summary>
        EnteringGame
    }

    //===== 运行期状态 =====
    // 这些字段的读写都发生在**截图调度线程的调用栈上**（OnCapture → OnCaptureCore → Step*），
    // 因此本类自己不额外加锁。
    // 唯一的例外是 Init()：它由截图调度线程之外调用，全仓库只有两个调用点——
    // ① GameTaskManager.ConvertToTriggerList **不会**对 AlwaysActive 触发器调用 Init（见那里的说明）；
    // ② TaskTriggerDispatcher.Start 里对常驻触发器的显式 Init。
    //    **它靠的是与 OnCapture 共用同一把 `_triggerListLocker` 互斥**（OnCapture 也在那把锁内被调用），
    //    **不是**靠"Start 发生在捕获开始之前"——切换捕获模式会 Stop → Start 连续执行，
    //    而 Stop 不 join 在飞的那一次 tick。改这条约定时两处必须一起改，
    //    否则 Init 会与 OnCaptureCore 并发写这些字段（现象是"超预算"判定读到 0 而误判失败）。
    // 计时一律用 Environment.TickCount64（单调时钟）：它是 Dismissing 唯一的出口，
    // 若用墙钟，NTP 校正或用户改时间导致回拨时"超预算"会永远不成立。

    private Stage _stage = Stage.Idle;
    private long _stageStartedAtMs;
    private long _nextClickAtMs;
    private long _nextProbeAtMs;
    private long _nextStageStepAtMs;

    /// <summary>连续失败次数（探测间隔按它退避）。</summary>
    private int _failedAttempts;

    /// <summary>
    /// 本次检测时**确认按钮模板不命中**——它既涵盖"靠提示框星标命中"的情况，
    /// 也涵盖"靠右下角「点击进入」开关命中"的情况，所以判"是否已点掉"时不能再要求该模板出现，
    /// 否则会出现"检出后因为模板本来就不命中而被立刻判成已消失"→ 根本没去点。
    /// 命名刻意不用 Star：它表达的是"没有可用的确认按钮证据"，不是"只有星标"。
    /// </summary>
    private bool _detectedWithoutConfirmButton;

    /// <summary>连续多少次采样都没看到弹窗证据（用于"是否已点掉"的两次确认）。</summary>
    private int _popupAbsentSamples;

    /// <summary>连续多少次采样都判定游戏处于可玩状态（用于阶段二的两次确认）。</summary>
    private int _playableSamples;

    /// <summary>
    /// 阶段二里**实际点下去了几下**（进入按钮或进门提示，由点击出口返回的真实结果累加，
    /// 不是"识别到几次"——窗口不在前台时出口会拒绝点击并返回 false）。
    /// 它决定阶段二超预算时怎么收尾：点过 = 有证据说明游戏确实停在进门流程里，按一次失败记账；
    /// 一下都没点过 = 只是"没看到可点的东西"，不能当失败（理由见 <see cref="StepEnteringGame"/>）。
    /// </summary>
    private int _enterGameClickCount;

    /// <summary>阶段二上一次被执行的时刻，用于空档重新起算（见 <see cref="EnterGameGapRestartMs"/>）。</summary>
    private long _lastEnterGameTickAtMs;

    public void Init()
    {
        _stage = Stage.Idle;
        _stageStartedAtMs = 0;
        _nextClickAtMs = 0;
        _nextProbeAtMs = 0;
        _nextStageStepAtMs = 0;
        _failedAttempts = 0;
        _detectedWithoutConfirmButton = false;
        _popupAbsentSamples = 0;
        _playableSamples = 0;
        _enterGameClickCount = 0;
        _lastEnterGameTickAtMs = 0;
    }

    public void OnCapture(CaptureContent content)
    {
        // 防御：触发器异常绝不能冒泡到截图调度循环（会连累同帧其它触发器）
        try
        {
            OnCaptureCore(content);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[异常弹窗处理] 探测异常，本功能可能未生效");
        }
    }

    private void OnCaptureCore(CaptureContent content)
    {
        var config = TaskContext.Instance().Config.OtherConfig.PopupRecoveryConfig;
        if (!config.Enabled)
        {
            // 功能被关闭：把进行中的流程复位，避免下次打开时接着旧状态跑
            if (_stage != Stage.Idle)
            {
                Init();
            }

            return;
        }

        var now = Environment.TickCount64;
        var ra = content.CaptureRectArea;

        if (_stage != Stage.Idle)
        {
            // 处理阶段内也要节流（见 StageStepIntervalMs）
            if (now < _nextStageStepAtMs)
            {
                return;
            }

            _nextStageStepAtMs = now + StageStepIntervalMs;

            // 按进入本次调用时的阶段分派：即使 StepDismissing 里把阶段换成了 EnteringGame，
            // 本次也只走一步（新阶段从下一次回调开始），保持"每次回调最多走一小步"。
            if (_stage == Stage.Dismissing)
            {
                StepDismissing(ra, config, now);
            }
            else if (_stage == Stage.EnteringGame)
            {
                StepEnteringGame(ra, config, now);
            }

            return;
        }

        // Idle：按间隔探测
        if (now < _nextProbeAtMs)
        {
            return;
        }

        _nextProbeAtMs = now + (NextProbeIntervalSeconds(config) * 1000L);

        // 证据 1（廉价）：弹窗外观
        if (!HasExceptionDialogLook(ra))
        {
            // 画面里没有异常弹窗 = 之前的异常场景已经过去，退避归零
            _failedAttempts = 0;
            return;
        }

        // 证据 2（精确）：OCR 文案必须命中异常关键词
        var evidence = FindPopupEvidence(ra, config);
        if (evidence == null)
        {
            return;
        }

        _detectedWithoutConfirmButton = !IsConfirmButtonPresent(ra);
        Logger.LogWarning("[异常弹窗处理] 检测到游戏异常弹窗（证据：{Evidence}），尝试自动点掉", evidence);
        _stage = Stage.Dismissing;
        _stageStartedAtMs = now;
        _nextClickAtMs = now;
        _nextStageStepAtMs = now;
        _popupAbsentSamples = 0;
    }

    /// <summary>探测间隔（秒）：连续失败后逐次翻倍退避，上限 <see cref="MaxBackoffSeconds"/>。</summary>
    private int NextProbeIntervalSeconds(Core.Config.OtherConfig.PopupRecovery config)
    {
        var baseInterval = Math.Clamp(config.ProbeIntervalSeconds, MinProbeIntervalSeconds, MaxProbeIntervalSeconds);
        if (_failedAttempts <= 0)
        {
            return baseInterval;
        }

        var boosted = baseInterval * Math.Pow(2, Math.Min(_failedAttempts, 4));
        return (int)Math.Min(boosted, MaxBackoffSeconds);
    }

    /// <summary>
    /// 阶段一：点掉异常弹窗。每次调用最多点一次，然后等下一帧。
    /// </summary>
    private void StepDismissing(ImageRegion ra, Core.Config.OtherConfig.PopupRecovery config, long now)
    {
        // 触发本次处理的那组外观证据都消失 = 弹窗已经被点掉。本阶段**只**负责把挡住画面的异常弹窗点掉：
        // 判成功一律以"弹窗证据已消失"为前提——**不能**拿"看起来在主界面"当成功门槛：
        // 弹窗是叠加在主界面上的模态框时那样会一次都不点（且每秒刷一条假成功提示）。
        if (IsPopupGone(ra, config))
        {
            // 弹窗证据消失只说明"挡住画面的东西没了"，不等于"脚本就能继续跑"：掉线、热更之后
            // 游戏常停在「点击进入 / 进入游戏」界面。所以下一步一律交给阶段二去确认并推回可玩状态，
            // 由它决定最终提示——本阶段自己不下成功结论（也就不会在这里说"脚本继续执行"）。
            Logger.LogInformation("[异常弹窗处理] 异常弹窗已点掉，开始确认游戏是否可以继续");
            _stage = Stage.EnteringGame;
            _stageStartedAtMs = now;
            _nextClickAtMs = now;
            _playableSamples = 0;
            _enterGameClickCount = 0;
            _lastEnterGameTickAtMs = now;
            return;
        }

        if (now - _stageStartedAtMs > DismissBudgetMs)
        {
            Fail("未能在限定时间内点掉异常弹窗", config, now);
            return;
        }

        if (now < _nextClickAtMs)
        {
            return;
        }

        _nextClickAtMs = now + ClickIntervalMs;
        if (!TryClickPopupButton(ra, config))
        {
            Logger.LogDebug("[异常弹窗处理] 画面中没有可点的白名单按钮，等待下一次");
        }
    }

    /// <summary>
    /// 阶段二：确认游戏能不能继续，不能就按**上游的进门顺序**把它推回可玩状态
    /// （`EnterGameRecovery.TryAdvanceEnterGame`：适龄提示 →「点击进入」→「进入游戏」→ 月卡 → 原石）。
    ///
    /// 进入条件见 <see cref="StepDismissing"/>（弹窗证据已消失）。三个出口，全部有界：
    /// * <see cref="EnterGameRecovery.IsGamePlayable"/> **连续** <see cref="PlayableConfirmSamples"/> 次命中
    ///   → 成功，这时才敢提示"脚本继续执行"；
    /// * 超过 <see cref="EnterGameBudgetMs"/>（或达到 <see cref="MaxEnterGameClicks"/> 次点击）
    ///   且期间**确实点过**进入按钮 → 按一次失败记账并退避；
    /// * 超预算但一次都没点过 → 只留 Warning 与提示，**不记账、不退避**（理由见下）。
    ///
    /// 两处刻意的取舍：
    /// * **"不在可玩界面"本身不当作失败**：它推不出"脚本跑不下去"——游戏可能只是还在加载、在过场里，
    ///   或者停在需要账号/密码的登录页。拿它当失败门槛会造出假失败（本项目已有同型教训：凭猜收紧判定
    ///   → 假熔断）。只有"确实点过进入按钮、点满预算仍回不去"才算失败。
    /// * **本阶段不重新探测新的异常弹窗**：最坏情况下新弹窗要等本阶段结束（≤120 秒）才被发现，不会丢。
    ///   写进注释是因为这是已知边界，不是遗漏。
    /// * **预算算的是"被调度的时间"，不是墙钟**：两次执行之间空档超过
    ///   <see cref="EnterGameGapRestartMs"/> 就重新起算（那段时间本触发器根本没在跑：游戏不在前台、
    ///   开关被关掉、截图会话停过）。否则回到前台/重新打开开关的第一帧就会拿着过期时间戳直接收尾——
    ///   那 120 秒其实没花出去。判断顺序上"可玩"永远先判，所以用户自己进了游戏会先命中成功分支。
    ///   另有点击次数硬上限 <see cref="MaxEnterGameClicks"/>，保证副作用有界。
    /// </summary>
    private void StepEnteringGame(ImageRegion ra, Core.Config.OtherConfig.PopupRecovery config, long now)
    {
        // 空档重新起算（见 EnterGameGapRestartMs）：点击计数不清零，它由 MaxEnterGameClicks 封顶。
        if (now - _lastEnterGameTickAtMs > EnterGameGapRestartMs)
        {
            Logger.LogInformation("[异常弹窗处理] 阶段二空档 {Seconds} 秒（本触发器未被调度），预算重新起算",
                (now - _lastEnterGameTickAtMs) / 1000);
            _stageStartedAtMs = now;
            _playableSamples = 0;
        }

        _lastEnterGameTickAtMs = now;

        if (EnterGameRecovery.IsGamePlayable(ra))
        {
            _playableSamples++;
            if (_playableSamples >= PlayableConfirmSamples)
            {
                Succeed("异常弹窗已消失，游戏已回到可玩界面",
                    "游戏异常弹窗已处理，已自动进入游戏，脚本继续执行", config, now);
            }

            return;
        }

        _playableSamples = 0;

        if (now - _stageStartedAtMs > EnterGameBudgetMs || _enterGameClickCount >= MaxEnterGameClicks)
        {
            if (_enterGameClickCount > 0)
            {
                Fail($"游戏未回到可玩界面（已尝试 {_enterGameClickCount} 次、"
                     + $"耗时 {(now - _stageStartedAtMs) / 1000} 秒），请手动处理", config, now);
            }
            else
            {
                Logger.LogWarning("[异常弹窗处理] 异常弹窗已点掉，但未识别到「点击进入 / 进入游戏」，"
                                  + "游戏也未回到可玩界面，可能需要手动进入或登录");
                NotifyToast("异常弹窗已点掉，未检测到可自动点击的进入按钮，请确认游戏状态");
                Finish(config, now);
            }

            return;
        }

        if (now < _nextClickAtMs)
        {
            return;
        }

        _nextClickAtMs = now + ClickIntervalMs;
        if (EnterGameRecovery.TryAdvanceEnterGame(ra))
        {
            _enterGameClickCount++;
        }
        else
        {
            Logger.LogDebug("[异常弹窗处理] 本轮没有点击（未识别到进入按钮或进门提示，或游戏窗口不在前台），继续等待");
        }
    }

    private void Succeed(string message, string toast, Core.Config.OtherConfig.PopupRecovery config, long now)
    {
        Logger.LogInformation("[异常弹窗处理] {Message}", message);
        NotifyToast(toast);
        Finish(config, now);
    }

    /// <summary>
    /// "本次处理到此为止、且不算失败"的公共收尾：清零失败计数、回 Idle、按当前间隔安排下一次探测。
    /// 与 <see cref="Fail"/> 的区别只在失败计数与日志级别。
    /// </summary>
    private void Finish(Core.Config.OtherConfig.PopupRecovery config, long now)
    {
        _failedAttempts = 0;
        _stage = Stage.Idle;
        _nextProbeAtMs = now + (NextProbeIntervalSeconds(config) * 1000L);
    }

    private void Fail(string reason, Core.Config.OtherConfig.PopupRecovery config, long now)
    {
        _failedAttempts++;
        var backoff = NextProbeIntervalSeconds(config);
        Logger.LogError("[异常弹窗处理] {Reason}（连续第 {Count} 次）。{Backoff} 秒内不再自动处理，请检查游戏状态",
            reason, _failedAttempts, backoff);
        NotifyToast($"游戏异常弹窗处理失败：{reason}");
        _stage = Stage.Idle;
        _nextProbeAtMs = now + (backoff * 1000L);
    }

    /// <summary>
    /// "开始处理"的外观门槛：弹窗自身的确认按钮 / 右下角的「点击进入」开关 / 上游的提示框星标，
    /// 三者任一命中即可（之后还要过 OCR 文案这一关）。
    /// </summary>
    private static bool HasExceptionDialogLook(ImageRegion ra)
    {
        return IsConfirmButtonPresent(ra) || IsEntrySwitchPresent(ra) || Bv.IsInPromptDialog(ra);
    }

    /// <summary>
    /// 弹窗是否已经被点掉。判据要与检测时**同一组证据**，而且要求**连续两次采样**都不见：
    /// * 同一组证据：避免"检出后却因模板本来就不命中而被立刻判成已消失"（那样根本不会去点）；
    /// * 连续两次：避免弹窗淡出/掉帧的单帧缺失被当成"已点掉"（误判后本轮不再点击，要等超预算与退避才可能重来）。
    /// 注意**不**把右下角的「点击进入」开关算进"弹窗仍在"：它属于进入/登录界面，
    /// 弹窗已被点掉、游戏停在进入界面时它反而会出现。
    ///
    /// **为什么还要 OCR 复核一次**：检测门槛里"确认按钮模板 / 提示框星标"并不总是可用——
    /// 弹窗只被「点击进入」开关检出时，这两项本来就不命中（见 <see cref="_detectedWithoutConfirmButton"/>），
    /// 光看它们会在点击后约 0.5 秒就判"已点掉"，早于 <see cref="ClickIntervalMs"/> 给游戏留的反应窗口。
    /// 而"命中异常文案"是**每次检测都必须满足**的证据，所以在下结论前用同一证据类复核：
    /// 文案仍在 = 弹窗仍在，继续点。
    ///
    /// 开销如实说明：复核**不在 Idle 常态探测上发生**，但**也不是"每次处理只做一次"**——
    /// 复核判定"仍在"时会把 <see cref="_popupAbsentSamples"/> 清零，于是每再攒够两次采样
    /// （约 1 秒）就会再复核一次，直到弹窗真的消失或 30 秒预算耗尽。
    /// 也就是说：弹窗迟迟点不掉时，本阶段的 OCR 频率约为每秒 1 次。
    /// </summary>
    private bool IsPopupGone(ImageRegion ra, Core.Config.OtherConfig.PopupRecovery config)
    {
        if (IsConfirmButtonPresent(ra) || (_detectedWithoutConfirmButton && Bv.IsInPromptDialog(ra)))
        {
            _popupAbsentSamples = 0;
            return false;
        }

        _popupAbsentSamples++;
        if (_popupAbsentSamples < 2)
        {
            return false;
        }

        if (FindPopupEvidence(ra, config) != null)
        {
            // 外观证据不见了、但异常文案还在：说明弹窗仍在（或正在关闭），本轮不要下成功结论。
            _popupAbsentSamples = 0;
            return false;
        }

        return true;
    }

    /// <summary>弹窗自身的确认按钮模板。</summary>
    private static bool IsConfirmButtonPresent(ImageRegion ra)
    {
        using var confirm = ra.Find(ElementRecognition.Get("PopupConfirmButton", ra));
        return confirm.IsExist();
    }

    /// <summary>右下角的「点击进入」开关：进入/登录界面的元素，说明游戏还不处于可玩状态。</summary>
    private static bool IsEntrySwitchPresent(ImageRegion ra)
    {
        using var exitSwitch = ra.Find(ElementRecognition.Get("PopupExitSwitch", ra));
        return exitSwitch.IsExist();
    }

    /// <summary>
    /// OCR 弹窗文本区域，返回命中的异常文案（无命中返回 null）。
    /// 区域取屏幕中部（x 0.25~0.8W、y 0.25~0.8H）：弹窗文字都在这里，面积比全屏小得多，降低每次探测的开销。
    /// </summary>
    private static string? FindPopupEvidence(ImageRegion ra, Core.Config.OtherConfig.PopupRecovery config)
    {
        var keywords = MergeKeywords(DefaultPopupTexts, config.ExtraKeywords);

        // FindMulti 返回的是仅含坐标与文本的 Region（没有自己的 Mat，Dispose 是空实现），
        // 图像本身由调用方持有，这里无需逐个释放。
        var textRegions = ra.FindMulti(BuildOcrRo(ra));
        foreach (var region in textRegions)
        {
            if (MatchesAny(region.Text, keywords))
            {
                return region.Text;
            }
        }

        return null;
    }

    /// <summary>
    /// 尝试点击弹窗按钮：先要求"存在异常文案"与"存在白名单按钮文案"且两者配成同一个弹窗；
    /// 白名单文案一个都没命中时（其它语言客户端的按钮文案是 "Confirm" 之类），
    /// 退回"异常文案 + 确认按钮图形模板"。
    /// </summary>
    private static bool TryClickPopupButton(ImageRegion ra, Core.Config.OtherConfig.PopupRecovery config)
    {
        var popupTexts = MergeKeywords(DefaultPopupTexts, config.ExtraKeywords);
        var textRegions = ra.FindMulti(BuildOcrRo(ra));
        if (textRegions.Count == 0)
        {
            return false;
        }

        // 证据 1：屏幕上确实存在异常弹窗文案（避免把普通确认框当异常弹窗）
        var popupText = textRegions.FirstOrDefault(r => MatchesAny(r.Text, popupTexts));
        if (popupText == null)
        {
            return false;
        }

        // 证据 2：存在可点击的按钮文案，且必须与异常文案**属于同一个弹窗**：
        // 按"离异常文案最近的按钮"配对并限制距离；只要求两者出现在同一块大 ROI 里是不够的——
        // 画面上只要有任意一个普通对话框的「确认/确定」就可能被误点。
        // 排除异常文案自身那块区域：OCR 可能把正文和按钮标签合并进同一个框。
        var button = textRegions
            .Where(r => !ReferenceEquals(r, popupText) && MatchesAny(r.Text, ClickableButtonTexts))
            .OrderBy(r => CenterDistance(r, popupText))
            .FirstOrDefault();

        if (button == null)
        {
            return TryClickConfirmButtonByTemplate(ra, popupText);
        }

        if (CenterDistance(button, popupText) > MaxEvidenceDistance)
        {
            Logger.LogWarning("[异常弹窗处理] 可点按钮离异常文案过远（{Distance:F0}px），判定不属于同一弹窗，跳过点击",
                CenterDistance(button, popupText));
            return false;
        }

        EnterGameRecovery.ClickIfGameActive(button, $"弹窗按钮：{button.Text}");
        return true;
    }

    /// <summary>
    /// 按钮文案没命中时的兜底：用确认按钮的**图形模板**点击。
    /// 依据与上游一致——「适龄提示」自动关闭（OCR 文案作证据 + BtnWhiteConfirm 模板点击）与
    /// <c>Bv.ClickConfirmButton</c> 都这么做。前提没有放松：调用方已确认画面里确有异常弹窗文案，
    /// 且模板按钮同样要过 <see cref="MaxEvidenceDistance"/> 这道"与异常文案同处一个弹窗"的判定。
    /// </summary>
    private static bool TryClickConfirmButtonByTemplate(ImageRegion ra, Region popupText)
    {
        // 搜索范围限定在画面下半部分：弹窗按钮都在那里，避免命中其它界面上的按钮。
        var lowerHalf = new Rect(0, ra.Height / 2, ra.Width, ra.Height / 2);

        foreach (var name in ConfirmButtonTemplates)
        {
            var ro = ElementRecognition.Get(name, ra).Clone();
            ro.RegionOfInterest = ro.RegionOfInterest == default
                ? lowerHalf // 通用按钮模板没有自带 ROI
                : ro.RegionOfInterest.Intersect(lowerHalf); // 自带 ROI 的与下半屏取交集

            // 交集为空要显式跳过：把空的 RegionOfInterest 交给识别器会被当成"没有 ROI"，
            // 搜索范围从下半屏静默扩大到整屏。
            if (ro.RegionOfInterest.Width <= 0 || ro.RegionOfInterest.Height <= 0)
            {
                continue;
            }

            using var button = ra.Find(ro);
            if (!button.IsExist())
            {
                continue;
            }

            if (CenterDistance(button, popupText) > MaxEvidenceDistance)
            {
                Logger.LogWarning("[异常弹窗处理] 确认按钮模板 {Template} 离异常文案过远（{Distance:F0}px），换下一个模板",
                    name, CenterDistance(button, popupText));
                continue;
            }

            EnterGameRecovery.ClickIfGameActive(button, $"确认按钮模板：{name}");
            return true;
        }

        return false;
    }

    /// <summary>OCR 弹窗文本区域：屏幕中部 x 0.25~0.8W、y 0.25~0.8H。</summary>
    private static RecognitionObject BuildOcrRo(ImageRegion ra)
    {
        return RecognitionObject.Ocr(ra.Width * 0.25, ra.Height * 0.25, ra.Width * 0.55, ra.Height * 0.55);
    }

    /// <summary>两个识别区域中心的距离。</summary>
    private static double CenterDistance(Region a, Region b)
    {
        var dx = a.X + a.Width / 2.0 - (b.X + b.Width / 2.0);
        var dy = a.Y + a.Height / 2.0 - (b.Y + b.Height / 2.0);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>文本是否命中任一关键词（不区分大小写，空关键词忽略）。</summary>
    private static bool MatchesAny(string? text, IReadOnlyList<string> keywords)
    {
        if (string.IsNullOrWhiteSpace(text) || keywords.Count == 0)
        {
            return false;
        }

        foreach (var keyword in keywords)
        {
            if (!string.IsNullOrEmpty(keyword) && text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>把基础关键词与用户追加的关键词合并成一个查找集合。</summary>
    private static IReadOnlyList<string> MergeKeywords(IReadOnlyList<string> baseKeywords, string? extraRaw)
    {
        var extra = ParseKeywords(extraRaw);
        if (extra.Count == 0)
        {
            return baseKeywords;
        }

        var merged = new List<string>(baseKeywords);
        foreach (var keyword in extra)
        {
            if (!merged.Contains(keyword))
            {
                merged.Add(keyword);
            }
        }

        return merged;
    }

    /// <summary>
    /// 把用户配置的扩展关键词（逗号/分号/竖线/换行分隔）解析为数组，自动去空与去重。
    /// 刻意不把空格当分隔符：国际服关键词本身含空格（如 "Update Notice"）。
    /// </summary>
    private static IReadOnlyList<string> ParseKeywords(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var separators = new[] { ',', '，', ';', '；', '|', '\n', '\r' };
        var result = new List<string>();
        foreach (var part in raw.Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var keyword = part.Trim();
            if (keyword.Length > 0 && !result.Contains(keyword))
            {
                result.Add(keyword);
            }
        }

        return result;
    }

    private static void NotifyToast(string message)
    {
        try
        {
            // 非阻塞投递：本方法在截图调度线程上被调用（该线程持有调度锁，绝不能等 UI 线程）
            UIDispatcherHelper.BeginInvoke(() => Toast.Information(message));
        }
        catch (Exception)
        {
            // 通知失败不影响主流程
        }
    }
}
