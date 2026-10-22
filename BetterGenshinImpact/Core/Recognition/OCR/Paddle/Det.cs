using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BetterGenshinImpact.Core.Recognition.OCR.Engine;
using BetterGenshinImpact.Core.Recognition.ONNX;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.OCR.Paddle;

public class Det(BgiOnnxModel model, OcrVersionConfig config, BgiOnnxSessionFactory bgiOnnxFactory)
    : IOcrDetector
{
    private static readonly ILogger Logger = App.GetLogger<Det>();

    private readonly object _sessionLock = new();
    private readonly InferenceSession _session = bgiOnnxFactory.CreateInferenceSession(model, true);
    private bool _disposed;

    /// <summary>Gets or sets the detection side length limit used by the Python det preprocess.</summary>
    public int LimitSideLen { get; set; } = 960;

    /// <summary>Gets or sets the maximum size limit after resizing.</summary>
    public int MaxSideLimit { get; set; } = 4000;

    /// <summary>Gets or sets the side length limit type. Supports max/min/resize_long.</summary>
    public string LimitType { get; set; } = "max";

    /// <summary>Gets or sets whether the official 2x2 dilation is enabled during post-processing.</summary>
    public bool UseDilation { get; set; }

    /// <summary>Gets or sets the score threshold for filtering out possible text boxes.</summary>
    public float BoxScoreThreshold { get; set; } = 0.6f;

    /// <summary>Gets or sets the threshold to binarize the text region.</summary>
    public float BoxThreshold { get; set; } = 0.3f;

    /// <summary>Gets or sets the maximum number of contours processed by DBPostProcess.</summary>
    public int MaxCandidates { get; set; } = 1000;

    /// <summary>Gets or sets the minimum size of the text boxes to be considered as valid.</summary>
    public int MinSize { get; set; } = 3;

    /// <summary>Gets or sets the ratio for enlarging text boxes during post-processing.</summary>
    public float UnclipRatio { get; set; } = 1.5f;

    public void Dispose()
    {
        lock (_sessionLock)
        {
            if (_disposed) return;
            _disposed = true;
            _session.Dispose();
        }
    }

    public RotatedRect[] Run(Mat src)
    {
        return RunBoxes(src).Select(box => box.Rect).ToArray();
    }

    internal PaddleOcrDetectionBox[] RunBoxes(Mat src)
    {
        using var pred = RunRaw(src, out var resizedSize);
        //OpenCvSharp.OpenCVException: 0 <= _colRange.start && _colRange.start <= _colRange.end && _colRange.end <= m.cols
        using var roi = pred[0, resizedSize.Height, 0, resizedSize.Width];
        var postProcessor = new DbPostProcessor(
            BoxThreshold,
            BoxScoreThreshold,
            MaxCandidates,
            UnclipRatio,
            MinSize,
            UseDilation);
        var sw = Stopwatch.StartNew();
        var boxes = postProcessor.Run(roi, src.Size());
        Logger.LogDebug("[OCR][Det] 后处理 {Ms:F1}ms 文本框 {Count}", sw.Elapsed.TotalMilliseconds, boxes.Length);
        return boxes;
    }

    public Mat RunRaw(Mat src, out Size resizedSize)
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
            // 与 PaddleOCR Python DetResizeForTest 保持一致：
            // 1. h + w < 64 时先将图像补全到至少 32x32
            // 2. 按 limit_type 规则缩放，并将宽高分别对齐到 32 的倍数
            // 3. 再 pad 到固定档位的正方形画布，使模型输入形状有限（OpenVINO 会按形状编译，形状无限则每次检测都可能触发重新编译）
            var sw = Stopwatch.StartNew();
            using var resized = MatResizeForDetection(input, out var contentSize);
            resizedSize = contentSize;
            var resizeMs = sw.Elapsed.TotalMilliseconds;
            var inputTensor = OcrUtils.NormalizeToTensorDnn(resized, config.NormalizeImage.Scale,
                config.NormalizeImage.Mean, config.NormalizeImage.Std, out var owner);
            var normalizeMs = sw.Elapsed.TotalMilliseconds - resizeMs;
            Mat result;
            double inferMs;
            using (owner)
            {
                lock (_sessionLock)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _session.Run([
                        NamedOnnxValue.CreateFromTensor(_session.InputNames[0], inputTensor)
                    ]);
                    inferMs = sw.Elapsed.TotalMilliseconds - resizeMs - normalizeMs;
                    var output = results[0];
                    if (output.ElementType is not TensorElementType.Float)
                        throw new Exception($"Unexpected output tensor type: {output.ElementType}");

                    if (output.ValueType is not OnnxValueType.ONNX_TYPE_TENSOR)
                        throw new Exception($"Unexpected output tensor value type: {output.ValueType}");
                    var outputTensor = output.AsTensor<float>();
                    result = OcrUtils.Tensor2Mat(outputTensor);
                    // 因为一个已知bug,tensor中内存在dml下使用完后会被释放掉,锁之外的代码会报错
                }
            }

            Logger.LogDebug(
                "[OCR][Det] 桶{Bucket} 内容{Width}x{Height} | 预处理 {Resize:F1}ms 张量化 {Norm:F1}ms 推理 {Infer:F1}ms 输出转换 {Out:F1}ms",
                resized.Width, contentSize.Width, contentSize.Height,
                resizeMs, normalizeMs, inferMs, sw.Elapsed.TotalMilliseconds - resizeMs - normalizeMs - inferMs);
            return result;
        }
        finally
        {
            converted?.Dispose();
        }
    }

    /// <summary>
    ///     检测输入形状分桶的档位步长：内容区域 pad 到 320 的倍数的正方形画布，
    ///     使模型输入形状只有有限几档（默认 LimitSideLen=960 时为 320/640/960 三档），
    ///     避免 OpenVINO 等按输入形状编译的执行提供程序反复触发模型重编译。
    /// </summary>
    private const int DetBucketStep = 320;

    /// <summary>当前配置下可能出现的检测输入档位，供启动时预热编译。</summary>
    internal int[] GetWarmupBuckets()
    {
        var limit = Math.Max(DetBucketStep, (int)Math.Ceiling(LimitSideLen / (double)DetBucketStep) * DetBucketStep);
        var buckets = new List<int>();
        for (var bucket = DetBucketStep; bucket <= limit; bucket += DetBucketStep)
        {
            buckets.Add(bucket);
        }

        return buckets.ToArray();
    }

    /// <summary>用零张量预热指定档位，提前触发该形状的底层编译（OpenVINO 按输入形状编译，首次耗时数秒）。</summary>
    internal void Warmup(int bucket)
    {
        var input = new DenseTensor<float>(new[] { 1, 3, bucket, bucket });
        lock (_sessionLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var results = _session.Run([
                NamedOnnxValue.CreateFromTensor(_session.InputNames[0], input)
            ]);
        }
    }

    /// <summary>
    ///     按 PaddleOCR Python 的 DetResizeForTest 缩放图像：
    ///     超小图先补边，再按 max/min/resize_long 规则缩放，并对齐到 32 的倍数，
    ///     最后放入黑底正方形画布（边长为 320 的倍数）。
    /// </summary>
    /// <param name="src">原始图像</param>
    /// <param name="contentSize">缩放后内容区域的实际尺寸，推理输出只需按此区域截取</param>
    /// <returns>已 pad 到分桶画布的图像</returns>
    private Mat MatResizeForDetection(Mat src, out Size contentSize)
    {
        // 正常尺寸图像只做只读操作，直接使用原 Mat，避免整帧克隆；
        // 仅极小图需要复制补边。
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
        {
            throw new InvalidOperationException(
                $"Invalid det resize target size: {resizeWidth}x{resizeHeight}, src={width}x{height}");
        }

        contentSize = new Size(resizeWidth, resizeHeight);
        var bucket = Math.Max((Math.Max(resizeHeight, resizeWidth) + DetBucketStep - 1) / DetBucketStep * DetBucketStep,
            DetBucketStep);
        var canvas = new Mat(new Size(bucket, bucket), work.Type(), Scalar.Black);
        using var resized = work.Resize(new Size(resizeWidth, resizeHeight));
        using (var roi = canvas[new Rect(0, 0, resizeWidth, resizeHeight)])
        {
            resized.CopyTo(roi);
        }

        return canvas;
    }

    private static Mat PrepareTinyImage(Mat src)
    {
        if (src.Width + src.Height >= 64)
        {
            return src.Clone();
        }

        var newHeight = Math.Max(32, src.Height);
        var newWidth = Math.Max(32, src.Width);
        var padded = new Mat(newHeight, newWidth, src.Type(), Scalar.Black);
        using var roi = padded[new Rect(0, 0, src.Width, src.Height)];
        src.CopyTo(roi);
        return padded;
    }

    private static double CalculateResizeRatio(int width, int height, int limitSideLen, string limitType)
    {
        return limitType switch
        {
            "max" => Math.Max(height, width) > limitSideLen
                ? 1.0 * limitSideLen / Math.Max(height, width)
                : 1.0,
            "min" => Math.Min(height, width) < limitSideLen
                ? 1.0 * limitSideLen / Math.Min(height, width)
                : 1.0,
            "resize_long" => 1.0 * limitSideLen / Math.Max(height, width),
            _ => throw new ArgumentOutOfRangeException(nameof(limitType), limitType,
                "limitType only supports max/min/resize_long")
        };
    }

    // 显式接口实现：预热与分桶属于内部细节，不扩大公共 API
    PaddleOcrDetectionBox[] IOcrDetector.RunBoxes(Mat src) => RunBoxes(src);
    int[] IOcrDetector.GetWarmupBuckets() => GetWarmupBuckets();
    void IOcrDetector.Warmup(int bucket) => Warmup(bucket);

    public string GetConfigName => config.Name;
}
