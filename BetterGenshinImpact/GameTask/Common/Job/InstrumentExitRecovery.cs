using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Music.Service;
using BetterGenshinImpact.GameTask.Runtime;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.Job;

internal static class InstrumentExitRecovery
{
    internal static bool IsRecoveryScreen(ImageRegion screen)
    {
        return NeedsRecovery(Classify(screen));
    }

    internal static bool NeedsRecovery(InstrumentExitScreen view) =>
        view is InstrumentExitScreen.InstrumentPage or InstrumentExitScreen.ExitPrompt or InstrumentExitScreen.BlockedPrompt;

    internal static InstrumentExitScreen Classify(ImageRegion screen)
    {
        var view = ReadScreen(screen, false, out var target);
        using (target)
            return view;
    }

    internal static async Task<bool> TryExitAsync(ImageRegion screen, CancellationToken ct)
    {
        if (!IsRecoveryScreen(screen)) return false;
        await RecoverAsync(TaskContext.Instance().Runtime, ct);
        return true;
    }

    internal static async Task RecoverAsync(GameRuntime? runtime, CancellationToken ct,
        Func<ImageRegion, bool>? canResumeInWorld = null, Action<ImageRegion>? observeTransition = null)
    {
        if (runtime == null) throw new OperationCanceledException("游戏窗口尚未绑定", ct);
        var flow = new InstrumentExitFlow();
        var hasPreviousWorld = false;
        using var previousWorld = new Mat();
        using var pausedTriggers = TaskTriggerDispatcher.Instance().PauseForInference();
        TaskControl.Logger.LogInformation("演奏界面打断任务，保留原步骤并等待恢复");

        while (true)
        {
            await Task.Delay(400, ct);
            if (!ReferenceEquals(TaskContext.Instance().Runtime, runtime) || !runtime.Window.IsAlive)
                throw new OperationCanceledException("绑定的游戏窗口已更换或关闭", ct);

            var viewport = runtime.Window.Viewport.ScreenRect;
            using var frame = CanObserve(runtime) ? runtime.Capture.Capture() : null;
            if (frame == null || frame.Frame.Width != viewport.Width || frame.Frame.Height != viewport.Height
                || viewport != runtime.Window.Viewport.ScreenRect)
            {
                hasPreviousWorld = false;
                flow.Observe(InstrumentExitScreen.Other);
                continue;
            }

            // 截图器可能回收原始缓冲区，区域需要独立持有本次识别的像素。
            using var current = new CaptureContent(frame.Frame.Clone(), 0, 0);
            var screen = current.CaptureRectArea;
            var view = ReadScreen(screen, true, out var target);
            using (target)
            {
                var worldReady = view == InstrumentExitScreen.World && (canResumeInWorld?.Invoke(screen) ?? true);
                var sameWorld = worldReady && hasPreviousWorld && previousWorld.Size() == screen.SrcMat.Size()
                    && previousWorld.Type() == screen.SrcMat.Type() && Cv2.Norm(previousWorld, screen.SrcMat, NormTypes.L1) == 0;
                if (!CanObserve(runtime) || viewport != runtime.Window.Viewport.ScreenRect)
                {
                    hasPreviousWorld = false;
                    flow.Observe(InstrumentExitScreen.Other);
                    continue;
                }

                if (view == InstrumentExitScreen.Other) observeTransition?.Invoke(screen);
                if (worldReady && !sameWorld) screen.SrcMat.CopyTo(previousWorld);
                hasPreviousWorld = worldReady;
                var action = flow.Observe(view, sameWorld, worldReady);
                ct.ThrowIfCancellationRequested();
                if (action is InstrumentExitAction.Close or InstrumentExitAction.Confirm) target!.Click();
                if (action == InstrumentExitAction.Complete) return;
            }
        }
    }

    private static bool CanObserve(GameRuntime runtime) =>
        ReferenceEquals(TaskContext.Instance().Runtime, runtime) && runtime.Window.IsAlive
        && !RunnerContext.Instance.IsSuspend && !runtime.Window.IsMinimized
        && (!runtime.Window.RequiresForeground || runtime.Window.IsForeground)
        && runtime.Window.Viewport.ScreenRect == TaskContext.Instance().SystemInfo.CaptureAreaRect;

    internal static InstrumentExitScreen ReadScreen(ImageRegion screen, bool exiting, out Region? target,
        Func<Mat, string>? readPrompt = null)
    {
        target = null;
        using var confirm = FindDialogButton(screen, confirm: true);
        using var ornament = screen.Find(ElementRecognition.Get("PromptDialogLeftBottomStar", screen));
        using var cancel = exiting || confirm != null || ornament.IsExist() ? FindDialogButton(screen, confirm: false) : null;
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

            if (exiting || text.Contains("退出演奏", StringComparison.Ordinal))
                return InstrumentExitScreen.BlockedPrompt;
        }
        else if (exiting && (confirm != null || cancel != null || ornament.MatchScore >= 0.93))
        {
            // 弹窗正在出现或只剩一部分时，等待下一帧再判断。
            return InstrumentExitScreen.Other;
        }

        if (!hasPrompt && Bv.IsInMainUi(screen))
            return InstrumentExitScreen.World;

        if (MusicKeyAutoDetector.TryDetect(screen.SrcMat) == null)
            return hasPrompt ? InstrumentExitScreen.UnrelatedPrompt : InstrumentExitScreen.Other;

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
