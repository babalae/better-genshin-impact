using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR.Engine;
using BetterGenshinImpact.Core.Recognition.OCR.Engine.data;
using BetterGenshinImpact.Core.Recognition.OCR.Paddle;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.Ncnn;

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

    private const int BucketStep = 320;

    public NcnnDet(string shimDllPath, string modelDir, OcrVersionConfig config,
        NcnnTuningSettings tuning, int gpuIndex = 0)
    {
        _config = config;
        _tuning = tuning.NormalizedCopy();
        _shim = new NcnnShim(shimDllPath);
        // det 输入桶按内容尺寸选择，pnnx 静态形状要求每桶一份模型，故按需懒加载（见 _sessions）
        _modelDir = modelDir;
        _gpuIndex = gpuIndex;
    }

    private readonly string _modelDir;
    private readonly int _gpuIndex;
    private readonly Dictionary<int, SessionEntry> _sessions = new();
    private readonly object _sessionLock = new();

    /// <summary>单个桶的会话池与缓存下来的 blob 名（每个桶模型独立，首次推理时解析）。</summary>
    private sealed class SessionEntry(NcnnSessionPool pool, float[] buffer)
    {
        public NcnnSessionPool Pool { get; } = pool;
        public float[] Buffer { get; } = buffer;
        public string InputName { get; set; } = "in0";
        public string OutputName { get; set; } = "out0";
        public bool BlobsResolved { get; set; }
    }

    public void Dispose()
    {
        lock (_sessionLock)
        {
            foreach (var entry in _sessions.Values) entry.Pool.Dispose();
            _sessions.Clear();
        }

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
            Warmup(bucket);
        }
    }

    /// <summary>用零张量预热指定桶。</summary>
    public void Warmup(int bucket)
    {
        var entry = GetSession(bucket);
        _ = RunSession(entry, new float[3 * bucket * bucket], bucket);
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
            var entry = GetSession(bucket);
            var tensor = OcrUtils.NormalizeToTensorDnn(canvas, _config.NormalizeImage.Scale,
                _config.NormalizeImage.Mean, _config.NormalizeImage.Std, out var owner);
            try
            {
                if (tensor is not DenseTensor<float> dense)
                    throw new InvalidOperationException("NormalizeToTensorDnn 应返回 DenseTensor<float>");
                dense.Buffer.CopyTo(entry.Buffer);
                var (data, info) = RunSession(entry, entry.Buffer, bucket);
                // 输出 [1,1,B,B]：ncnn Mat 为 (w,h,c)，平面存储，直接按行拷到 CV_32FC1
                var expected = bucket * bucket;
                if (data.Length < expected)
                    throw new InvalidOperationException(
                        $"ncnn det 输出维度异常（桶 {bucket}）：{DescribeShape(info, data.Length)}，期望至少 {expected} 个元素");
                var mat = new Mat(bucket, bucket, MatType.CV_32FC1);
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

    /// <summary>单次推理：返回带元信息的紧凑 fp32。</summary>
    private (float[] Data, NcnnTensorInfo Info) RunSession(SessionEntry entry, float[] buffer, int bucket)
        => entry.Pool.Use(net =>
        {
            if (!entry.BlobsResolved) ResolveBlobs(entry, net);
            var outputs = _shim.InferEx(net,
                [NcnnInput.FromFloat(entry.InputName, buffer, bucket, bucket, 3)],
                [entry.OutputName]);
            if (outputs.Count == 0)
                throw new InvalidOperationException($"ncnn det 未返回输出（桶 {bucket}）");
            return (outputs[0].Data, outputs[0].Info);
        });

    /// <summary>从模型自身读取 blob 名；探测不到时保留默认的 in0 / out0。</summary>
    private void ResolveBlobs(SessionEntry entry, nint net)
    {
        var inputs = _shim.QueryInputBlobs(net);
        var outputs = _shim.QueryOutputBlobs(net);
        if (inputs.Length > 0 && !string.IsNullOrWhiteSpace(inputs[0])) entry.InputName = inputs[0];
        if (outputs.Length > 0 && !string.IsNullOrWhiteSpace(outputs[0])) entry.OutputName = outputs[0];
        entry.BlobsResolved = true;
    }

    private static string DescribeShape(NcnnTensorInfo info, int count) =>
        $"dims={info.Dims} {info.ShapeText} elempack={info.ElementPack} elembits={info.ElementBits} count={count}";

    private SessionEntry GetSession(int bucket)
    {
        lock (_sessionLock)
        {
            if (_sessions.TryGetValue(bucket, out var entry)) return entry;
            var paramPath = Path.Combine(_modelDir, $"det_w{bucket}.param");
            var modelPath = Path.Combine(_modelDir, $"det_w{bucket}.bin");
            if (!File.Exists(paramPath) || !File.Exists(modelPath))
                throw new FileNotFoundException($"ncnn det 模型缺失（桶 {bucket}）", paramPath);
            var options = _tuning.ToNetOptions(paramPath, modelPath, _gpuIndex);
            var pool = new NcnnSessionPool(_shim, options, _tuning.ParallelSessions);
            entry = new SessionEntry(pool, new float[3 * bucket * bucket]);
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
