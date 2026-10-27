using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR.Engine;
using BetterGenshinImpact.Core.Recognition.OCR.Engine.data;
using BetterGenshinImpact.Core.Recognition.OCR.Paddle;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.Ncnn;

/// <summary>
///     ncnn Vulkan 后端的检测器（实验性）：动态方形画布单模型（det_dyn），
///     输入 pad 到「内容对齐 32 的最小正方形」，ncnn 输出 [1,1,B,B] 概率图后
///     交给同一套 DbPostProcessor 出文本框。
///     shim 由装配方注入并持有生命周期，本类只负责会话与推理。
/// </summary>
internal sealed class NcnnDet : IOcrDetector
{
    private readonly ILogger _logger;

    private readonly INcnnShim _shim;
    private readonly OcrVersionConfig _config;
    private readonly NcnnTuningSettings _tuning;

    /// <summary>det 内容边长上限（与 Det.LimitSideLen 默认值一致）。</summary>
    public int LimitSideLen { get; set; } = 960;

    public int MaxSideLimit { get; set; } = 4000;
    public string LimitType { get; set; } = "max";
    public bool UseDilation { get; set; }
    public float BoxScoreThreshold { get; set; } = 0.6f;
    public float BoxThreshold { get; set; } = 0.3f;
    public int MaxCandidates { get; set; } = 1000;
    public int MinSize { get; set; } = 3;
    public float UnclipRatio { get; set; } = 1.5f;

    private const int AlignStep = 32;

    private const string DynamicParamName = "det_dyn.param";
    private const string DynamicBinName = "det_dyn.bin";

    private readonly string _modelDir;
    private readonly int _gpuIndex;
    private readonly NcnnSession _session;

    /// <summary>动态方形画布上限（构造时按 LimitSideLen 对齐 32）；输入缓冲按此上限分配。</summary>
    private readonly int _maxCanvas;

    public NcnnDet(INcnnShim shim, string modelDir, OcrVersionConfig config,
        NcnnTuningSettings tuning, int gpuIndex = 0, ILogger? logger = null)
    {
        _shim = shim;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        _config = config;
        _tuning = tuning.NormalizedCopy();
        _modelDir = modelDir;
        _gpuIndex = gpuIndex;
        _maxCanvas = Math.Max(64, (int)Math.Ceiling(LimitSideLen / (double)AlignStep) * AlignStep);
        // det 画布恒为正方形：动态方形模型在 Vulkan 上全尺寸对拍通过，构造时直接建会话（产物缺失立即失败）
        _session = CreateSession();
    }

    public void Dispose() => _session.Dispose();

    public string GetConfigName => "PP-OCRv6 det (ncnn)";

    /// <summary>检测文本框（与 Det.RunBoxes 同语义）。</summary>
    public PaddleOcrDetectionBox[] RunBoxes(Mat src)
    {
        var sw = Stopwatch.StartNew();
        using var pred = RunRaw(src, out var contentSize);
        var inferMs = sw.Elapsed.TotalMilliseconds;
        using var roi = pred[0, contentSize.Height, 0, contentSize.Width];
        var postProcessor = new DbPostProcessor(
            BoxThreshold, BoxScoreThreshold, MaxCandidates, UnclipRatio, MinSize, UseDilation);
        var boxes = postProcessor.Run(roi, src.Size());
        _logger.LogDebug(
            "[OCR][Det][ncnn] 画布{Canvas} 内容{Width}x{Height} | 推理+转换 {Infer:F1}ms 后处理 {Post:F1}ms 文本框 {Count}",
            pred.Width, contentSize.Width, contentSize.Height, inferMs, sw.Elapsed.TotalMilliseconds - inferMs,
            boxes.Length);
        return boxes;
    }

    /// <summary>推理概率图：返回 [1,B,B] 的 float Mat（B 为方形画布边长）。</summary>
    public Mat RunRaw(Mat src, out Size contentSize)
    {
        Mat? converted = null;
        var input = src.Channels() switch
        {
            4 => converted = src.CvtColor(ColorConversionCodes.BGRA2BGR),
            1 => converted = src.CvtColor(ColorConversionCodes.GRAY2BGR),
            3 => src,
            var x => throw new Exception($"Unexpect src channel: {x}, allow: (1/3/4)")
        };
        try
        {
            using var canvas = MatResizeForDetection(input, out contentSize);
            var side = canvas.Width;
            var tensor = OcrUtils.NormalizeToTensorDnn(canvas, _config.NormalizeImage.Scale,
                _config.NormalizeImage.Mean, _config.NormalizeImage.Std, out var owner);
            try
            {
                if (tensor is not DenseTensor<float> dense)
                    throw new InvalidOperationException("NormalizeToTensorDnn 应返回 DenseTensor<float>");
                dense.Buffer.CopyTo(_session.InputBuffer);
                var output = _session.Infer(
                    NcnnInput.FromFloat("in0", _session.InputBuffer, side, side, 3));
                var (data, info) = (output.Data, output.Info);
                // 输出 [1,1,B,B]：ncnn Mat 为 (w,h,c)，平面存储，直接按行拷到 CV_32FC1
                var expected = side * side;
                if (data.Length < expected)
                    throw new InvalidOperationException(
                        $"ncnn det 输出维度异常（画布 {side}）：{DescribeShape(info, data.Length)}，期望至少 {expected} 个元素");
                var mat = new Mat(side, side, MatType.CV_32FC1);
                data.AsSpan(0, expected).CopyTo(mat.AsSpan<float>());
                return mat;
            }
            finally
            {
                owner.Dispose();
            }
        }
        finally
        {
            converted?.Dispose();
        }
    }

    private NcnnSession CreateSession()
    {
        var paramPath = Path.Combine(_modelDir, DynamicParamName);
        var modelPath = Path.Combine(_modelDir, DynamicBinName);
        if (!File.Exists(paramPath) || !File.Exists(modelPath))
            throw new FileNotFoundException("ncnn det 动态形状模型缺失（det_dyn.param / det_dyn.bin）", paramPath);
        return new NcnnSession(_shim, _tuning.ToNetOptions(paramPath, modelPath, _gpuIndex),
            _tuning.ParallelSessions, 3 * _maxCanvas * _maxCanvas);
    }

    private static string DescribeShape(NcnnTensorInfo info, int count) =>
        $"dims={info.Dims} {info.ShapeText} elempack={info.ElementPack} elembits={info.ElementBits} count={count}";

    /// <summary>与 Det.MatResizeForDetection 完全一致：按 limit 缩放 → 对齐 32 → pad 到正方形画布。</summary>
    private Mat MatResizeForDetection(Mat src, out Size contentSize)
    {
        using var preprocessed = src.Width + src.Height >= 64 ? null : PrepareTinyImage(src);
        var work = preprocessed ?? src;

        var size = work.Size();
        var height = size.Height;
        var width = size.Width;

        var ratio = CalculateResizeRatio(width, height, LimitSideLen, LimitType);
        var resizeHeight = (int)(height * ratio);
        var resizeWidth = (int)(width * ratio);

        if (Math.Max(resizeHeight, resizeWidth) > MaxSideLimit)
        {
            var maxSideRatio = 1.0 * MaxSideLimit / Math.Max(resizeHeight, resizeWidth);
            resizeHeight = (int)(resizeHeight * maxSideRatio);
            resizeWidth = (int)(resizeWidth * maxSideRatio);
        }

        resizeHeight = Math.Max((int)Math.Round(resizeHeight / 32.0) * 32, 32);
        resizeWidth = Math.Max((int)Math.Round(resizeWidth / 32.0) * 32, 32);

        if (resizeWidth <= 0 || resizeHeight <= 0)
            throw new InvalidOperationException($"Invalid det resize target size: {resizeWidth}x{resizeHeight}");

        // 动态方形画布：内容对齐 32 的最小正方形（上限 _maxCanvas，超限等比缩入）
        var side = Math.Max((Math.Max(resizeHeight, resizeWidth) + AlignStep - 1) / AlignStep * AlignStep, 64);
        if (side > _maxCanvas)
        {
            var fitRatio = 1.0 * _maxCanvas / side;
            resizeHeight = Math.Max((int)Math.Round(resizeHeight * fitRatio / (double)AlignStep) * AlignStep, AlignStep);
            resizeWidth = Math.Max((int)Math.Round(resizeWidth * fitRatio / (double)AlignStep) * AlignStep, AlignStep);
            side = _maxCanvas;
        }

        contentSize = new Size(resizeWidth, resizeHeight);
        var canvas = new Mat(new Size(side, side), work.Type(), Scalar.Black);
        using var resized = work.Resize(new Size(resizeWidth, resizeHeight));
        using (var roi = canvas[new Rect(0, 0, resizeWidth, resizeHeight)])
        {
            resized.CopyTo(roi);
        }

        return canvas;
    }

    private static Mat PrepareTinyImage(Mat src)
    {
        if (src.Width + src.Height >= 64) return src.Clone();
        var newHeight = Math.Max(32, src.Height);
        var newWidth = Math.Max(32, src.Width);
        var padded = new Mat(newHeight, newWidth, src.Type(), Scalar.Black);
        using var roi = padded[new Rect(0, 0, src.Width, src.Height)];
        src.CopyTo(roi);
        return padded;
    }

    private static double CalculateResizeRatio(int width, int height, int limitSideLen, string limitType)
        => limitType switch
        {
            "max" => Math.Max(height, width) > limitSideLen ? 1.0 * limitSideLen / Math.Max(height, width) : 1.0,
            "min" => Math.Min(height, width) < limitSideLen ? 1.0 * limitSideLen / Math.Min(height, width) : 1.0,
            "resize_long" => 1.0 * limitSideLen / Math.Max(height, width),
            _ => throw new ArgumentOutOfRangeException(nameof(limitType), limitType,
                "limitType only supports max/min/resize_long")
        };
}
