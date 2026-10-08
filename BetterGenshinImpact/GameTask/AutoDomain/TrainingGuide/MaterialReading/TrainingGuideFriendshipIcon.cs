using System;
using BetterGenshinImpact.Core.Config;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

/// <summary>仅核验第三项是否为好感经验；不匹配时仍交给材料识别器。</summary>
internal static class TrainingGuideFriendshipIcon
{
    private const double Threshold = .85;

    public static bool Matches(Mat normalized, ILogger logger, string captureId, string level)
    {
        try
        {
            using var template = Cv2.ImRead(Global.Absolute(
                @"GameTask\AutoDomain\Assets\1920x1080\training_friendship_top.png"), ImreadModes.Color);
            if (template.Empty()) throw new InvalidOperationException("好感经验上半部模板缺失");
            // 输入与参考图均为 125×125。只搜索上部，排除底部黑条和数量数字；留少量定位余量。
            using var top = new Mat(normalized, new Rect(0, 0, normalized.Width, 90));
            using var scores = new Mat();
            Cv2.MatchTemplate(top, template, scores, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(scores, out _, out double score, out _, out _);
            var matched = double.IsFinite(score) && score >= Threshold;
            TrainingGuideDiagnostics.LogIconLocation(captureId,
                $"第三项好感核验：入口={level}；位置=3；候选=好感经验；分数={score:F6}；阈值={Threshold:F4}；匹配={matched}；结果={(matched ? "跳过第三项，从第四项识别材料" : "第三项继续进行材料识别")}");
            return matched;
        }
        catch (Exception e)
        {
            logger.LogWarning("第三项好感核验失败 [{CaptureId}]：{Message}，第三项继续进行材料识别",
                captureId, e.GetBaseException().Message);
            return false;
        }
    }
}
