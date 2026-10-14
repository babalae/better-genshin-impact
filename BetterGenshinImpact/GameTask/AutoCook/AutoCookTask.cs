using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Service;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoCook;

public class AutoCookTask : ISoloTask
{
    private readonly ILogger<AutoCookTask> _logger = App.GetLogger<AutoCookTask>();
    private const int UiCheckIntervalMs = 400;
    private const int PeakMinCount = 600; // 最小仙跳墙 700 多
    private const int PeakTolerance = 20;
    private const int PeakStableFrameCount = 3;
    private const int TriggerDropCount = 300; // 正常是 400多
    private const int MinimizedWaitMs = 500;
    private const int MaxInteractWithoutCook = 3; // 后台连续发送交互键仍未开始烹饪时，视为停在烹饪结果界面
    private static readonly Rect CookColorRect1080P = new(600, 660, 730, 190);
    private static readonly Scalar TargetCookColor = new(255, 192, 64);

    // 后台运行：烹饪结束后的结果界面没有交互键提示，需要发送 Esc 关闭
    private bool _awaitingResultDismiss;
    private int _interactWithoutCookCount;

    public string Name => "自动烹饪";

    public async Task Start(CancellationToken ct)
    {
        var runBackground = TaskContext.Instance().Config.AutoCookConfig.RunBackgroundEnabled;
        if (!runBackground)
        {
            await RunCookLoop(false, ct);
            return;
        }

        // 后台运行时请求画中画：游戏不在前台时由调度器把画面推送到画中画窗口
        PictureInPictureService.SetRequestedByTask(true);
        try
        {
            await RunCookLoop(true, ct);
        }
        finally
        {
            PictureInPictureService.SetRequestedByTask(false);
            PictureInPictureService.Hide();
        }
    }

    private async Task RunCookLoop(bool runBackground, CancellationToken ct)
    {
        var assetScale = TaskContext.Instance().SystemInfo.AssetScale;
        var autoCookConfig = TaskContext.Instance().Config.AutoCookConfig;
        var checkIntervalMs = Math.Max(1, autoCookConfig.CheckIntervalMs);
        var stopTaskWhenRecoverButtonDetected = autoCookConfig.StopTaskWhenRecoverButtonDetected;
        var peakMinCount = (int)(PeakMinCount * assetScale);
        var triggerDropCount = (int)(TriggerDropCount * assetScale);
        var cookColorRect = ScaleRect(CookColorRect1080P, assetScale);
        _logger.LogInformation("自动烹饪任务启动");
        if (runBackground)
        {
            _logger.LogInformation("自动烹饪：{Text}", "已开启后台运行，游戏窗口不在前台时使用后台按键，请不要最小化游戏");
        }

        var lastUiCheckTime = DateTime.MinValue;
        var inCookUi = false;
        int? peakColorCount = null;
        int? peakCandidate = null;
        var peakCandidateStableFrames = 0;
        // 按下空格后本轮烹饪结束，停止的烹饪条和结果界面不再建立峰值，否则会多按一次空格
        var roundFinished = false;
        var minimizedLogged = false;

        while (!ct.IsCancellationRequested)
        {
            // 后台运行时不会自动还原窗口；最小化后截图器拿不到新帧，等待用户还原
            if (runBackground && IsGameMinimized())
            {
                if (!minimizedLogged)
                {
                    _logger.LogWarning("自动烹饪：{Text}", "游戏窗口已最小化，等待还原后继续");
                    minimizedLogged = true;
                }

                inCookUi = false;
                ResetPeakState(ref peakColorCount, ref peakCandidate, ref peakCandidateStableFrames);
                await WaitNextCheck(MinimizedWaitMs, runBackground, ct);
                continue;
            }

            minimizedLogged = false;
            using var captureRegion = CaptureToRectArea();
            var useBackground = runBackground && !SystemControl.IsGenshinImpactActive();
            var now = DateTime.UtcNow;
            if (!inCookUi || (now - lastUiCheckTime).TotalMilliseconds >= UiCheckIntervalMs)
            {
                var currentInCookUi = IsInCookUi(captureRegion);
                if (currentInCookUi != inCookUi)
                {
                    ResetPeakState(ref peakColorCount, ref peakCandidate, ref peakCandidateStableFrames);
                    roundFinished = false;
                }

                inCookUi = currentInCookUi;
                lastUiCheckTime = now;
                if (!inCookUi)
                {
                    ResetPeakState(ref peakColorCount, ref peakCandidate, ref peakCandidateStableFrames);
                }
                else
                {
                    var confirmResult = stopTaskWhenRecoverButtonDetected && HasRecoverButton(captureRegion)
                        ? ConfirmResult.RecoverButtonDetected
                        : await TryConfirm(captureRegion, runBackground, stopTaskWhenRecoverButtonDetected, ct);
                    if (confirmResult == ConfirmResult.RecoverButtonDetected)
                    {
                        _logger.LogInformation("自动烹饪：{Text}", "检测到自动烹饪按钮，结束任务");
                        return;
                    }

                    if (confirmResult == ConfirmResult.Confirmed)
                    {
                        ResetPeakState(ref peakColorCount, ref peakCandidate, ref peakCandidateStableFrames);
                        roundFinished = false;
                    }
                }
            }

            if (inCookUi && !roundFinished)
            {
                var currentColorCount = CountTargetColor(captureRegion, cookColorRect);
                if (peakColorCount.HasValue)
                {
                    if (currentColorCount <= peakColorCount.Value - triggerDropCount)
                    {
                        (useBackground ? InputHub.Background : InputHub.Foreground).Keyboard.KeyPress(User32.VK.VK_SPACE);
                        _logger.LogInformation("自动烹饪：{Text}", $"烹饪条像素数量较峰值下降超过{triggerDropCount}，按下空格。峰值:{peakColorCount.Value} 当前:{currentColorCount}");
                        ResetPeakState(ref peakColorCount, ref peakCandidate, ref peakCandidateStableFrames);
                        roundFinished = true;
                        _awaitingResultDismiss = true;
                    }
                }
                else if (TryBuildPeak(currentColorCount, peakMinCount, ref peakCandidate, ref peakCandidateStableFrames, out var builtPeak))
                {
                    peakColorCount = builtPeak;
                    _interactWithoutCookCount = 0;
                    _logger.LogInformation("自动烹饪：{Text}", $"识别到完美烹饪条峰值像素数:{builtPeak}");
                }
            }

            await WaitNextCheck(checkIntervalMs, runBackground, ct);
        }
    }

    private enum ConfirmResult
    {
        NotFound,
        Confirmed,
        RecoverButtonDetected,
    }

    /// <summary>
    /// 处理白色确认按钮。
    /// 前台直接点击；后台点击由游戏按真实光标位置判定，不可靠且会抢占用户鼠标，因此改为发送按键：
    /// “手动烹饪”按钮带有交互键提示，发送交互键；烹饪结束后的结果界面没有交互键提示，发送 Esc 关闭
    /// </summary>
    private async Task<ConfirmResult> TryConfirm(ImageRegion captureRegion, bool runBackground, bool stopTaskWhenRecoverButtonDetected, CancellationToken ct)
    {
        using (var found = captureRegion.Find(ElementRecognition.Get("BtnWhiteConfirm", captureRegion)))
        {
            if (!found.IsExist())
            {
                return ConfirmResult.NotFound;
            }
        }

        // 与 Bv.FindAndClick 相同，等待按钮出现动画结束后再操作。
        // 等待期间界面可能变化（熟练度满时出现“自动烹饪”按钮），重新截图确认，避免多烹饪一次
        await Task.Delay(500, ct);
        using var latestRegion = CaptureToRectArea();
        if (stopTaskWhenRecoverButtonDetected && HasRecoverButton(latestRegion))
        {
            return ConfirmResult.RecoverButtonDetected;
        }

        using var confirmButton = latestRegion.Find(ElementRecognition.Get("BtnWhiteConfirm", latestRegion));
        if (!confirmButton.IsExist())
        {
            return ConfirmResult.NotFound;
        }

        if (!runBackground || SystemControl.IsGenshinImpactActive())
        {
            confirmButton.Click();
            // 前台已点击处理，避免切到后台后在“手动烹饪”界面误发 Esc 退出烹饪
            ResetBackgroundConfirmState();
            _logger.LogInformation("自动烹饪：{Text}", "自动点击确认");
            return ConfirmResult.Confirmed;
        }

        if (_awaitingResultDismiss || _interactWithoutCookCount >= MaxInteractWithoutCook)
        {
            InputHub.Background.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
            ResetBackgroundConfirmState();
            _logger.LogInformation("自动烹饪：{Text}", "后台发送 Esc 关闭烹饪结果界面");
        }
        else
        {
            InputHub.Background.SimulateAction(GIActions.PickUpOrInteract);
            _interactWithoutCookCount++;
            _logger.LogInformation("自动烹饪：{Text}", "后台发送交互键开始手动烹饪");
        }

        return ConfirmResult.Confirmed;
    }

    private static bool HasRecoverButton(ImageRegion captureRegion)
    {
        using var recoverButton = captureRegion.Find(ElementRecognition.Get("BtnWhiteRecover", captureRegion));
        return recoverButton.IsExist();
    }

    private void ResetBackgroundConfirmState()
    {
        _awaitingResultDismiss = false;
        _interactWithoutCookCount = 0;
    }

    /// <summary>
    /// 等待下一次检测。后台运行时只处理暂停，不检查也不抢占游戏窗口焦点
    /// </summary>
    private static async Task WaitNextCheck(int millisecondsTimeout, bool runBackground, CancellationToken ct)
    {
        if (!runBackground)
        {
            await Delay(millisecondsTimeout, ct);
            return;
        }

        TrySuspend();
        await Task.Delay(millisecondsTimeout, ct);
    }

    private static bool IsGameMinimized()
    {
        return TaskContext.Instance().Runtime?.Window.IsMinimized ?? false;
    }

    private static void ResetPeakState(ref int? peakColorCount, ref int? peakCandidate, ref int peakCandidateStableFrames)
    {
        peakColorCount = null;
        peakCandidate = null;
        peakCandidateStableFrames = 0;
    }

    private static bool TryBuildPeak(int currentColorCount, int peakMinCount, ref int? peakCandidate, ref int peakCandidateStableFrames, out int builtPeak)
    {
        builtPeak = 0;
        if (currentColorCount <= peakMinCount)
        {
            peakCandidate = null;
            peakCandidateStableFrames = 0;
            return false;
        }

        if (!peakCandidate.HasValue)
        {
            peakCandidate = currentColorCount;
            peakCandidateStableFrames = 1;
            return false;
        }

        if (Math.Abs(currentColorCount - peakCandidate.Value) <= PeakTolerance)
        {
            peakCandidate = Math.Max(peakCandidate.Value, currentColorCount);
            peakCandidateStableFrames++;
            if (peakCandidateStableFrames >= PeakStableFrameCount && peakCandidate.Value > peakMinCount)
            {
                builtPeak = peakCandidate.Value;
                peakCandidate = null;
                peakCandidateStableFrames = 0;
                return true;
            }

            return false;
        }

        peakCandidate = currentColorCount;
        peakCandidateStableFrames = 1;
        return false;
    }

    private static Rect ScaleRect(Rect rect, double scale)
    {
        return new Rect(
            (int)(rect.X * scale),
            (int)(rect.Y * scale),
            (int)(rect.Width * scale),
            (int)(rect.Height * scale));
    }

    private bool IsInCookUi(ImageRegion captureRegion)
    {
        using var cookIcon = captureRegion.Find(ElementRecognition.Get("UiLeftTopCookIcon", captureRegion));
        return cookIcon.IsExist();
    }

    private int CountTargetColor(ImageRegion captureRegion, Rect cookColorRect)
    {
        using var crop = captureRegion.DeriveCrop(cookColorRect);
        using var rgb = new Mat();
        using var mask = new Mat();
        Cv2.CvtColor(crop.SrcMat, rgb, ColorConversionCodes.BGR2RGB);
        Cv2.InRange(rgb, TargetCookColor, TargetCookColor, mask);
        return Cv2.CountNonZero(mask);
    }
}
