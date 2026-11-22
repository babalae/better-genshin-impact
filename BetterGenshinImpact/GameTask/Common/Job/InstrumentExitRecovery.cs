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

namespace BetterGenshinImpact.GameTask.Common.Job;

internal static class InstrumentExitRecovery
{
    internal static async Task<bool> TryExitAsync(ImageRegion screen, CancellationToken ct)
    {
        var runtime = TaskContext.Instance().Runtime;
        var flow = new InstrumentExitFlow();
        var hasPreviousWorld = false;
        try
        {
            var result = Advance(flow, screen, runtime, ct, ref hasPreviousWorld);
            if (!flow.Started)
                return result == InstrumentExitAction.Complete;

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
                result = Advance(flow, current.CaptureRectArea, runtime, ct, ref hasPreviousWorld, previousWorld);
                if (result == InstrumentExitAction.Complete)
                {
                    ct.ThrowIfCancellationRequested();
                    EnsureTargetWindow(runtime);
                    return true;
                }
            }

            throw new InstrumentExitRecoveryException("退出演奏后未确认回到大世界，已暂停当前任务");
        }
        catch (Exception e) when (flow.Started && e is not (NormalEndException or OperationCanceledException))
        {
            TaskControl.Logger.LogDebug(e, "退出演奏时截图、识别或输入失败");
            throw new InstrumentExitRecoveryException("退出演奏时无法继续验证当前界面，已暂停当前任务");
        }
    }

    private static InstrumentExitAction Advance(InstrumentExitFlow flow, ImageRegion screen, GameRuntime? runtime,
        CancellationToken ct, ref bool hasPreviousWorld, Mat? previousWorld = null)
    {
        ct.ThrowIfCancellationRequested();
        var view = ReadScreen(screen, flow.Started, out var target);
        using (target)
        {
            bool sameWorldFrame = false;
            if (previousWorld != null)
            {
                if (view == InstrumentExitScreen.World)
                {
                    var current = screen.SrcMat;
                    sameWorldFrame = hasPreviousWorld
                                     && previousWorld.Size() == current.Size()
                                     && previousWorld.Type() == current.Type()
                                     && Cv2.Norm(previousWorld, current, NormTypes.L1) == 0;
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
                    : "未能确认演奏页的关闭按钮或退出演奏弹窗，已暂停当前任务");
            }

            if (action is InstrumentExitAction.Close or InstrumentExitAction.Confirm)
            {
                ct.ThrowIfCancellationRequested();
                EnsureTargetWindow(runtime);
                target!.Click();
            }

            return action;
        }
    }

    private static void EnsureTargetWindow(GameRuntime? runtime)
    {
        if (RunnerContext.Instance.IsSuspend)
            throw new InstrumentExitRecoveryException("任务已暂停，已停止退出演奏");

        if (runtime == null || !ReferenceEquals(TaskContext.Instance().Runtime, runtime)
                            || (runtime.Window.RequiresForeground && !runtime.Window.IsForeground))
        {
            throw new InstrumentExitRecoveryException("绑定的游戏窗口已切换或失焦，已暂停退出演奏");
        }
    }

    internal static InstrumentExitScreen ReadScreen(ImageRegion screen, bool exiting, out Region? target,
        Func<Mat, string>? readPrompt = null)
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

        if (MusicKeyAutoDetector.TryDetect(screen.SrcMat) == null)
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
internal sealed class InstrumentExitRecoveryException(string message) : NormalEndException(message);
