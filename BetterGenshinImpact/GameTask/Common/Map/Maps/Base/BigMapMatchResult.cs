using System;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

/// <summary>
/// 大地图在完整地图中的匹配结果及其可靠性。
/// </summary>
public readonly record struct BigMapMatchResult(
    Rect ImageRect,
    double Confidence,
    int AnchorCount,
    int InlierCount,
    double FitError,
    string Source)
{
    public const double ReliableConfidenceThreshold = 0.55d;

    public bool HasResult => ImageRect.Width > 0 && ImageRect.Height > 0;

    public bool IsReliable => HasResult && Confidence >= ReliableConfidenceThreshold;

    public static BigMapMatchResult Failed(string source)
    {
        return new BigMapMatchResult(default, 0d, 0, 0, double.PositiveInfinity, source);
    }
}
