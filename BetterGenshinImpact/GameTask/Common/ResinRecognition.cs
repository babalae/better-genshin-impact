using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common;

/// <summary>
/// 大地图顶栏原粹树脂识别。图标模板匹配与 OCR 区域参考 AutoBossTask 的实现。
/// </summary>
public static class ResinRecognition
{
    /// <summary>
    /// 在大地图界面顶栏识别原粹树脂当前值与上限。
    /// 不点击图标，直接解析顶栏 "当前/上限" 文本。
    /// </summary>
    /// <param name="capture">大地图界面的截图区域</param>
    /// <param name="resinIconRect">输出：树脂图标在截图中的矩形（绝对坐标），可用于裁剪截图条；识别失败时为 default</param>
    /// <returns>(当前值, 上限)；识别失败返回 null</returns>
    public static (int Current, int Max)? RecognizeInBigMapTopBar(ImageRegion capture, out Rect resinIconRect)
    {
        var assetScale = TaskContext.Instance().SystemInfo.AssetScale;
        var debugPrefix = DateTime.Now.ToString("yyyyMMdd_HHmmss_ffff", CultureInfo.InvariantCulture);
        SaveDebugImage(capture.SrcMat, $"{debugPrefix}_full.png");

        using var iconSearchRegion = capture.DeriveCrop(new Rect(
            (int)(1200 * assetScale), (int)(25 * assetScale),
            (int)(580 * assetScale), (int)(50 * assetScale)));
        var captureRect = TaskContext.Instance().SystemInfo.ScaleMax1080PCaptureRect;
        var iconRa = iconSearchRegion.Find(RecognitionAssets.Get("AutoBoss", "OriginalResinTopIcon", captureRect.Width, captureRect.Height));
        if (iconRa.IsEmpty())
        {
            resinIconRect = default;
            return null;
        }

        resinIconRect = new Rect(
            iconSearchRegion.X + iconRa.Left,
            iconSearchRegion.Y + iconRa.Top,
            iconRa.Width,
            iconRa.Height);

        var countRect = new Rect(
            resinIconRect.Right + (int)(25 * assetScale),
            (int)(33 * assetScale),
            (int)(105 * assetScale),
            (int)(25 * assetScale));
        using var countRegion = capture.DeriveCrop(countRect);
        SaveDebugImage(countRegion.SrcMat, $"{debugPrefix}_count-raw.png");

        // 根据树脂数字实测 RGB 颜色生成暖灰色文字掩膜，仅用于调试，不参与当前识别流程。
        // 样本：(236,229,216)、(227,222,216)、(191,191,184)。HSV 范围留出抗锯齿和亮度变化余量。
        using var textColorMask = OpenCvCommonHelper.InRangeHsv(
            countRegion.SrcMat,
            new Scalar(8, 0, 175),
            new Scalar(38, 40, 255));
        SaveDebugImage(textColorMask, $"{debugPrefix}_count-text-color-mask.png");

        var countText = OcrFactory.Paddle.OcrWithoutDetector(countRegion.SrcMat);

        // 顶栏文本为 "当前/上限"，上限固定为三位数(200)；斜杠可能被 OCR 认成 7 或 1，
        // 因此删除全部非数字后按"后三位为上限、其余为当前值"切分
        var digits = Regex.Replace(StringUtils.ConvertFullWidthNumToHalfWidth(countText), @"\D", "");
        if (digits.Length < 4)
        {
            return null;
        }

        if (!int.TryParse(digits[..^3], NumberStyles.None, CultureInfo.InvariantCulture, out var current)
            || !int.TryParse(digits[^3..], NumberStyles.None, CultureInfo.InvariantCulture, out var max)
            || current < 0 || max <= 0 || current > max)
        {
            return null;
        }

        return (current, max);
    }

    private static void SaveDebugImage(Mat image, string fileName)
    {
        try
        {
            var directory = Global.Absolute(@"log\ResinRecognition");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, fileName);
            if (!Cv2.ImWrite(path, image))
            {
                TaskControl.Logger.LogWarning("树脂识别调试截图保存失败: {Path}", path);
            }
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogDebug(e, "树脂识别调试截图保存异常: {FileName}", fileName);
        }
    }
}
