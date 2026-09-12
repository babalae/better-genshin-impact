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
/// 3. **不抢焦点、不还原窗口、不做重登**：窗口不在前台或已最小化就跳过本次点击，把动作降到"至多一次点击"。
/// 4. 预算 + 退避：点不掉时在日志与提示里明确告知需要人工处理，并逐次拉长探测间隔，
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
        Dismissing
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

            StepDismissing(ra, config, now);

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
        // 触发本次处理的那组外观证据都消失 = 弹窗已经被点掉。
        // 热更后的加载画面可能几十秒都不是主界面，因此这里**不要求回到主界面**、也不判失败：
        // 本阶段只负责"把挡住画面的异常弹窗点掉"，（若之后停在进入游戏界面）自动点「点击进入」
        // 属于独立的一步，见后续改动。
        // 判成功一律以"弹窗证据已消失"为前提——**不能**拿"看起来在主界面"当成功门槛：
        // 弹窗是叠加在主界面上的模态框时那样会一次都不点（且每秒刷一条假成功提示）。
        if (IsPopupGone(ra, config))
        {
            // 判据与提示**分开**：这里判成功只认"弹窗证据已消失"（理由见上面）。
            // 但提示措辞必须如实——弹窗没了不等于脚本就能继续，掉线/热更后常停在「点击进入」界面，
            // 而"自动进入游戏"是独立的一步（见后续改动）。所以 IsInMainUi 只决定**说什么**，
            // 不参与成败判定。
            var backInGame = Bv.IsInMainUi(ra);
            Succeed("异常弹窗已消失", SuccessToast(backInGame), config, now);
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

    /// <summary>成功提示的措辞：只有拿到"已回到主界面"这一证据时才敢说"脚本继续执行"。</summary>
    private static string SuccessToast(bool backInGame) => backInGame
        ? "游戏异常弹窗已处理，脚本继续执行"
        : "异常弹窗已点掉，但游戏未回到主界面，请手动进入";

    private void Succeed(string message, string toast, Core.Config.OtherConfig.PopupRecovery config, long now)
    {
        Logger.LogInformation("[异常弹窗处理] {Message}", message);
        NotifyToast(toast);
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
    /// 点击前的最后一道校验。本触发器只在游戏处于前台时才会被调度（<see cref="IsBackgroundRunning"/> 为 false），
    /// 这里再确认一次，避免"点下去时窗口刚好被切走/最小化"——SendInput 用的是绝对桌面坐标，会落到别的窗口上。
    /// 刻意**不**抢焦点、**不**还原最小化窗口：那是用户自己的桌面状态。
    /// </summary>
    private static void ClickIfGameActive(Region region, string what)
    {
        if (SystemControl.IsGenshinImpactMinimized())
        {
            Logger.LogWarning("[异常弹窗处理] 游戏窗口已最小化，跳过点击（{What}）", what);
            return;
        }

        if (!SystemControl.IsGenshinImpactActiveByProcess())
        {
            Logger.LogWarning("[异常弹窗处理] 游戏窗口不在前台，跳过点击（{What}）", what);
            return;
        }

        Logger.LogInformation("[异常弹窗处理] 点击：{What}", what);
        region.Click();
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

        ClickIfGameActive(button, $"弹窗按钮：{button.Text}");
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

            ClickIfGameActive(button, $"确认按钮模板：{name}");
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
