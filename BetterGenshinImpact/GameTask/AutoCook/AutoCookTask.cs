using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.Common.BgiVision;
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
    private static readonly Rect CookColorRect = new(600, 660, 730, 190);
    private static readonly Scalar TargetCookColor = new(255, 192, 64);

    public string Name => "自动烹饪";

    public async Task Start(CancellationToken ct)
    {
        var checkIntervalMs = Math.Max(1, TaskContext.Instance().Config.AutoCookConfig.CheckIntervalMs);
        _logger.LogInformation("自动烹饪任务启动");
        var lastUiCheckTime = DateTime.MinValue;
        var inCookUi = false;
        int? peakColorCount = null;
        int? peakCandidate = null;
        var peakCandidateStableFrames = 0;

        while (!ct.IsCancellationRequested)
        {
            using var captureRegion = CaptureToRectArea();
            var now = DateTime.UtcNow;
            if (!inCookUi || (now - lastUiCheckTime).TotalMilliseconds >= UiCheckIntervalMs)
            {
                var currentInCookUi = IsInCookUi(captureRegion);
                if (currentInCookUi != inCookUi)
                {
                    ResetPeakState(ref peakColorCount, ref peakCandidate, ref peakCandidateStableFrames);
                }

                inCookUi = currentInCookUi;
                lastUiCheckTime = now;
                if (!inCookUi)
                {
                    ResetPeakState(ref peakColorCount, ref peakCandidate, ref peakCandidateStableFrames);
                }
                else
                {
                    if (Bv.ClickWhiteConfirmButton(captureRegion))
                    {
                        ResetPeakState(ref peakColorCount, ref peakCandidate, ref peakCandidateStableFrames);
                        _logger.LogInformation("自动烹饪：{Text}", "自动点击确认");
                    }
                }
            }

            if (inCookUi)
            {
                var currentColorCount = CountTargetColor(captureRegion);
                if (peakColorCount.HasValue)
                {
                    if (currentColorCount <= peakColorCount.Value - TriggerDropCount)
                    {
                        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_SPACE);
                        _logger.LogInformation("自动烹饪：{Text}", $"烹饪条像素数量较峰值下降超过{TriggerDropCount}，按下空格。峰值:{peakColorCount.Value} 当前:{currentColorCount}");
                        ResetPeakState(ref peakColorCount, ref peakCandidate, ref peakCandidateStableFrames);
                    }
                }
                else if (TryBuildPeak(currentColorCount, ref peakCandidate, ref peakCandidateStableFrames, out var builtPeak))
                {
                    peakColorCount = builtPeak;
                    _logger.LogInformation("自动烹饪：{Text}", $"识别到完美烹饪条峰值像素数:{builtPeak}");
                }
            }

            await Delay(checkIntervalMs, ct);
        }
    }

    private static void ResetPeakState(ref int? peakColorCount, ref int? peakCandidate, ref int peakCandidateStableFrames)
    {
        peakColorCount = null;
        peakCandidate = null;
        peakCandidateStableFrames = 0;
    }

    private static bool TryBuildPeak(int currentColorCount, ref int? peakCandidate, ref int peakCandidateStableFrames, out int builtPeak)
    {
        builtPeak = 0;
        if (currentColorCount <= PeakMinCount)
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
            if (peakCandidateStableFrames >= PeakStableFrameCount && peakCandidate.Value > PeakMinCount)
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

    private bool IsInCookUi(ImageRegion captureRegion)
    {
        using var cookIcon = captureRegion.Find(ElementAssets.Instance.UiLeftTopCookIcon);
        return cookIcon.IsExist();
    }

    private int CountTargetColor(ImageRegion captureRegion)
    {
        using var crop = captureRegion.DeriveCrop(CookColorRect);
        using var rgb = new Mat();
        using var mask = new Mat();
        Cv2.CvtColor(crop.SrcMat, rgb, ColorConversionCodes.BGR2RGB);
        Cv2.InRange(rgb, TargetCookColor, TargetCookColor, mask);
        return Cv2.CountNonZero(mask);
    }
}
