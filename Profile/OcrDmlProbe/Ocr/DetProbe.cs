using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace OcrDmlProbe.Ocr;

/// <summary>与 BetterGI Det.cs 逐行一致的检测器（分桶 + 预热 + 阶段计时），日志改走控制台。</summary>
public class DetProbe : IDisposable
{
    private readonly object _sessionLock = new();
    private readonly InferenceSession _session;
    private readonly OcrVersionConfig _config;
    private readonly Action<string> _log;
    private bool _disposed;

    public int LimitSideLen { get; set; } = 960;
    public int MaxSideLimit { get; set; } = 4000;
    public string LimitType { get; set; } = "max";
    public bool UseDilation { get; set; }
    public float BoxScoreThreshold { get; set; } = 0.6f;
    public float BoxThreshold { get; set; } = 0.3f;
    public int MaxCandidates { get; set; } = 1000;
    public int MinSize { get; set; } = 3;
    public float UnclipRatio { get; set; } = 1.5f;

    public DetProbe(string modelName, string modelPath, OcrVersionConfig config,
        int dmlDeviceId, int cpuThreads, Action<string> log, string providers = "both", bool enableProfiling = false)
    {
        _config = config;
        _log = log;
        _session = DmlSessionFactory.CreateSession(modelPath, dmlDeviceId, cpuThreads, modelName, providers, enableProfiling);
        DmlSessionFactory.Describe(_session, modelName, log);
    }

    /// <summary>结束 ORT 内置 profiler，返回 chrome trace JSON 路径（需创建会话时开启 --profile）。</summary>
    public string EndProfiling() => _session.EndProfiling();

    public void Dispose()
    {
        lock (_sessionLock)
        {
            if (_disposed) return;
            _disposed = true;
            _session.Dispose();
        }
    }

    public PaddleOcrDetectionBox[] RunBoxes(Mat src, out DetTimings timings)
    {
        using var pred = RunRaw(src, out var resizedSize, out var raw);
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
        var postMs = sw.Elapsed.TotalMilliseconds;
        _log?.Invoke($"[OCR][Det] 后处理 {postMs:F1}ms 文本框 {boxes.Length}");
        timings = raw with { PostMs = postMs, Boxes = boxes.Length };
        return boxes;
    }

    public Mat RunRaw(Mat src, out Size resizedSize, out DetTimings timings)
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
            var sw = Stopwatch.StartNew();
            using var resized = MatResizeForDetection(input, out var contentSize);
            resizedSize = contentSize;
            var resizeMs = sw.Elapsed.TotalMilliseconds;
            var inputTensor = OcrUtils.NormalizeToTensorDnn(resized, _config.NormalizeImage.Scale,
                _config.NormalizeImage.Mean, _config.NormalizeImage.Std, out var owner);
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

            var total = sw.Elapsed.TotalMilliseconds;
            timings = new DetTimings(resizeMs, normalizeMs, inferMs,
                total - resizeMs - normalizeMs - inferMs, 0, 0);
            _log?.Invoke(
                $"[OCR][Det] 桶{resized.Width} 内容{contentSize.Width}x{contentSize.Height} | " +
                $"预处理 {resizeMs:F1}ms 张量化 {normalizeMs:F1}ms 推理 {inferMs:F1}ms 输出转换 {timings.OutConvMs:F1}ms");
            return result;
        }
        finally
        {
            converted?.Dispose();
        }
    }

    private const int DetBucketStep = 320;

    public int[] GetWarmupBuckets()
    {
        var limit = Math.Max(DetBucketStep, (int)Math.Ceiling(LimitSideLen / (double)DetBucketStep) * DetBucketStep);
        var buckets = new List<int>();
        for (var bucket = DetBucketStep; bucket <= limit; bucket += DetBucketStep)
        {
            buckets.Add(bucket);
        }

        return buckets.ToArray();
    }

    public void Warmup(int bucket)
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

    /// <summary>纯 session.Run 计时：指定边长的零张量，隔离 EP 单形状执行成本。graphId>=0 时带 gpu_graph_id 回放。</summary>
    public double TimeBucketRun(int bucket, long graphId = -1)
    {
        var input = new DenseTensor<float>(new[] { 1, 3, bucket, bucket });
        lock (_sessionLock)
        {
            var start = Stopwatch.GetTimestamp();
            if (graphId >= 0)
            {
                using var ro = new Microsoft.ML.OnnxRuntime.RunOptions();
                ro.AddRunConfigEntry("gpu_graph_id", graphId.ToString());
                using var results = _session.Run(
                    new[] { NamedOnnxValue.CreateFromTensor(_session.InputNames[0], input) },
                    new[] { _session.OutputNames[0] },
                    ro);
            }
            else
            {
                using var results = _session.Run([
                    NamedOnnxValue.CreateFromTensor(_session.InputNames[0], input)
                ]);
            }
            return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
    }

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
}
