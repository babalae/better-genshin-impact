using System;
using BetterGenshinImpact.GameTask.Common.Job;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

internal static class TrainingGuideRewardPolicy
{
    public const double Threshold = .6;

    public static bool IsCommonReward(string? name) => name is "摩拉" or "好感经验" or "冒险阅历";

    public static string? Recognize(IItemIconRecognizer recognizer, Mat icon, int index)
    {
        if (recognizer is not ItemRecognizer) return recognizer.Recognize(icon);
        var (candidate, originalThreshold) = TrainingGuideIconMatch.Read(recognizer, icon);
        var accepted = !string.IsNullOrEmpty(candidate.Name) && double.IsFinite(candidate.Score) && candidate.Score >= Threshold;
        TrainingGuideDiagnostics.Detail("培养领奖候选：位置={Index}；候选={Name}；分数={Score:F6}；培养阈值={Threshold:F4}；原阈值={Original:F4}；接受={Accepted}；通用奖励={Common}",
            index + 1, candidate.Name, candidate.Score, Threshold, originalThreshold, accepted, IsCommonReward(candidate.Name));
        return accepted ? candidate.Name : null;
    }
}
