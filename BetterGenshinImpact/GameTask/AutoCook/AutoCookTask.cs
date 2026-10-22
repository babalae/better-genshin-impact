using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.GameTask.SoloTask;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoCook;

/// <summary>
/// 自动烹饪：在烹饪界面识别完美烹饪区间并自动按下空格
/// </summary>
[SoloTaskInfo(TaskId, "自动烹饪", Group = "采集与生活", Order = 330, Scopes = SoloTaskScopes.Standalone)]
public class AutoCookTask : ISoloTask<NoParam, NoData>
{
    /// <summary>
    /// 稳定任务标识
    /// </summary>
    public const string TaskId = "auto_cook";

    private readonly ILogger<AutoCookTask> _logger = App.GetLogger<AutoCookTask>();
    private const int UiCheckIntervalMs = 400;
    private const int PeakMinCount = 600; // 最小仙跳墙 700 多
    private const int PeakTolerance = 20;
    private const int PeakStableFrameCount = 3;
    private const int TriggerDropCount = 300; // 正常是 400多
    private static readonly Rect CookColorRect1080P = new(600, 660, 730, 190);
    private static readonly Scalar TargetCookColor = new(255, 192, 64);

    /// <summary>
    /// 本次运行使用的运行环境
    /// </summary>
    private readonly GameRuntime _runtime;

    /// <summary>
    /// 配置根，由创建方传入
    /// </summary>
    private readonly AllConfig _config;

    /// <summary>
    /// 创建自动烹饪任务
    /// </summary>
    /// <param name="runtime">本次运行使用的运行环境</param>
    /// <param name="config">配置根</param>
    public AutoCookTask(GameRuntime runtime, AllConfig config)
    {
        _runtime = runtime;
        _config = config;
    }

    /// <summary>
    /// 运行自动烹饪，直到取消；开启“检测到自动烹饪按钮时结束”且识别到该按钮时返回成功。取消时抛出异常
    /// </summary>
    /// <param name="param">空参数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>任务结果</returns>
    public async Task<SoloTaskResult<NoData>> RunAsync(NoParam param, CancellationToken ct)
    {
        var assetScale = _runtime.SystemInfo.AssetScale;
        var autoCookConfig = _config.AutoCookConfig;
        var checkIntervalMs = Math.Max(1, autoCookConfig.CheckIntervalMs);
        var stopTaskWhenRecoverButtonDetected = autoCookConfig.StopTaskWhenRecoverButtonDetected;
        var peakMinCount = (int)(PeakMinCount * assetScale);
        var triggerDropCount = (int)(TriggerDropCount * assetScale);
        var cookColorRect = ScaleRect(CookColorRect1080P, assetScale);
        _logger.LogInformation("自动烹饪任务启动");
        var lastUiCheckTime = DateTime.MinValue;
        var inCookUi = false;
        int? peakColorCount = null;
        int? peakCandidate = null;
        var peakCandidateStableFrames = 0;

        while (!ct.IsCancellationRequested)
        {
            using var captureRegion = _runtime.CaptureToRectArea();
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
                    if (stopTaskWhenRecoverButtonDetected)
                    {
                        var e = captureRegion.Find(ElementRecognition.Get("BtnWhiteRecover", captureRegion));
                        if (e.IsExist())
                        {
                            // 出现自动烹饪按钮说明熟练度已满，确认完成
                            _logger.LogInformation("自动烹饪：{Text}", "检测到自动烹饪按钮，结束任务");
                            return SoloTaskResult.Success("检测到自动烹饪按钮，结束任务");
                        }
                    }

                    if (Bv.ClickWhiteConfirmButton(captureRegion))
                    {
                        ResetPeakState(ref peakColorCount, ref peakCandidate, ref peakCandidateStableFrames);
                        _logger.LogInformation("自动烹饪：{Text}", "自动点击确认");
                    }
                }
            }

            if (inCookUi)
            {
                var currentColorCount = CountTargetColor(captureRegion, cookColorRect);
                if (peakColorCount.HasValue)
                {
                    if (currentColorCount <= peakColorCount.Value - triggerDropCount)
                    {
                        InputHub.Foreground.Keyboard.KeyPress(User32.VK.VK_SPACE);
                        _logger.LogInformation("自动烹饪：{Text}", $"烹饪条像素数量较峰值下降超过{triggerDropCount}，按下空格。峰值:{peakColorCount.Value} 当前:{currentColorCount}");
                        ResetPeakState(ref peakColorCount, ref peakCandidate, ref peakCandidateStableFrames);
                    }
                }
                else if (TryBuildPeak(currentColorCount, peakMinCount, ref peakCandidate, ref peakCandidateStableFrames, out var builtPeak))
                {
                    peakColorCount = builtPeak;
                    _logger.LogInformation("自动烹饪：{Text}", $"识别到完美烹饪条峰值像素数:{builtPeak}");
                }
            }

            await _runtime.Delay(checkIntervalMs, ct);
        }

        // 循环只会因取消结束，取消时向上抛出
        ct.ThrowIfCancellationRequested();
        return SoloTaskResult.Unverified("自动烹饪结束");
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
