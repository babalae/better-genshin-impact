using BetterGenshinImpact.GameTask;
using OpenCvSharp;
using System;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

/// <summary>
/// 传送过渡页（loading screen）检测器：联机锄地路径专用。
///
/// 用于解决 .kiro/specs/multiplayer-tp-success-via-loading-screen/bugfix.md 描述的
/// "联机倒地复苏 → 派蒙重新可见 → Bv.IsInMainUi==true → 误判传送成功"问题。
///
/// 判据基于过渡页的视觉特征：屏幕中央 ROI 在 HSV 色彩空间下饱和度方差极低、
/// 灰度图中极端亮 (>=220) 或极端暗 (&lt;=70) 像素占比 >= 0.85。
///
/// 设计为纯函数：不读 TaskContext.SystemInfo（生产路径除外）、不持有 logger、不触发截图，
/// 便于 Property-Based Test 直接构造合成 Mat 撒输入。
/// </summary>
internal static class TeleportLoadingDetector
{
    // === 阈值常量 (bugfix.md §"Q4 ROI 与判据阈值") ===
    private const double SaturationStdThreshold = 25.0;
    private const double DarkOrLightRatioThreshold = 0.85;
    private const int DarkGrayUpperBound = 70;
    private const int LightGrayLowerBound = 220;

    // === ROI 常量 (1080P 基准, bugfix.md §"Q4") ===
    private const int RoiX1080P = 480;
    private const int RoiY1080P = 80;
    private const int RoiW1080P = 960;
    private const int RoiH1080P = 540;

    /// <summary>
    /// 生产入口：自动按当前分辨率换算 ROI。
    /// </summary>
    public static bool IsLoadingScreen(Mat bgr)
    {
        if (bgr == null || bgr.Empty()) return false;
        var scale = TaskContext.Instance().SystemInfo.ScaleTo1080PRatio;
        int x = (int)(RoiX1080P * scale);
        int y = (int)(RoiY1080P * scale);
        int w = (int)(RoiW1080P * scale);
        int h = (int)(RoiH1080P * scale);
        return IsLoadingScreen(bgr, x, y, w, h);
    }

    /// <summary>
    /// 测试入口：显式传入当前分辨率 ROI（已换算），不读 TaskContext。
    /// PBT 用此重载直接撒输入。
    /// </summary>
    public static bool IsLoadingScreen(Mat bgr, int roiX, int roiY, int roiW, int roiH)
    {
        if (bgr == null || bgr.Empty()) return false;

        // 1. ROI 越界裁剪
        int x = Math.Max(0, roiX);
        int y = Math.Max(0, roiY);
        int w = Math.Min(roiW, bgr.Width - x);
        int h = Math.Min(roiH, bgr.Height - y);
        if (w <= 0 || h <= 0) return false;

        using var roi = new Mat(bgr, new Rect(x, y, w, h));

        // 2. 灰度 / HSV 转换（兼容传入已是单通道灰度图的场景）
        Mat? hsv = null;
        Mat? gray = null;
        try
        {
            if (roi.Channels() == 1)
            {
                gray = roi.Clone();
                // 单通道无 HSV，饱和度方差按 0 处理（必然 < 阈值），仅靠亮/暗比例判定
                hsv = null;
            }
            else
            {
                hsv = new Mat();
                Cv2.CvtColor(roi, hsv, ColorConversionCodes.BGR2HSV);
                gray = new Mat();
                Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);
            }

            // 3. 饱和度标准差
            double saturationStd;
            if (hsv != null)
            {
                Cv2.MeanStdDev(hsv, out _, out var stdDev);
                // stdDev: [Hmean_std, Smean_std, Vmean_std]，取 S 通道（index 1）
                saturationStd = stdDev.Val1;
            }
            else
            {
                saturationStd = 0.0;
            }

            // 4. 亮 / 暗比例
            int total = gray!.Rows * gray.Cols;
            if (total <= 0) return false;
            using var darkMask = new Mat();
            using var lightMask = new Mat();
            Cv2.Threshold(gray, darkMask, DarkGrayUpperBound, 255, ThresholdTypes.BinaryInv);
            Cv2.Threshold(gray, lightMask, LightGrayLowerBound, 255, ThresholdTypes.Binary);
            double darkRatio = (double)Cv2.CountNonZero(darkMask) / total;
            double lightRatio = (double)Cv2.CountNonZero(lightMask) / total;

            // 5. 综合判据：低饱和方差 + 极端亮/暗比例任一 >= 0.85
            return saturationStd < SaturationStdThreshold
                   && (darkRatio > DarkOrLightRatioThreshold
                       || lightRatio > DarkOrLightRatioThreshold);
        }
        finally
        {
            hsv?.Dispose();
            gray?.Dispose();
        }
    }
}
