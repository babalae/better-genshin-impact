using System;
using System.Diagnostics;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.OCR;

/// <summary>
/// 当 OCR 引擎不支持 IOcrMatchService 时的回退实现。
/// 使用普通 OCR 识别文字后，通过字符串相似度进行匹配。
/// </summary>
public class OcrMatchFallbackService : IOcrMatchService
{
    private readonly IOcrService _ocrService;

    public OcrMatchFallbackService(IOcrService ocrService)
    {
        _ocrService = ocrService;
    }

    public double OcrMatch(Mat mat, string target)
    {
        var startTime = Stopwatch.GetTimestamp();
        var ocrResult = _ocrService.OcrResult(mat);
        var score = ComputeBestTextSimilarity(ocrResult, target);
        var time = Stopwatch.GetElapsedTime(startTime);
        Debug.WriteLine($"OcrMatchFallback 耗时 {time.TotalMilliseconds}ms 目标: {target} 分数: {score:F4}");
        return score;
    }

    public double OcrMatchDirect(Mat mat, string target)
    {
        var startTime = Stopwatch.GetTimestamp();
        var text = _ocrService.OcrWithoutDetector(mat);
        var score = ComputeTextSimilarity(text, target);
        var time = Stopwatch.GetElapsedTime(startTime);
        Debug.WriteLine($"OcrMatchDirectFallback 耗时 {time.TotalMilliseconds}ms 目标: {target} 分数: {score:F4}");
        return score;
    }

    /// <summary>
    /// 在 OCR 结果的所有区域中找到与目标字符串最相似的分数。
    /// </summary>
    private static double ComputeBestTextSimilarity(OcrResult ocrResult, string target)
    {
        double bestScore = 0;
        foreach (var region in ocrResult.Regions)
        {
            var score = ComputeTextSimilarity(region.Text, target);
            if (score > bestScore) bestScore = score;
            if (score >= 1.0) break;
        }

        return bestScore;
    }

    /// <summary>
    /// 计算两个字符串的相似度 (0~1)。
    /// 优先检查子串包含关系，否则使用编辑距离计算。
    /// </summary>
    public static double ComputeTextSimilarity(string text, string target)
    {
        if (string.IsNullOrEmpty(target)) return 1.0;
        if (string.IsNullOrEmpty(text)) return 0.0;
        if (text.Contains(target, StringComparison.OrdinalIgnoreCase)) return 1.0;
        if (target.Contains(text, StringComparison.OrdinalIgnoreCase)) return (double)text.Length / target.Length;

        var distance = LevenshteinDistance(text, target);
        var maxLen = Math.Max(text.Length, target.Length);
        return 1.0 - (double)distance / maxLen;
    }

    /// <summary>
    /// 计算两个字符串之间的编辑距离（Levenshtein Distance）。
    /// </summary>
    public static int LevenshteinDistance(string s, string t)
    {
        var sLen = s.Length;
        var tLen = t.Length;
        var prev = new int[tLen + 1];
        var curr = new int[tLen + 1];

        for (var j = 0; j <= tLen; j++)
            prev[j] = j;

        for (var i = 1; i <= sLen; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= tLen; j++)
            {
                var cost = s[i - 1] == t[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }

            (prev, curr) = (curr, prev);
        }

        return prev[tLen];
    }
}
