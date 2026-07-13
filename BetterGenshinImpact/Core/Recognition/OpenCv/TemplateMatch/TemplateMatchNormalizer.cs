using System;
using System.Linq;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.OpenCv.TemplateMatch;

public class TemplateMatchNormalizer
{
    public TemplateMatchModes Mode;
    public double Value;
    public int Sign;
    private readonly double _bestMatchValue;
    private readonly double _worstMatchValue;
    
    /// <summary>
    /// 构造模板匹配归一化器
    /// </summary>
    /// <param name="mode">模板匹配类型</param>
    /// <param name="template">模板图</param>
    /// <param name="mask">遮罩</param>
    public TemplateMatchNormalizer(Mat template, Mat? mask = null, TemplateMatchModes mode = TemplateMatchModes.SqDiff)
    {
        Init(mode);
        (_bestMatchValue, _worstMatchValue) = mode switch
        {
            TemplateMatchModes.SqDiff =>
                SqDiffMatchValue(template, mask),
            TemplateMatchModes.SqDiffNormed =>
                (0, 1),
            TemplateMatchModes.CCorr =>
                CCorrMatchValue(template, mask),
            TemplateMatchModes.CCorrNormed =>
                (1, 0),
            TemplateMatchModes.CCoeff =>
                CCoeffMatchValue(template, mask),
            TemplateMatchModes.CCoeffNormed =>
                (1, -1),
            _ => throw new ArgumentException($"未知的模板匹配模式: {mode}", nameof(mode))
        };
    }
    
    public TemplateMatchNormalizer(Mat[] templates, Mat? mask = null, TemplateMatchModes mode = TemplateMatchModes.SqDiff, int[]? channels = null)
    {
        Init(mode);
        (_bestMatchValue, _worstMatchValue) = mode switch
        {
            TemplateMatchModes.SqDiff =>
                SumMatchValue(templates, mask, SqDiffMatchValue, channels),
            TemplateMatchModes.SqDiffNormed =>
                (0, 1),
            TemplateMatchModes.CCorr =>
                SumMatchValue(templates, mask, CCorrMatchValue, channels),
            TemplateMatchModes.CCorrNormed =>
                (1, 0),
            TemplateMatchModes.CCoeff =>
                SumMatchValue(templates, mask, CCoeffMatchValue, channels),
            TemplateMatchModes.CCoeffNormed =>
                (1, -1),
            _ => throw new ArgumentException($"未知的模板匹配模式: {mode}", nameof(mode))
        };
    }

    public TemplateMatchNormalizer(double bestMatchValue, double worstMatchValue, TemplateMatchModes mode = TemplateMatchModes.SqDiff)
    {
        Init(mode);
        _bestMatchValue = bestMatchValue;
        _worstMatchValue = worstMatchValue;
    }

    private void Init(TemplateMatchModes mode)
    {
        Mode = mode;
        Sign = mode switch
        {
            TemplateMatchModes.SqDiff or TemplateMatchModes.SqDiffNormed => -1,
            _ => 1
        };
        Reset();
    }

    /// <summary>
    /// 更新 value
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public bool Update(double value)
    {
        if (double.IsPositiveInfinity(value) || double.IsNegativeInfinity(value))
            return false;
        if (value > Math.Max(_bestMatchValue, _worstMatchValue) || value < Math.Min(_bestMatchValue, _worstMatchValue))
            return false;
        if (Sign * (Value - value) >= 0) return false;
        Value = value;
        return true;
    }

    public void Reset()
    {
        Value = Sign > 0 ? double.NegativeInfinity : double.PositiveInfinity;
    }

    public double Confidence()
    {
        return (_bestMatchValue == _worstMatchValue)? 0 :
        (Value - _worstMatchValue) / (_bestMatchValue - _worstMatchValue);
    }

    public static (double, double) SumMatchValue(Mat[] templates, Mat? mask, Func<Mat, Mat?, (double, double)> getValue, int[]? channels = null)
    {
        var outValue = (0.0, 0.0);
        var selectedTemplates = channels?.Where(i => i >= 0 && i < templates.Length)
                                    .Select(i => templates[i])
                                ?? templates;
        foreach (var template in selectedTemplates)
        {
            var matchValue = getValue(template, mask);
            outValue.Item1 += matchValue.Item1;
            outValue.Item2 += matchValue.Item2;
        }
        return outValue;
    }

    public static (double, double) SqDiffMatchValue(Mat template, Mat? mask = null)
    {
        using var inverted = new Mat();
        Cv2.Subtract(255, template, inverted);
        Cv2.Max(template, inverted, inverted);
        var worstVal = Cv2.Norm(inverted, NormTypes.L2SQR, mask);
        return (0, worstVal);
    }

    public static (double, double) CCorrMatchValue(Mat template, Mat? mask = null)
    {
        var bestVal = Cv2.Norm(template, NormTypes.L2SQR, mask);
        return (bestVal, 0);
    }

    public static (double, double) CCoeffMatchValue(Mat template, Mat? mask = null)
    {
        using var result = new Mat();
        Cv2.MatchTemplate(template, template, result, TemplateMatchModes.CCoeff, mask);
        var bestVal = result.At<double>(0, 0);
        return (bestVal, -bestVal);
    }
}