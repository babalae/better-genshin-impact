using OpenCvSharp;
using System;
using System.Linq;

namespace BetterGenshinImpact.Core.Recognition.OpenCv.TemplateMatch;

public class FastSqDiffMatcher : IDisposable
{
    public readonly Mat[] Source;
    private readonly Mat[] _sourceSq;

    /// <summary>
    /// 初始化模板匹配器
    /// </summary>
    /// <param name="source">源图像</param>
    /// <param name="templateSize">模板尺寸</param>
    public FastSqDiffMatcher(Mat source, Size templateSize)
    {
        if (source.Empty())
            throw new Exception("源图像为空");
        if (templateSize.Width > source.Width || templateSize.Height > source.Height)
            throw new Exception("模板图尺寸超过源图片尺寸");

        Source = source.Split();

        using Mat sourceF = new Mat();
        source.ConvertTo(sourceF, MatType.CV_32F);
        Cv2.Multiply(sourceF, sourceF, sourceF);
        _sourceSq = sourceF.Split();
    }

    /// <summary>
    /// 执行模板匹配
    /// </summary>
    /// <param name="maskedTemplates">模板图像</param>
    /// <param name="maskF">遮罩图像</param>
    /// <returns>最佳匹配位置</returns> (Point, double)
    public (Point Loc, double Val) Match(Mat[] maskedTemplates, Mat maskF)
    {
        return Match(Source, _sourceSq, maskedTemplates, maskF);
    }
    
    public (Point Loc, double Val) Match(Mat[] maskedTemplates, Mat maskF, Rect rect, int[]? channels = null)
    {
        var sourceRoi = SelectMatsByIndex(GetRegionViews(Source, rect), channels);
        var sourceSqRoi = SelectMatsByIndex(GetRegionViews(_sourceSq, rect), channels);
        var maskedTemplatesSelect = SelectMatsByIndex(maskedTemplates, channels);
        return Match(sourceRoi, sourceSqRoi, maskedTemplatesSelect, maskF);
    }

    private (Point Loc, double Val) Match(Mat[] source, Mat[] sourceSq, Mat[] maskedTemplates, Mat maskF)
    {
        var n = source.Length;
        if (maskedTemplates.Length != n)
            throw new Exception($"模板图通道数 {maskedTemplates.Length} 与源图像通道数 {n} 不匹配");

        // 计算互相关图
        using var crossCorr = new Mat();
        using var temp = new Mat();
        Cv2.MatchTemplate(source[0], maskedTemplates[0], crossCorr, TemplateMatchModes.CCorr);
        for (var i = 1; i < n; i++)
        {
            Cv2.MatchTemplate(source[i], maskedTemplates[i], temp, TemplateMatchModes.CCorr);
            Cv2.Add(crossCorr, temp, crossCorr);
        }

        Cv2.Multiply(crossCorr, -2, crossCorr);
        // 计算源图像与遮罩的加权平方图
        for (var i = 0; i < n; i++)
        {
            Cv2.MatchTemplate(sourceSq[i], maskF, temp, TemplateMatchModes.CCorr);
            Cv2.Add(crossCorr, temp, crossCorr);
        }
        Cv2.MinMaxLoc(crossCorr, out var minVal, out _, out var minLoc, out _);
        return (minLoc, minVal);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="template">模板图</param>
    /// <param name="mask">遮罩, 类型为 8UC1, 且尺寸与 template 相同</param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static (Mat[] maskedTemplates, Mat maskF) PreProcess(Mat template, Mat mask)
    {
        if (mask.Type() != MatType.CV_8UC1)
            throw new Exception("遮罩格式不对");
        if (template.Size() != mask.Size())
            throw new Exception("模板图与遮罩尺寸不匹配");

        var maskF = new Mat();
        mask.ConvertTo(maskF, MatType.CV_32F);
        Cv2.Normalize(maskF, maskF, 0, 1, NormTypes.MinMax);

        using var maskedTemplate = new Mat(template.Size(), template.Type(), Scalar.All(0));
        Cv2.BitwiseAnd(template, template, maskedTemplate, mask);

        var maskedTemplates = maskedTemplate.Split();
        
        return (maskedTemplates, maskF);
    }

    public static double GetTplSumSq(Mat[] maskedTemplates, int[]? channels = null)
    {
        return SelectMatsByIndex(maskedTemplates, channels).Sum(maskedTpl => Cv2.Norm(maskedTpl, NormTypes.L2SQR));
    }
    
    public static Mat[] SelectMatsByIndex(Mat[] matArray, int[]? channels = null)
    {
        return channels?.Where(index => index >= 0 && index < matArray.Length) 
            .Select(index => matArray[index])
            .ToArray()
            ?? matArray;
    }
    
    public static Mat[] GetRegionViews(Mat[] images, Rect rect)
    {
        return images.Select(img => img.SubMat(rect)).ToArray();
    }

    public void Dispose()
    {
        foreach (var img in Source)
        {
            img.Dispose();
        }
        foreach (var img in _sourceSq)
        {
            img.Dispose();
        }
    }
}