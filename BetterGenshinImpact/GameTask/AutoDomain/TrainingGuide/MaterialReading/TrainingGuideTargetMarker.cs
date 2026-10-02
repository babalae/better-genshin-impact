using System;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common.Job;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

/// <summary>培养标记遮挡的局部补救，不改变公共识别器及其阈值。</summary>
internal static class TrainingGuideTargetMarker
{
    // 试验阈值：仍需通过入口家族和奖励等级完整性校验。
    private const double MinimumMaterialScore = .45;
    private const double MinimumMarkerScore = .85;

    public static TrainingGuideMaterial? TryRecover((ItemIconCandidate Candidate, double Threshold)? match,
        Mat strip, Rect icon, double scale, ILogger logger, string captureId, string level, int index)
    {
        if (match == null) return null;
        try
        {
            var (candidate, threshold) = match.Value;
            var material = TrainingGuideMaterialCatalog.Find(candidate.Name);
            // 只对原阈值以下的培养材料候选使用遮挡补救，不能替换已确认的其他奖励。
            if (material == null || !double.IsFinite(candidate.Score) || candidate.Score >= threshold)
                return null;

            var markerScore = Match(strip, icon, scale);
            var accepted = candidate.Score >= MinimumMaterialScore && markerScore >= MinimumMarkerScore;
            TrainingGuideDiagnostics.LogIconLocation(captureId,
                $"遮挡候选：入口={level}；位置={index}；候选={candidate.Name}；材料分数={candidate.Score:F6}；原阈值={threshold:F4}；遮挡材料阈值={MinimumMaterialScore:F4}；标记分数={markerScore:F6}；标记阈值={MinimumMarkerScore:F4}；允许进入列表校验={accepted}；原因={(accepted ? "左上角培养标记匹配且候选达到低分阈值" : "标记或材料分数不足")}");
            return accepted ? material : null;
        }
        catch (Exception e)
        {
            logger.LogWarning("培养标记验证失败 [{CaptureId}]，位置 {Index}：{Message}，不放行低分候选",
                captureId, index, e.GetBaseException().Message);
            return null;
        }
    }

    private static double Match(Mat strip, Rect icon, double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0) return 0;
        // 从原始奖励条取左上角，向外留少量余量，避免定位框切掉标记尖角。
        var left = Math.Max(0, icon.X - (int)Math.Ceiling(4 * scale));
        var top = Math.Max(0, icon.Y - (int)Math.Ceiling(6 * scale));
        var right = Math.Min(strip.Width, icon.X + (int)Math.Ceiling(40 * scale));
        var bottom = Math.Min(strip.Height, icon.Y + (int)Math.Ceiling(44 * scale));
        using var roi = new Mat(strip, new Rect(left, top, right - left, bottom - top));
        using var bgr = new Mat();
        if (roi.Channels() == 4) Cv2.CvtColor(roi, bgr, ColorConversionCodes.BGRA2BGR);
        else roi.CopyTo(bgr);
        using var source = new Mat();
        Cv2.Resize(bgr, source, new Size((int)Math.Round(roi.Width / scale), (int)Math.Round(roi.Height / scale)),
            0, 0, scale >= 1 ? InterpolationFlags.Area : InterpolationFlags.Cubic);
        using var template = Cv2.ImRead(Global.Absolute(
            @"GameTask\AutoDomain\Assets\1920x1080\training_target_marker.png"), ImreadModes.Unchanged);
        if (template.Empty() || template.Channels() != 4)
            throw new InvalidOperationException("培养标记模板缺失或没有透明通道");
        using var templateBgr = new Mat();
        using var mask = new Mat();
        Cv2.CvtColor(template, templateBgr, ColorConversionCodes.BGRA2BGR);
        Cv2.ExtractChannel(template, mask, 3);
        if (source.Width < template.Width || source.Height < template.Height) return 0;
        using var scores = new Mat();
        // 透明部分不参与匹配；相关系数消除纯亮度背景造成的虚高匹配。
        Cv2.MatchTemplate(source, templateBgr, scores, TemplateMatchModes.CCoeffNormed, mask);
        var best = 0d;
        for (var y = 0; y < scores.Rows; y++)
        for (var x = 0; x < scores.Cols; x++)
        {
            var score = scores.At<float>(y, x);
            if (float.IsFinite(score) && score <= 1.00001f) best = Math.Max(best, score);
        }
        return Math.Min(1, best);
    }
}
