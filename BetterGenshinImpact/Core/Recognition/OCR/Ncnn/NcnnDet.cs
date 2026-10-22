using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using BetterGenshinImpact.Core.Recognition.OCR.Engine;
using BetterGenshinImpact.Core.Recognition.OCR.Engine.data;
using BetterGenshinImpact.Core.Recognition.OCR.Paddle;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.OCR.Ncnn;

/// <summary>
///     ncnn Vulkan 后端的检测器（实验性）：输入 pad 到正方形桶（与 Det 的分桶一致），
///     ncnn 输出 [1,1,B,B] 概率图后交给同一套 DbPostProcessor 出文本框。
///     模型按桶边长分桶（320/640/960 各一份 pnnx 转换产物）。
/// </summary>
internal sealed class NcnnDet : IOcrDetector
{
    private static readonly ILogger Logger = App.GetLogger<NcnnDet>();

    private readonly NcnnShim _shim;
    private readonly OcrVersionConfig _config;

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

    private const int BucketStep = 320;

    public NcnnDet(string shimDllPath, string modelDir, OcrVersionConfig config,
        int gpuIndex = 0, int numThreads = 4)
    {
        _config = config;
        _shim = new NcnnShim(shimDllPath);
        // det 输入桶按内容尺寸选择，pnnx 静态形状要求每桶一份模型，故按需懒加载（见 _sessions）
        _modelDir = modelDir;
        _gpuIndex = gpuIndex;
        _numThreads = numThreads;
    }

    private readonly string _modelDir;
    private readonly int _gpuIndex;
    private readonly int _numThreads;
    private readonly Dictionary<int, (nint Net, float[] Buffer)> _sessions = new();

    public void Dispose()
    {
        foreach (var (net, _) in _sessions.Values)
        {
            if (net != nint.Zero) _shim.Destroy(net);
        }

        _sessions.Clear();
        _shim.Dispose();
    }

    public string GetConfigName => "PP-OCRv6 det (ncnn)";

    // 显式接口实现：预热细节不扩大公共 API
    int[] IOcrDetector.GetWarmupBuckets() => GetWarmupBuckets();
    void IOcrDetector.Warmup(int bucket) => Warmup(bucket);
    PaddleOcrDetectionBox[] IOcrDetector.RunBoxes(Mat src) => RunBoxes(src);

    /// <summary>当前配置下预热所有可能的桶（启动时调用）。</summary>
    public void WarmupAll()
    {
        foreach (var bucket in GetWarmupBuckets())
        {
            var (net, _) = GetSession(bucket);
            var input = new float[3 * bucket * bucket];
            _ = RunSession(net, input, bucket, out _, out _);
        }
    }

    /// <summary>用零张量预热指定桶。</summary>
    public void Warmup(int bucket)
    {
        var (net, buffer) = GetSession(bucket);
        var input = new float[3 * bucket * bucket];
        _ = RunSession(net, input, bucket, out var ptr, out _);
        _shim.FreeBuffer(ptr);
    }

    public int[] GetWarmupBuckets()
    {
        var limit = Math.Max(BucketStep, (int)Math.Ceiling(LimitSideLen / (double)BucketStep) * BucketStep);
        var buckets = new List<int>();
        for (var bucket = BucketStep; bucket <= limit; bucket += BucketStep)
        {
            buckets.Add(bucket);
        }

        return buckets.ToArray();
    }

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
        Logger.LogDebug(
            "[OCR][Det][ncnn] 桶{Bucket} 内容{Width}x{Height} | 推理+转换 {Infer:F1}ms 后处理 {Post:F1}ms 文本框 {Count}",
            pred.Width, contentSize.Width, contentSize.Height, inferMs, sw.Elapsed.TotalMilliseconds - inferMs,
            boxes.Length);
        return boxes;
    }

    /// <summary>推理概率图：返回 [1,B,B] 的 float Mat（B 为桶边长）。</summary>
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
            var bucket = canvas.Width;
            var (net, buffer) = GetSession(bucket);
            var tensor = OcrUtils.NormalizeToTensorDnn(canvas, _config.NormalizeImage.Scale,
                _config.NormalizeImage.Mean, _config.NormalizeImage.Std, out var owner);
            try
            {
                if (tensor is not DenseTensor<float> dense)
                    throw new InvalidOperationException("NormalizeToTensorDnn 应返回 DenseTensor<float>");
                dense.Buffer.CopyTo(buffer);
                var rc = RunSession(net, buffer, bucket, out var outPtr, out var outCount);
                if (rc != 0) throw new InvalidOperationException($"ncnn det 推理失败: {rc}");
                try
                {
                    // 输出 [1,1,B,B]：ncnn Mat 为 (w,h,c)，平面存储，直接按行拷到 CV_32FC1
                    var mat = new Mat(bucket, bucket, MatType.CV_32FC1);
                    var span = mat.AsSpan<float>();
                    unsafe
                    {
                        new ReadOnlySpan<float>((float*)outPtr, outCount).CopyTo(span);
                    }

                    return mat;
                }
                finally
                {
                    _shim.FreeBuffer(outPtr);
                }
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

    private int RunSession(nint net, float[] buffer, int bucket, out nint outPtr, out int outCount)
        => _shim.Infer(net, "in0", "out0", buffer, bucket, bucket, 3, out outPtr, out outCount);

    private (nint Net, float[] Buffer) GetSession(int bucket)
    {
        lock (_sessions)
        {
            if (_sessions.TryGetValue(bucket, out var entry)) return entry;
            var paramPath = Path.Combine(_modelDir, $"det_w{bucket}.param");
            var modelPath = Path.Combine(_modelDir, $"det_w{bucket}.bin");
            if (!File.Exists(paramPath) || !File.Exists(modelPath))
                throw new FileNotFoundException($"ncnn det 模型缺失（桶 {bucket}）", paramPath);
            var net = _shim.Create(paramPath, modelPath, _gpuIndex, _numThreads);
            if (net == nint.Zero)
                throw new InvalidOperationException($"ncnn det 会话创建失败（桶 {bucket}）");
            entry = (net, new float[3 * bucket * bucket]);
            _sessions[bucket] = entry;
            return entry;
        }
    }

    /// <summary>与 Det.MatResizeForDetection 完全一致：按 limit 缩放 → 对齐 32 → pad 到正方形桶。</summary>
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

        contentSize = new Size(resizeWidth, resizeHeight);
        var bucket = Math.Max((Math.Max(resizeHeight, resizeWidth) + BucketStep - 1) / BucketStep * BucketStep,
            BucketStep);
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
