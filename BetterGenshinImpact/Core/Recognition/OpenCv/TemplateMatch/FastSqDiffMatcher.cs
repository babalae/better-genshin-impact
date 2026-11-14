using OpenCvSharp;
using System;
using System.Linq;

namespace BetterGenshinImpact.Core.Recognition.OpenCv.TemplateMatch;

public class FastSqDiffMatcher : IDisposable
{
    public readonly Mat[] Source;
    private readonly Mat[] _sourceSq;

    /// <summary>
    /// <see cref="Source"/> 的 CV_32F 版本，仅供 <see cref="MatchCCoeffNormed"/> 使用。
    ///
    /// 必须单独准备：OpenCV 的 matchTemplate 要求 image 与 templ 同类型，
    /// 而 mask 经 PreProcess 归一化后是 CV_32F，无法直接与 CV_8U 的 Source 运算。
    ///
    /// 采用懒加载：局部匹配（高频路径）完全用不到它，只有全局匹配才需要，
    /// 这样不会为全部 366 个图层无条件多占一份 32F 内存。
    /// </summary>
    private Mat[]? _sourceF;

    /// <summary>
    /// <see cref="Source"/> 的平方（CV_32F），同样按需创建，仅供 <see cref="MatchCCoeffNormed"/>。
    /// Σ(S²·m) 是归一化互相关的必需项，若每次匹配都现算，366 个图层的开销会翻倍。
    /// </summary>
    private Mat[]? _sourceSqF;

    /// <summary>
    /// 归一化互相关中，源图窗口方差的下限。低于此值说明窗口内几乎是常数
    /// （纯色背景、全黑、被 UI 完全遮挡），任何模板都能"匹配"上，结果无意义。
    /// </summary>
    private const double MinValidVariance = 1e-3;

    /// <summary>
    /// 归一化互相关分母的下限，仅用于避免除零。
    /// </summary>
    private const double MinValidDenominator = 1e-6;

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
    /// 带遮罩的归一化互相关系数（等价于 <see cref="TemplateMatchModes.CCoeffNormed"/>，但支持遮罩）。
    ///
    /// 为什么必须用这个而不是现有的 SSD：
    /// SSD = Σ(S²·m) - 2Σ(S·T·m) + Σ(T²) 是"能量差"的绝对量，而各图层底色/亮度差异极大
    /// （深色地下城 vs 亮色雪原），Σ(S²·m) 因此天然不同，导致 SSD 的绝对值**跨图层不可比**。
    /// 实测：至冬"运输局东"处正确图层 MapBack_07 的 SSD 在 366 个图层中排第 11 名，
    /// 被分辨率/亮度不同的 City_560 挤掉，最终定位误差达 2140 世界单位。
    ///
    /// 本方法先减去均值再归一化，值域 [-1,1]，跨图层可比。同一批实测中正确图层稳定排第 1，
    /// 与次优的差（gap）最小 0.1269，判别力足够。
    ///
    /// 计算复用已有的 <c>Source</c> / <c>_sourceSq</c>，只额外需要一个 Σ(S·m) 的卷积，
    /// 因此代价约为原 SSD 的 1.5 倍（每图层 9 次而非 6 次 matchTemplate）。
    /// </summary>
    public (Point Loc, double Val) MatchCCoeffNormed(Mat[] maskedTemplates, Mat maskF, int[]? channels = null)
    {
        var source = SelectMatsByIndex(Source, channels);
        var templates = SelectMatsByIndex(maskedTemplates, channels);
        var n = source.Length;
        if (templates.Length != n)
            throw new Exception($"模板图通道数 {templates.Length} 与源图像通道数 {n} 不匹配");

        // 掩码内有效像素数。注意 maskF 已由 PreProcess 归一化到 [0,1]，
        // 因此求和结果就是"有效像素数"（权重为 1 的部分），可直接作为 count 使用。
        var count = Cv2.Sum(maskF).Val0;
        if (count <= 0)
        {
            return (default, -1);
        }

        // 全部转到 CV_32F：OpenCV 的 matchTemplate 要求 image 与 templ 同类型，
        // 而 maskF 是 32F，所以源图与模板都必须升到 32F。
        // 源图的 32F 版本（以及它的平方）按需缓存，避免每次匹配都重算 —— 366 个图层
        // 全量转一次 32F 的代价很高，而这个对象在全局匹配中会被反复使用。
        var sourceF = GetSourceFloat(channels);

        var tplF = new Mat[n];
        try
        {
            for (var i = 0; i < n; i++)
            {
                tplF[i] = new Mat();
                templates[i].ConvertTo(tplF[i], MatType.CV_32F);
            }

            // 模板侧的常数项：ΣT、ΣT²，再得到 varT。
            // 必须**逐通道**求和后各自算方差再累加；把三通道混在一起算会引入通道间交叉项，
            // 方差可能变负、量级也会失真（实测会让正确图层的相关系数排到 366/366）。
            double varT = 0;
            var sumT = new double[n];
            for (var i = 0; i < n; i++)
            {
                sumT[i] = Cv2.Sum(tplF[i]).Val0;
                varT += Cv2.Norm(tplF[i], NormTypes.L2SQR) - sumT[i] * sumT[i] / count;
            }

            if (varT <= MinValidVariance)
            {
                // 模板本身在遮罩内几乎是常数，无从判别。
                return (default, -1);
            }

            // 先跑一次确定结果图尺寸（matchTemplate 输出比源图小 template-1）
            using var probe = new Mat();
            Cv2.MatchTemplate(sourceF[0], tplF[0], probe, TemplateMatchModes.CCorr);
            var resSize = probe.Size();

            using var varS = new Mat(resSize, MatType.CV_32F, Scalar.All(0));
            using var cov = new Mat(resSize, MatType.CV_32F, Scalar.All(0));
            using var s1 = new Mat();   // Σ(S·m)
            using var s2 = new Mat();   // Σ(S²·m)
            using var st = new Mat();   // Σ(S·T·m)
            using var tmp = new Mat();
            var sourceSqF = GetSourceSqFloat();
            var sqSel = SelectMatsByIndex(sourceSqF, channels);
            for (var i = 0; i < n; i++)
            {
                Cv2.MatchTemplate(sourceF[i], maskF, s1, TemplateMatchModes.CCorr);
                Cv2.MatchTemplate(sqSel[i], maskF, s2, TemplateMatchModes.CCorr);
                Cv2.MatchTemplate(sourceF[i], tplF[i], st, TemplateMatchModes.CCorr);

                // varS += Σ(S²m) - (ΣSm)²/c
                Cv2.Multiply(s1, s1, tmp, 1.0 / count);
                Cv2.Subtract(s2, tmp, tmp);
                Cv2.Add(varS, tmp, varS);

                // cov += Σ(STm) - ΣSm·ΣTm/c
                Cv2.AddWeighted(st, 1.0, s1, -sumT[i] / count, 0, tmp);
                Cv2.Add(cov, tmp, cov);
            }

            // den = sqrt(varS · varT)
            using var den = new Mat();
            Cv2.Multiply(varS, varT, den);
            // varS 可能因浮点误差略小于 0，先截断负值再开方
            Cv2.Threshold(den, den, 0, 0, ThresholdTypes.Tozero);
            Cv2.Sqrt(den, den);

            // 源图窗口方差过小（纯色/全黑/被 UI 完全遮挡）时无法判别，统一置为 -1。
            using var invalid = new Mat();
            using var minVarS = new Mat(resSize, MatType.CV_32F, Scalar.All(MinValidVariance));
            using var minDen = new Mat(resSize, MatType.CV_32F, Scalar.All(MinValidDenominator));
            Cv2.Compare(varS, minVarS, invalid, CmpType.LT);
            Cv2.Compare(den, minDen, tmp, CmpType.LT);
            Cv2.BitwiseOr(invalid, tmp, invalid);

            // 避免除零：分母加上极小量；无效位置随后会被覆盖成 -1
            using var eps = new Mat(resSize, MatType.CV_32F, Scalar.All(MinValidDenominator));
            Cv2.Add(den, eps, den);
            using var ncc = new Mat();
            Cv2.Divide(cov, den, ncc);
            ncc.SetTo(Scalar.All(-1), invalid);

            // 理论上 CCOEFF_NORMED ∈ [-1,1]，截断以消除浮点溢出产生的异常大值
            Cv2.Min(ncc, 1.0, ncc);
            Cv2.Max(ncc, -1.0, ncc);

            Cv2.MinMaxLoc(ncc, out _, out var maxVal, out _, out var maxLoc);
            return (maxLoc, maxVal);
        }
        finally
        {
            foreach (var t in tplF)
            {
                t?.Dispose();
            }
        }
    }

    /// <summary>
    /// 取 <see cref="Source"/> 的 CV_32F 版本（懒加载，按需创建）。
    /// </summary>
    private Mat[] GetSourceFloat(int[]? channels)
    {
        if (_sourceF == null)
        {
            lock (this)
            {
                if (_sourceF == null)
                {
                    var arr = new Mat[Source.Length];
                    for (var i = 0; i < Source.Length; i++)
                    {
                        arr[i] = new Mat();
                        Source[i].ConvertTo(arr[i], MatType.CV_32F);
                    }
                    _sourceF = arr;
                }
            }
        }
        return SelectMatsByIndex(_sourceF, channels);
    }

    /// <summary>
    /// 取 <see cref="Source"/> 平方的 CV_32F 版本（懒加载）。
    /// </summary>
    private Mat[] GetSourceSqFloat()
    {
        if (_sourceSqF == null)
        {
            lock (this)
            {
                if (_sourceSqF == null)
                {
                    var srcF = GetSourceFloat(null);
                    var arr = new Mat[srcF.Length];
                    for (var i = 0; i < srcF.Length; i++)
                    {
                        arr[i] = new Mat();
                        Cv2.Multiply(srcF[i], srcF[i], arr[i]);
                    }
                    _sourceSqF = arr;
                }
            }
        }
        return _sourceSqF;
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
        if (_sourceF != null)
        {
            foreach (var img in _sourceF)
            {
                img.Dispose();
            }
            _sourceF = null;
        }
        if (_sourceSqF != null)
        {
            foreach (var img in _sourceSqF)
            {
                img.Dispose();
            }
            _sourceSqF = null;
        }
    }
}