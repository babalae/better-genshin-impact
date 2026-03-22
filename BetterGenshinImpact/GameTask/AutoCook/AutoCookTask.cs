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
    private const int CheckIntervalMs = 10;
    private const int UiCheckIntervalMs = 400;
    private static readonly Rect CookColorRect = new(600, 660, 730, 190);
    private static readonly Scalar TargetCookColor = new(255, 192, 64);

    public string Name => "自动烹饪";

    public async Task Start(CancellationToken ct)
    {
        _logger.LogInformation("自动烹饪任务启动");
        var lastUiCheckTime = DateTime.MinValue;
        var inCookUi = false;
        var previousColorCount = -1;

        while (!ct.IsCancellationRequested)
        {
            using var captureRegion = CaptureToRectArea();
            var now = DateTime.UtcNow;
            if (!inCookUi || (now - lastUiCheckTime).TotalMilliseconds >= UiCheckIntervalMs)
            {
                inCookUi = IsInCookUi(captureRegion);
                lastUiCheckTime = now;
                if (!inCookUi)
                {
                    previousColorCount = -1;
                }
                else
                {
                    if (Bv.ClickWhiteConfirmButton(captureRegion))
                    {
                        _logger.LogInformation("自动烹饪：{Text}", "自动点击确认");
                    }
                }
            }

            if (inCookUi)
            {
                var currentColorCount = CountTargetColor(captureRegion);
                if (previousColorCount > 20 && currentColorCount <= previousColorCount * 0.9)
                {
                    Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_SPACE);
                    _logger.LogInformation("自动烹饪：{Text}", $"检测到像素数量下降，按下空格。{previousColorCount} -> {currentColorCount}");
                }

                previousColorCount = currentColorCount;
            }

            await Delay(CheckIntervalMs, ct);
        }
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