using System;
using BetterGenshinImpact.GameTask.Common.Job;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

/// <summary>培养入口专用的 ItemV2 候选读取，不改变公共识别器的阈值和返回值。</summary>
internal static class TrainingGuideIconMatch
{
    internal sealed record RecognitionResult(string? RecognizedName, (ItemIconCandidate Candidate, double Threshold)? CandidateMatch);

    public static RecognitionResult Recognize(
        IItemIconRecognizer recognizer, Mat icon)
    {
        if (recognizer is not ItemRecognizer) return new RecognitionResult(recognizer.Recognize(icon), null);
        // 同一次推理供原阈值判断、诊断和遮挡补救共同使用。
        var match = Read(recognizer, icon);
        return new RecognitionResult(match.Candidate.Score >= match.Threshold ? match.Candidate.Name : null, match);
    }

    public static (ItemIconCandidate Candidate, double Threshold) Read(IItemIconRecognizer recognizer, Mat icon)
    {
        if (recognizer is not ItemRecognizer itemRecognizer)
            throw new InvalidOperationException("无法读取 ItemV2 诊断候选和阈值");
        return itemRecognizer.MatchWithThreshold(icon);
    }
}
