using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Music.Service;
using BetterGenshinImpact.GameTask.Runtime;
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
            return await TryExitOnceAsync(screen, runtime, ct, instrumentOnly, quest);
        }
        catch (InstrumentExitRecoveryException e) when (waitForWorld)
        {
            await WaitForWorldAsync(runtime, e.Message, ct, playDurin: e.AllowPerformance && quest != null);
            return true;
        }
    }

    private static async Task<bool> TryExitOnceAsync(ImageRegion screen, GameRuntime? runtime, CancellationToken ct,
        bool instrumentOnly, InstrumentQuestRecovery? quest)
    {
        var flow = new InstrumentExitFlow();
        var hasPreviousWorld = false;
        IDisposable? pausedTriggers = null;
        try
        {
            var result = Advance(flow, screen, runtime, ct, ref hasPreviousWorld, null, action =>
            {
                if (action == InstrumentExitAction.Close && quest?.IsReentry(runtime) == true)
                    throw new InstrumentExitRecoveryException("同一路线点再次进入演奏，等待核对任务谱面", true);
                pausedTriggers = TaskTriggerDispatcher.Instance().PauseForInference();
                EnsureTargetWindow(runtime);
                runtime!.Input.ReleaseAll();
            }, quest != null);
            if (!flow.Started)
                return !instrumentOnly && result == InstrumentExitAction.Complete;

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
                result = Advance(flow, current.CaptureRectArea, runtime, ct, ref hasPreviousWorld, previousWorld,
                    null, quest != null);
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
        catch (Exception e) when (flow.Started && e is not (NormalEndException or OperationCanceledException))
        {
            TaskControl.Logger.LogDebug(e, "退出演奏时截图、识别或输入失败");
            throw new InstrumentExitRecoveryException("退出演奏时无法继续验证当前界面，已暂停当前任务");
        }
        finally
        {
            pausedTriggers?.Dispose();
        }
    }

    internal static async Task WaitForWorldAsync(GameRuntime? runtime, string reason, CancellationToken ct,
        Func<ImageRegion, bool>? additionalCheck = null, bool playDurin = false, InstrumentQuestRecovery? reentry = null)
    {
        EnsureSameRuntime(runtime, ct);
        using var pausedTriggers = TaskTriggerDispatcher.Instance().PauseForInference();
        runtime!.Input.ReleaseAll();
        TaskControl.Logger.LogWarning("{Reason}，保留当前步骤，确认回到大世界后继续", reason);
        var session = new InstrumentRecoverySession(playDurin);
        using var previous = new Mat();
        var rings = playDurin ? new InstrumentRingTracker() : null;
        DurinScoreVision? score = null;
        bool sampling = false;
        while (true)
        {
            await Task.Delay(sampling ? 12 : 400, ct);
            EnsureSameRuntime(runtime, ct);
            var window = runtime.Window;
            try
            {
                var image = CanObserve(window) ? TaskControl.CaptureGameImageNoRetry(runtime.Capture) : null;
                if (image == null)
                {
                    ResetObservation();
                    continue;
                }
                using var current = new CaptureContent(image, 0, 0);
                var region = current.CaptureRectArea;
                bool samePointReopened = reentry?.IsReentry(runtime) == true;
                var view = ReadRecoveryScreen(region, session.CanPlay || samePointReopened, out var target);
                using (target)
                {
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

                    if (await ObserveRecoveryFrame(session, region, view, same, reading, cues, runtime, ct))
                    {
                        EnsureSameRuntime(runtime, ct);
                        if (CanObserve(window)) return;
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

    private static async Task<bool> ObserveRecoveryFrame(InstrumentRecoverySession session, ImageRegion region,
        InstrumentExitScreen view, bool same, ScoreReading reading, uint cues, GameRuntime runtime, CancellationToken ct)
    {
        User32.VK? held = null;
        var keyboard = runtime.Input.Foreground.Keyboard;
        try
        {
            bool world = session.Observe(view, same, reading, cues, key =>
            {
                var verified = ReadScreen(region, true, out var close, durinKeyboard: true);
                using (close)
                {
                    if (verified != InstrumentExitScreen.InstrumentPage)
                        throw new InstrumentExitRecoveryException("演奏页已改变，已停止发键");
                }
                ct.ThrowIfCancellationRequested();
                EnsureTargetWindow(runtime);
                EnsureCaptureGeometry(region, runtime.Window);
                held = (User32.VK)key;
                keyboard.KeyDown(held.Value);
            });
            if (held.HasValue) await Task.Delay(35, ct);
            return world;
        }
        finally
        {
            if (held.HasValue) keyboard.KeyUp(held.Value);
        }
    }

    // 收缩光圈只持续数帧。每帧核对琴键和 X，实际发键前再执行完整的弹窗排除。
    internal static InstrumentExitScreen ReadRecoveryScreen(ImageRegion screen, bool canPlay, out Region? target)
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
        return ReadScreen(screen, true, out target);
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

    private static InstrumentExitAction Advance(InstrumentExitFlow flow, ImageRegion screen, GameRuntime? runtime,
        CancellationToken ct, ref bool hasPreviousWorld, Mat? previousWorld, Action<InstrumentExitAction>? beforeInput,
        bool declaredQuest = false)
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

            if (action is InstrumentExitAction.Close or InstrumentExitAction.Confirm)
            {
                ct.ThrowIfCancellationRequested();
                EnsureTargetWindow(runtime);
                beforeInput?.Invoke(action);
                ct.ThrowIfCancellationRequested();
                EnsureTargetWindow(runtime);
                EnsureCaptureGeometry(screen, runtime!.Window);
                target!.Click();
            }

            return action;
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
        Func<Mat, string>? readPrompt = null, bool durinKeyboard = false)
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
            return InstrumentExitScreen.Other;

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
internal sealed class InstrumentExitRecoveryException(string message, bool allowPerformance = false) : NormalEndException(message)
{
    internal bool AllowPerformance { get; } = allowPerformance;
}
