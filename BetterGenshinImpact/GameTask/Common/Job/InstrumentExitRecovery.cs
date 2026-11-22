using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Music.Service;
using BetterGenshinImpact.GameTask.Runtime;
using Fischless.GameCapture;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask.Common.Job;

internal static class InstrumentExitRecovery
{
    internal static async Task<bool> TryExitAsync(ImageRegion screen, CancellationToken ct, bool waitForWorld = true,
        bool instrumentOnly = false, InstrumentQuestRecovery? quest = null)
    {
        var runtime = TaskContext.Instance().Runtime;
        try
        {
            if (quest?.IsReentry(runtime) == true)
            {
                var afterExit = ReadScreen(screen, true, out var target,
                    durinKeyboard: DurinScoreVision.HasKeyboard(screen.SrcMat), classifyMenu: true);
                using (target)
                {
                    if (afterExit == InstrumentExitScreen.OrdinaryMenu) return false;
                    if (afterExit != InstrumentExitScreen.World)
                        throw new InstrumentExitRecoveryException("核对同一路线点退出演奏后的画面",
                            afterExit == InstrumentExitScreen.InstrumentPage, allowOrdinaryMenu: afterExit == InstrumentExitScreen.Other);
                    return !instrumentOnly;
                }
            }
            if (!waitForWorld)
            {
                var view = ReadScreen(screen, false, out var target,
                    durinKeyboard: quest != null && DurinScoreVision.HasKeyboard(screen.SrcMat));
                using (target)
                {
                    if (view is InstrumentExitScreen.InstrumentPage or InstrumentExitScreen.ExitPrompt)
                        throw new InstrumentExitRecoveryException("传送期间遇到演奏，重新取帧处理退出", retryExit: true);
                    if (view == InstrumentExitScreen.BlockedPrompt)
                        throw new InstrumentExitRecoveryException("传送期间遇到无法确认的演奏界面");
                    return !instrumentOnly && view == InstrumentExitScreen.World;
                }
            }
            return await TryExitOnceAsync(screen, runtime, ct, instrumentOnly, quest);
        }
        catch (InstrumentExitRecoveryException e) when (waitForWorld)
        {
            if (e.AllowOrdinaryMenu)
                return await WaitForWorldOrMenuAsync(runtime, e.Message, ct, quest);
            await WaitForWorldAsync(runtime, e.Message, ct, playDurin: e.AllowPerformance && quest != null,
                reentry: e.AllowPerformance ? null : quest);
            if (e.ExitClicked) quest?.RecordExit(runtime!);
            return true;
        }
    }

    private static async Task<bool> TryExitOnceAsync(ImageRegion screen, GameRuntime? runtime, CancellationToken ct,
        bool instrumentOnly, InstrumentQuestRecovery? quest)
    {
        var flow = new InstrumentExitFlow();
        var hasPreviousWorld = false;
        bool clicked = false;
        IDisposable? pausedTriggers = null;
        try
        {
            var result = ReadExitAction(flow, screen, ct, ref hasPreviousWorld, null, quest != null);
            if (!flow.Started)
                return !instrumentOnly && result == InstrumentExitAction.Complete;

            pausedTriggers = TaskTriggerDispatcher.Instance().PauseForInference();
            EnsureTargetWindow(runtime);
            await ReleasePressedKeysAsync(runtime!, ct);
            var viewport = runtime!.Window.Viewport.ScreenRect;
            var releasedAt = Stopwatch.GetElapsedTime(0);
            using (var frame = await CaptureAfterReleaseAsync(runtime!.Capture, releasedAt, ct))
            {
                ClickVerifiedButton(result, frame, viewport, runtime, ct, quest != null);
                clicked = true;
            }

            // WGC 可能重复返回缓存帧，确认大世界时只计入不同画面。
            using var previousWorld = new Mat();
            for (var i = 0; i < 12; i++)
            {
                await Task.Delay(400, ct);
                EnsureTargetWindow(runtime);
                var image = TaskControl.CaptureGameImageNoRetry(runtime?.Capture);
                if (image == null)
                {
                    flow.Observe(InstrumentExitScreen.Other);
                    hasPreviousWorld = false;
                    continue;
                }

                using var current = new CaptureContent(image, 0, 0);
                result = ReadExitAction(flow, current.CaptureRectArea, ct, ref hasPreviousWorld, previousWorld, quest != null);
                if (result is InstrumentExitAction.Close or InstrumentExitAction.Confirm)
                {
                    var beforeCapture = runtime.Window.Viewport.ScreenRect;
                    using var fresh = await CaptureNewerThanAsync(runtime.Capture, Stopwatch.GetElapsedTime(0), ct);
                    ClickVerifiedButton(result, fresh, beforeCapture, runtime, ct, quest != null);
                }
                if (result == InstrumentExitAction.Complete)
                {
                    ct.ThrowIfCancellationRequested();
                    EnsureTargetWindow(runtime);
                    quest?.RecordExit(runtime!);
                    return true;
                }
            }

            throw new InstrumentExitRecoveryException("退出演奏后未确认回到大世界，已暂停当前任务", true);
        }
        catch (InstrumentExitRecoveryException e) when (clicked)
        {
            e.ExitClicked = true;
            throw;
        }
        catch (Exception e) when (flow.Started && e is not (NormalEndException or OperationCanceledException))
        {
            TaskControl.Logger.LogDebug(e, "退出演奏时截图、识别或输入失败");
            throw new InstrumentExitRecoveryException("退出演奏时无法继续验证当前界面，已暂停当前任务")
                { ExitClicked = clicked };
        }
        finally
        {
            pausedTriggers?.Dispose();
        }
    }

    internal static Task WaitForWorldAsync(GameRuntime? runtime, string reason, CancellationToken ct,
        Func<ImageRegion, bool>? additionalCheck = null, bool playDurin = false, InstrumentQuestRecovery? reentry = null)
        => WaitForRecoveryAsync(runtime, reason, ct, additionalCheck, playDurin, reentry, false);

    internal static Task<bool> WaitForWorldOrMenuAsync(GameRuntime? runtime, string reason, CancellationToken ct,
        InstrumentQuestRecovery? reentry)
        => WaitForRecoveryAsync(runtime, reason, ct, null, false, reentry, true);

    internal static Task WaitForMapInputAsync(GameRuntime? runtime, string reason, CancellationToken ct,
        InstrumentQuestRecovery? reentry)
        => WaitForRecoveryAsync(runtime, reason, ct, null, false, reentry, true, true);

    private static async Task<bool> WaitForRecoveryAsync(GameRuntime? runtime, string reason, CancellationToken ct,
        Func<ImageRegion, bool>? additionalCheck, bool playDurin, InstrumentQuestRecovery? reentry, bool allowOrdinaryMenu,
        bool retryInitialInstrument = false)
    {
        EnsureSameRuntime(runtime, ct);
        using var pausedTriggers = TaskTriggerDispatcher.Instance().PauseForInference();
        await ReleasePressedKeysAsync(runtime!, ct);
        // Win32 SendInput 在调用返回前投递。Web SDK 是异步队列，无法取得实际发键时刻。
        bool synchronousKeyboard = runtime!.Kind == GameRuntimeKind.Win32Window;
        playDurin &= synchronousKeyboard;
        if (!synchronousKeyboard) reentry = null;
        TaskControl.Logger.LogWarning("{Reason}，保留当前步骤，确认回到大世界后继续", reason);
        var session = new InstrumentRecoverySession(playDurin);
        bool awaitingFirstPage = playDurin;
        using var previous = new Mat();
        var rings = playDurin ? new InstrumentRingTracker() : null;
        DurinScoreVision? score = null;
        bool sampling = false;
        using var note = new HeldInstrumentNote(runtime!.Input.Foreground.Keyboard);
        var viewport = runtime.Window.Viewport.ScreenRect;
        var acceptAfter = Stopwatch.GetElapsedTime(0);
        while (true)
        {
            await Task.Delay(sampling || note.Held ? 12 : 400, ct);
            EnsureSameRuntime(runtime, ct);
            var window = runtime.Window;
            try
            {
                note.ReleaseIfDue(session.CanPlay && session.Playback!.AwaitingHit, Stopwatch.GetElapsedTime(0));
                if (viewport != window.Viewport.ScreenRect)
                {
                    viewport = window.Viewport.ScreenRect;
                    acceptAfter = Stopwatch.GetElapsedTime(0);
                    ResetObservation();
                }
                var captureStart = Stopwatch.GetElapsedTime(0);
                var frame = CanObserve(window) ? runtime.Capture.Capture() : null;
                if (frame == null)
                {
                    ResetObservation();
                    continue;
                }
                if (!CaptureFitsViewport(frame, viewport) || viewport != window.Viewport.ScreenRect)
                {
                    frame.Dispose();
                    ResetObservation();
                    continue;
                }
                using var current = new CaptureContent(frame.Frame, 0, 0);
                // WGC 会在 KeyDown 之后交付此前缓存的画面。同步截图后端按采样起点判断。
                var capturedAt = frame.SystemRelativeTime ?? captureStart;
                if (capturedAt <= acceptAfter || !session.CanObserveCapture(capturedAt)) continue;
                var region = current.CaptureRectArea;
                bool samePointReopened = reentry?.IsReentry(runtime) == true;
                bool canRetryMap = retryInitialInstrument && session.Playback == null && !samePointReopened;
                Region? target;
                var view = canRetryMap
                    ? ReadScreen(region, true, out target, durinKeyboard: DurinScoreVision.HasKeyboard(region.SrcMat),
                        classifyMenu: true, classifyWelkin: true)
                    : ReadRecoveryScreen(region, session.CanPlay || samePointReopened, out target, classifyWelkin: true);
                using (target)
                {
                    // 保留凌晨的月卡领取。每张新画面都重新识别，奖励页切走后不再点击。
                    if (view == InstrumentExitScreen.WelkinReward && !note.Held)
                    {
                        ResetObservation();
                        ct.ThrowIfCancellationRequested();
                        EnsureTargetWindow(runtime);
                        EnsureCaptureGeometry(region, window);
                        GameCaptureRegion.GameRegion1080PPosClick(100, 100);
                        acceptAfter = Stopwatch.GetElapsedTime(0) + TimeSpan.FromMilliseconds(60);
                        continue;
                    }
                    if (canRetryMap && view is InstrumentExitScreen.OrdinaryMenu or InstrumentExitScreen.InstrumentPage or InstrumentExitScreen.ExitPrompt)
                        return false;
                    if (allowOrdinaryMenu && session.Playback == null && view == InstrumentExitScreen.Other
                        && IsKnownOrdinaryMenu(region)) return false;
                    if (view == InstrumentExitScreen.World && additionalCheck != null && !additionalCheck(region))
                        view = InstrumentExitScreen.Other;
                    if (view == InstrumentExitScreen.InstrumentPage && region is not GameCaptureRegion { Width: 1920, Height: 1080 })
                        view = InstrumentExitScreen.Other;
                    if (view == InstrumentExitScreen.InstrumentPage && samePointReopened)
                    {
                        // 重新定位也可能恰好遇到同一点的第二次演奏。
                        session = new InstrumentRecoverySession(true);
                        rings = new InstrumentRingTracker();
                        reentry = null;
                    }
                    if (view == InstrumentExitScreen.InstrumentPage) awaitingFirstPage = false;
                    if (awaitingFirstPage && view is InstrumentExitScreen.BlockedPrompt or InstrumentExitScreen.ExitPrompt)
                        view = InstrumentExitScreen.Other;

                    var same = !previous.Empty() && IsSameFrame(previous, region.SrcMat);
                    if (!same) region.SrcMat.CopyTo(previous);
                    ScoreReading reading = default;
                    uint cues = 0;
                    sampling = view == InstrumentExitScreen.InstrumentPage && session.CanPlay;
                    int sheet = session.Playback?.Sheet ?? 0;
                    if (sampling && !same)
                    {
                        score ??= new DurinScoreVision();
                        reading = score.Read(region.SrcMat, sheet, session.Playback!.NextNote);
                        cues = rings!.Observe(region.SrcMat);
                    }
                    else if (!sampling)
                    {
                        rings?.Reset();
                    }

                    bool world = session.ObserveCapturedFrame(capturedAt, view, same, reading, cues, key =>
                    {
                        ct.ThrowIfCancellationRequested();
                        EnsureTargetWindow(runtime);
                        EnsureCaptureGeometry(region, window);
                        note.KeyDown((User32.VK)key);
                        session.RecordKeyDown(note.PressedAt);
                    }, key => !note.Held && CanDispatchOnFrame(region, cues, score!,
                        session.Playback!.Sheet, session.Playback.NextNote, key));
                    note.ReleaseIfDue(session.CanPlay && session.Playback!.AwaitingHit, Stopwatch.GetElapsedTime(0));
                    if (world)
                    {
                        EnsureSameRuntime(runtime, ct);
                        if (CanObserve(window) && !note.Held) return true;
                        ResetObservation();
                    }

                    if (sampling && !session.CanPlay)
                        TaskControl.Logger.LogInformation("演奏{Status}，等待回到大世界后继续原步骤",
                            session.Playback!.Completed ? "完成" : "已停止");
                    sampling &= session.CanPlay;
                    if (session.Playback?.Sheet != sheet) rings?.Reset();
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                EnsureSameRuntime(runtime, ct);
                session.Playback?.Stop();
                ResetObservation();
                TaskControl.Logger.LogDebug(e, "恢复期间无法读取画面或发送琴键，等待回到大世界");
            }
        }

        void ResetObservation()
        {
            session.Observe(InstrumentExitScreen.Other, false, default, 0, _ => { });
            rings?.Reset();
            sampling = false;
        }
    }

    internal static bool CanDispatchOnFrame(ImageRegion latest, uint rings, DurinScoreVision score,
        int sheet, int slot, char key)
    {
        if (latest.Width != 1920 || latest.Height != 1080 || !DurinScoreVision.HasKeyboard(latest.SrcMat)) return false;
        var view = ReadScreen(latest, true, out var target, durinKeyboard: true);
        using (target)
        {
            if (view != InstrumentExitScreen.InstrumentPage) return false;
        }
        var reading = score.Read(latest.SrcMat, sheet, slot);
        return reading.RemainingScore && reading.Red == 0 && reading.PressedKeys == 0
            && (reading.Hits >> slot) == 0 && rings == (1u << DurinPlayback.KeyLayout.IndexOf(key));
    }

    internal static async Task ReleasePressedKeysAsync(GameRuntime runtime, CancellationToken ct)
    {
        while (true)
        {
            EnsureSameRuntime(runtime, ct);
            bool foreground = runtime.Input.Foreground.TryReleasePressedKeys();
            bool background = ReferenceEquals(runtime.Input.Foreground, runtime.Input.Background)
                || runtime.Input.Background.TryReleasePressedKeys();
            if (foreground && background) return;
            await Task.Delay(100, ct);
        }
    }

    // 收缩光圈只持续数帧。每帧核对琴键和 X，实际发键前再执行完整的弹窗排除。
    internal static InstrumentExitScreen ReadRecoveryScreen(ImageRegion screen, bool canPlay, out Region? target,
        bool classifyWelkin = false)
    {
        if (canPlay && DurinScoreVision.HasKeyboard(screen.SrcMat))
        {
            using var close = screen.Find(ElementRecognition.Get("PageCloseWhite", screen));
            if (close.IsExist())
            {
                target = null;
                return InstrumentExitScreen.InstrumentPage;
            }
        }
        return ReadScreen(screen, true, out target, classifyWelkin: classifyWelkin);
    }

    private static bool CanObserve(IGameWindow window) =>
        !RunnerContext.Instance.IsSuspend && !window.IsMinimized
        && (!window.RequiresForeground || window.IsForeground)
        && window.Viewport.ScreenRect == TaskContext.Instance().SystemInfo.CaptureAreaRect;

    private static void EnsureSameRuntime(GameRuntime? runtime, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (runtime == null || !ReferenceEquals(TaskContext.Instance().Runtime, runtime) || !runtime.Window.IsAlive)
            throw new InstrumentExitRecoveryException("原游戏窗口已失效，无法恢复当前步骤");
    }

    private static bool IsSameFrame(Mat previous, Mat current)
        => previous.Size() == current.Size() && previous.Type() == current.Type()
           && Cv2.Norm(previous, current, NormTypes.L1) == 0;

    private static InstrumentExitAction ReadExitAction(InstrumentExitFlow flow, ImageRegion screen,
        CancellationToken ct, ref bool hasPreviousWorld, Mat? previousWorld, bool declaredQuest)
    {
        ct.ThrowIfCancellationRequested();
        var view = ReadScreen(screen, flow.Started, out var target, durinKeyboard:
            declaredQuest && DurinScoreVision.HasKeyboard(screen.SrcMat));
        using (target)
        {
            bool sameWorldFrame = false;
            if (previousWorld != null)
            {
                if (view == InstrumentExitScreen.World)
                {
                    var current = screen.SrcMat;
                    sameWorldFrame = hasPreviousWorld && IsSameFrame(previousWorld, current);
                    if (!sameWorldFrame)
                        current.CopyTo(previousWorld);
                }

                hasPreviousWorld = view == InstrumentExitScreen.World;
            }

            var action = flow.Observe(view, sameWorldFrame);
            if (action == InstrumentExitAction.Stop)
            {
                throw new InstrumentExitRecoveryException(view is InstrumentExitScreen.InstrumentPage or InstrumentExitScreen.ExitPrompt
                    ? "退出之后再次进入演奏界面，已暂停当前任务"
                    : "未能确认演奏页的关闭按钮或退出演奏弹窗，已暂停当前任务",
                    flow.Started && view == InstrumentExitScreen.InstrumentPage);
            }

            return action;
        }
    }

    // WGC 可能连续交付松键前的画面。同步后端使用实际开始采样的时刻。
    internal static async Task<GameCaptureFrame?> CaptureAfterReleaseAsync(IGameCapture capture,
        TimeSpan releasedAt, CancellationToken ct)
    {
        var earliest = releasedAt + TimeSpan.FromMilliseconds(60);
        await Task.Delay(80, ct);
        return await CaptureNewerThanAsync(capture, earliest - TimeSpan.FromTicks(1), ct);
    }

    private static async Task<GameCaptureFrame?> CaptureNewerThanAsync(IGameCapture capture, TimeSpan previous, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var captureStart = Stopwatch.GetElapsedTime(0);
            var frame = capture.Capture();
            if (frame != null && (frame.SystemRelativeTime ?? captureStart) > previous) return frame;
            frame?.Dispose();
            if (attempt < 9) await Task.Delay(35, ct);
        }
        return null;
    }

    internal static bool CaptureFitsViewport(GameCaptureFrame frame, RECT viewport) =>
        frame.Frame.Cols == viewport.Width && frame.Frame.Rows == viewport.Height
        && (frame.CaptureRect is not { } bounds || bounds.Width == viewport.Width && bounds.Height == viewport.Height);

    private static void ClickVerifiedButton(InstrumentExitAction action, GameCaptureFrame? frame, RECT viewport,
        GameRuntime runtime, CancellationToken ct, bool declaredQuest)
    {
        ct.ThrowIfCancellationRequested();
        EnsureTargetWindow(runtime);
        if (frame == null) throw new InstrumentExitRecoveryException("操作前未能取得新的演奏画面");
        // WGC 捕获的是窗口相对像素。仅平移时源矩形的原点可能沿用启动位置。
        if (viewport != runtime.Window.Viewport.ScreenRect || !CaptureFitsViewport(frame, viewport))
            throw new InstrumentExitRecoveryException("截图后游戏窗口位置或大小已改变，等待画面恢复");
        // CaptureContent 接管 Mat。独立的 Mat 头与外层捕获帧各自管理自己的引用。
        using var fresh = new CaptureContent(new Mat(frame.Frame, new Rect(0, 0, frame.Frame.Cols, frame.Frame.Rows)), 0, 0);
        var latest = fresh.CaptureRectArea;
        var view = ReadScreen(latest, true, out var target,
            durinKeyboard: declaredQuest && DurinScoreVision.HasKeyboard(latest.SrcMat));
        using (target)
        {
            var expected = action == InstrumentExitAction.Close ? InstrumentExitScreen.InstrumentPage : InstrumentExitScreen.ExitPrompt;
            if (view != expected || target == null)
                throw new InstrumentExitRecoveryException("操作前演奏界面已改变，等待重新确认");
            ct.ThrowIfCancellationRequested();
            EnsureTargetWindow(runtime);
            EnsureCaptureGeometry(latest, runtime.Window);
            target.Click();
        }
    }

    private static void EnsureCaptureGeometry(ImageRegion screen, IGameWindow window)
    {
        Region? capture = screen;
        while (capture is not null and not GameCaptureRegion)
            capture = capture.Prev;

        var viewport = window.Viewport.ScreenRect;
        if (capture == null || capture.X != viewport.X || capture.Y != viewport.Y
                            || capture.Width != viewport.Width || capture.Height != viewport.Height)
        {
            throw new InstrumentExitRecoveryException("截图后游戏窗口位置或大小已改变，等待画面恢复");
        }
    }

    private static void EnsureTargetWindow(GameRuntime? runtime)
    {
        if (RunnerContext.Instance.IsSuspend)
            throw new InstrumentExitRecoveryException("任务已暂停，已停止退出演奏");

        if (runtime == null || !ReferenceEquals(TaskContext.Instance().Runtime, runtime) || !CanObserve(runtime.Window))
        {
            throw new InstrumentExitRecoveryException("绑定的游戏窗口已切换或失焦，已暂停退出演奏");
        }
    }

    internal static InstrumentExitScreen ReadScreen(ImageRegion screen, bool exiting, out Region? target,
        Func<Mat, string>? readPrompt = null, bool durinKeyboard = false, bool classifyMenu = false,
        bool classifyWelkin = false)
    {
        target = null;
        using var confirm = FindDialogButton(screen, confirm: true);
        using var ornament = screen.Find(ElementRecognition.Get("PromptDialogLeftBottomStar", screen));
        using var cancel = confirm != null || ornament.IsExist() ? FindDialogButton(screen, confirm: false) : null;
        // 装饰星在场景中也有相似图案，需要再核对按钮才能视为弹窗。
        var hasPrompt = confirm != null && cancel != null
                        || ornament.IsExist() && (confirm != null || cancel != null);

        if (hasPrompt)
        {
            // 覆盖整个弹窗正文，不包含下方的确认和取消按钮。
            using var body = screen.DeriveCrop(screen.Width * 0.26, screen.Height * 0.33,
                screen.Width * 0.48, screen.Height * 0.29);
            var text = readPrompt is null ? OcrFactory.Paddle.Ocr(body.SrcMat) : readPrompt(body.SrcMat);
            if (IsExitPrompt(text))
            {
                if (confirm == null)
                    return InstrumentExitScreen.BlockedPrompt;

                target = screen.Derive(confirm.X, confirm.Y, confirm.Width, confirm.Height);
                return InstrumentExitScreen.ExitPrompt;
            }

            if (exiting)
                return InstrumentExitScreen.BlockedPrompt;
        }
        else if (exiting && (confirm != null || cancel != null || ornament.MatchScore >= 0.93))
        {
            // 弹窗正在出现或只剩一部分时，等待下一帧再判断。
            return InstrumentExitScreen.Other;
        }

        if (Bv.IsInMainUi(screen))
            return InstrumentExitScreen.World;

        if (!durinKeyboard && MusicKeyAutoDetector.TryDetect(screen.SrcMat) == null)
        {
            if (classifyMenu && !hasPrompt)
            {
                if (Bv.IsInBigMapUi(screen) || Bv.IsInPartyViewUi(screen)) return InstrumentExitScreen.OrdinaryMenu;
                using var partyList = screen.Find(ElementRecognition.Get("PartyBtnDelete", screen));
                using var paimon = screen.Find(ElementRecognition.Get("BtnExitDoor", screen));
                using var cook = screen.Find(RecognitionAssets.Get("AutoSkip", "Cook", screen.Width, screen.Height));
                if (partyList.IsExist() || paimon.IsExist() || cook.IsExist()) return InstrumentExitScreen.OrdinaryMenu;
            }
            if (classifyWelkin && BlessingOfTheWelkinMoonTask.IsClaimTime()
                && BlessingOfTheWelkinMoonTask.IsClaimScreen(screen)) return InstrumentExitScreen.WelkinReward;
            return InstrumentExitScreen.Other;
        }

        using var close = screen.Find(ElementRecognition.Get("PageCloseWhite", screen));
        if (hasPrompt || !close.IsExist())
            return InstrumentExitScreen.BlockedPrompt;

        target = screen.Derive(close.X, close.Y, close.Width, close.Height);
        return InstrumentExitScreen.InstrumentPage;
    }

    internal static bool IsExitPrompt(string text)
    {
        var normalized = string.Concat(text.Where(character => !char.IsWhiteSpace(character)));
        return normalized is "是否要退出演奏" or "是否要退出演奏？" or "是否要退出演奏?";
    }

    internal static bool IsKnownOrdinaryMenu(ImageRegion screen)
    {
        var view = ReadScreen(screen, true, out var target, classifyMenu: true);
        using (target) return view == InstrumentExitScreen.OrdinaryMenu;
    }

    internal static async Task<bool> TryCloseOrdinaryMenuAsync(GameRuntime? runtime, CancellationToken ct) =>
        await ActOnFreshMenuAsync(runtime, ct, openMap: false) == MenuAction.Closed;

    internal enum MenuAction { Wait, MapAlreadyOpen, MapRequested, Closed, Instrument }

    internal static async Task<MenuAction> ActOnFreshMenuAsync(GameRuntime? runtime, CancellationToken ct, bool openMap)
    {
        EnsureSameRuntime(runtime, ct);
        EnsureTargetWindow(runtime);
        var viewport = runtime!.Window.Viewport.ScreenRect;
        using var frame = await CaptureNewerThanAsync(runtime!.Capture, Stopwatch.GetElapsedTime(0), ct);
        if (frame == null || viewport != runtime.Window.Viewport.ScreenRect || !CaptureFitsViewport(frame, viewport))
            return MenuAction.Wait;
        using var capture = new CaptureContent(frame.Frame.SubMat(new Rect(0, 0, frame.Frame.Cols, frame.Frame.Rows)), 0, 0);
        var screen = capture.CaptureRectArea;
        var view = ReadScreen(screen, true, out var target,
            durinKeyboard: openMap && DurinScoreVision.HasKeyboard(screen.SrcMat), classifyMenu: true);
        target?.Dispose();
        if (view is InstrumentExitScreen.InstrumentPage or InstrumentExitScreen.ExitPrompt) return MenuAction.Instrument;
        bool sendMap = openMap && view == InstrumentExitScreen.World;
        if (!sendMap && view != InstrumentExitScreen.OrdinaryMenu) return MenuAction.Wait;
        bool mapAlreadyOpen = openMap && !sendMap && Bv.IsInBigMapUi(screen);
        EnsureTargetWindow(runtime);
        if (viewport != runtime.Window.Viewport.ScreenRect) return MenuAction.Wait;
        EnsureCaptureGeometry(screen, runtime!.Window);
        ct.ThrowIfCancellationRequested();
        if (sendMap)
        {
            runtime.Input.Foreground.SimulateAction(GIActions.OpenMap);
            return MenuAction.MapRequested;
        }
        if (mapAlreadyOpen) return MenuAction.MapAlreadyOpen;
        runtime.Input.Foreground.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
        return MenuAction.Closed;
    }

    private static Region? FindDialogButton(ImageRegion screen, bool confirm)
    {
        using var buttons = screen.DeriveCrop(screen.Width * (confirm ? 0.50 : 0.26), screen.Height * 0.63,
            screen.Width * 0.24, screen.Height * 0.14);
        var name = confirm ? "Confirm" : "Cancel";
        using var dark = buttons.Find(ElementRecognition.Get("BtnBlack" + name, screen.Width, screen.Height));
        if (dark.IsExist())
            return screen.Derive(buttons.X + dark.X, buttons.Y + dark.Y, dark.Width, dark.Height);

        using var light = buttons.Find(ElementRecognition.Get("BtnWhite" + name, screen.Width, screen.Height));
        return light.IsExist()
            ? screen.Derive(buttons.X + light.X, buttons.Y + light.Y, light.Width, light.Height)
            : null;
    }
}

// 让上层异常清理保留这次恢复的停止结果，避免再次操作同一个演奏页。
internal sealed class InstrumentExitRecoveryException(string message, bool allowPerformance = false,
    bool retryExit = false, bool allowOrdinaryMenu = false) : NormalEndException(message)
{
    internal bool AllowOrdinaryMenu { get; } = allowOrdinaryMenu;
    internal bool ExitClicked { get; set; }
    internal bool AllowPerformance { get; } = allowPerformance;
    internal bool RetryExit { get; } = retryExit;
}
