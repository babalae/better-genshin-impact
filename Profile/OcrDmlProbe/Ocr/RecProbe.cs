using System.Buffers;
using System.Diagnostics;
using System.Text;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace OcrDmlProbe.Ocr;

/// <summary>与 BetterGI Rec.cs 逐行一致的识别器（160 步长分桶 + 预热 + 阶段计时），日志改走控制台。</summary>
public class RecProbe : IDisposable
{
    private readonly IReadOnlyList<string> _labels;
    private readonly object _sessionLock = new();
    private readonly InferenceSession _session;
    private readonly OcrVersionConfig _config;
    private readonly Action<string> _log;
    private bool _disposed;

    private const int RecBucketStep = 160;
    private const int RecWarmupMaxWidth = 960;
    private const int RecStackBatch = 8;

    private readonly bool _stackedEnabled;
    private readonly string _modelPath;
    private readonly int _dmlDeviceId;
    private InferenceSession? _pinnedSession;

    public RecProbe(string modelName, string modelPath, IReadOnlyList<string> labels, OcrVersionConfig config,
        int dmlDeviceId, int cpuThreads, Action<string> log, string providers = "both", bool enableProfiling = false)
    {
        _labels = labels;
        _modelPath = modelPath;
        _dmlDeviceId = dmlDeviceId;
        _stackedEnabled = providers != "cpu";
        _config = config;
        _log = log;
        _session = DmlSessionFactory.CreateSession(modelPath, dmlDeviceId, cpuThreads, modelName, providers, enableProfiling);
        DmlSessionFactory.Describe(_session, modelName, log);
    }

    /// <summary>结束 ORT 内置 profiler，返回 chrome trace JSON 路径（需创建会话时开启 --profile）。</summary>
    public string EndProfiling() => _session.EndProfiling();

    /// <summary>无锁版单档 Run：仅用于并发吞吐实验（ORT InferenceSession.Run 线程安全）。</summary>
    public double TimeBucketRunUnlocked(int bucketWidth)
    {
        var input = new DenseTensor<float>(new[] { 1, 3, _config.Shape.Height, bucketWidth });
        var start = Stopwatch.GetTimestamp();
        using var results = _session.Run([
            NamedOnnxValue.CreateFromTensor(_session.InputNames[0], input)
        ]);
        return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    public void Dispose()
    {
        lock (_sessionLock)
        {
            if (_disposed) return;
            _disposed = true;
            _pinnedSession?.Dispose();
            _session.Dispose();
        }
    }

    public (int Batch, int Width)[] GetWarmupBuckets()
    {
        var batches = _stackedEnabled ? new[] { 1, RecStackBatch } : new[] { 1 };
        var buckets = new List<(int Batch, int Width)>();
        foreach (var batch in batches)
        {
            for (var width = RecBucketStep; width <= RecWarmupMaxWidth; width += RecBucketStep)
            {
                buckets.Add((batch, width));
            }
        }

        return buckets.ToArray();
    }

    public void Warmup(int batch, int width)
    {
        var input = new DenseTensor<float>(new[] { batch, 3, _config.Shape.Height, width });
        lock (_sessionLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var results = _session.Run([
                NamedOnnxValue.CreateFromTensor(_session.InputNames[0], input)
            ]);
        }
    }

    /// <summary>创建宽度钉死的独立会话（AddFreeDimensionOverrideByName），验证静态形状是否消除每 Run 的 MetaCommand 重建。</summary>
    public void CreatePinnedSession(int width)
    {
        _pinnedSession?.Dispose();
        var options = new SessionOptions();
        options.EnableMemoryPattern = false;
        options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
        options.AddSessionConfigEntry("ep.dml.enable_cpu_sync_spinning", "true");
        options.AddFreeDimensionOverrideByName("DynamicDimension.0", 1);
        options.AddFreeDimensionOverrideByName("DynamicDimension.1", width);
        options.AppendExecutionProvider_DML(_dmlDeviceId);
        options.AppendExecutionProvider_CPU();
        _pinnedSession = new InferenceSession(_modelPath, options);
    }

    public double TimeBucketRunPinnedSession(int bucketWidth, int batch = 1)
    {
        var input = new DenseTensor<float>(new[] { batch, 3, _config.Shape.Height, bucketWidth });
        lock (_sessionLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var start = Stopwatch.GetTimestamp();
            using var results = _pinnedSession!.Run([
                NamedOnnxValue.CreateFromTensor(_pinnedSession.InputNames[0], input)
            ]);
            return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
    }

    /// <summary>纯 session.Run 计时：指定批/宽的零张量，隔离 EP 单形状执行成本。graphId>=0 时带 gpu_graph_id 回放。</summary>
    public double TimeBucketRun(int bucketWidth, int batch = 1, long graphId = -1)
    {
        var input = new DenseTensor<float>(new[] { batch, 3, _config.Shape.Height, bucketWidth });
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

    /// <summary>
    /// 真·批量对照：把多张图堆叠成 [N,3,H,W] 一次 Run（生产 RunMulti 是每张各跑一次 [1,3,H,W]）。
    /// 返回单次堆叠 Run 耗时；若明显低于 N×单张耗时，说明 DML 每次 Run 的固定开销是主要成本。
    /// </summary>
    public double TimeStackedBatch(Mat[] srcs)
    {
        if (srcs.Length == 0) return 0;
        var modelHeight = _config.Shape.Height;
        var maxWidth = (int)Math.Ceiling(srcs.Max(src =>
        {
            var size = src.Size();
            return 1.0 * size.Width / size.Height * modelHeight;
        }));
        maxWidth = (int)Math.Ceiling(maxWidth / (double)RecBucketStep) * RecBucketStep;

        // 逐张预处理到相同档宽后沿 batch 维堆叠
        var tensors = new Tensor<float>[srcs.Length];
        try
        {
            for (var i = 0; i < srcs.Length; i++)
            {
                tensors[i] = OcrUtils.ResizeNormImg(srcs[i], new OcrShape(3, maxWidth, modelHeight), out var owner);
                lock (owners_)
                {
                    owners_.Add(owner);
                }
            }

            var batch = new DenseTensor<float>(new[] { srcs.Length, 3, modelHeight, maxWidth });
            for (var i = 0; i < srcs.Length; i++)
            {
                if (tensors[i] is DenseTensor<float> dense)
                {
                    dense.Buffer.CopyTo(batch.Buffer.Slice(i * dense.Buffer.Length));
                }
            }

            lock (_sessionLock)
            {
                var start = Stopwatch.GetTimestamp();
                using var results = _session.Run([
                    NamedOnnxValue.CreateFromTensor(_session.InputNames[0], batch)
                ]);
                var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                _log?.Invoke($"[批量对照] N={srcs.Length} 单次堆叠 Run [{srcs.Length},3,{modelHeight},{maxWidth}] = {ms:F1}ms");
                return ms;
            }
        }
        finally
        {
            lock (owners_)
            {
                foreach (var o in owners_) o.Dispose();
                owners_.Clear();
            }
        }
    }

    private readonly List<IMemoryOwner<float>> owners_ = [];

    public OcrRecognizerResult[] Run(Mat[] srcs, out RecTimings totals, int batchSize = 0)
    {
        if (srcs.Length == 0)
        {
            totals = new RecTimings(0, 0, 0, 0, 0);
            return [];
        }

        var chooseBatchSize = batchSize != 0 ? batchSize : Math.Min(8, Environment.ProcessorCount);

        var preMs = 0d;
        var inferMs = 0d;
        var decodeMs = 0d;
        var lastBucket = 0;
        var lines = 0;

        var results = srcs
            .Select((x, i) => (mat: x, i))
            .OrderBy(x => x.mat.Width)
            .Chunk(chooseBatchSize)
            .SelectMany(x =>
            {
                var multi = RunMulti(x.Select(x1 => x1.mat).ToArray(), out var t);
                preMs += t.PreMs;
                inferMs += t.InferMs;
                decodeMs += t.DecodeMs;
                lastBucket = t.BucketWidth;
                lines += t.Lines;
                return multi.Select((r, idx) => (r, id: x[idx].i));
            })
            .OrderBy(x => x.id)
            .Select(x => x.r)
            .ToArray();

        totals = new RecTimings(preMs, inferMs, decodeMs, lines, lastBucket);
        return results;
    }

    public OcrRecognizerResult Run(Mat src)
    {
        return RunMulti([src], out _).Single();
    }

    private OcrRecognizerResult[] RunMulti(Mat[] srcs, out RecTimings timings)
    {
        if (srcs.Length == 0)
        {
            timings = new RecTimings(0, 0, 0, 0, 0);
            return [];
        }

        for (var i = 0; i < srcs.Length; ++i)
        {
            var src = srcs[i];
            if (src.Empty())
                throw new ArgumentException($"src[{i}] size should not be 0, wrong input picture provided?");
        }

        var modelHeight = _config.Shape.Height;
        var maxWidth = (int)Math.Ceiling(srcs.Max(src =>
        {
            var size = src.Size();
            return 1.0 * size.Width / size.Height * modelHeight;
        }));
        // 形状分桶：批宽向上取整到固定档位，pad 区域为零、CTC 解码天然截断，不影响识别结果
        maxWidth = (int)Math.Ceiling(maxWidth / (double)RecBucketStep) * RecBucketStep;
        // DML：多行堆叠成固定批一次 Run，把每次 Run 的 CPU 派发地板摊薄；补零行输出空白，解码时按实际行数截断
        var stacked = _stackedEnabled && srcs.Length > 1;
        List<IMemoryOwner<float>> owners = [];
        var preprocessMs = 0d;
        var inferMs = 0d;
        var decodeMs = 0d;
        OcrRecognizerResult[] decodeResults;
        try
        {
            if (stacked)
            {
                var tensors = new Tensor<float>[srcs.Length];
                for (var i = 0; i < srcs.Length; i++)
                {
                    Mat? channel3 = default;
                    try
                    {
                        var src = srcs[i];
                        channel3 = src.Channels() switch
                        {
                            4 => src.CvtColor(ColorConversionCodes.BGRA2BGR),
                            1 => src.CvtColor(ColorConversionCodes.GRAY2BGR),
                            3 => src,
                            var x => throw new Exception($"Unexpect src channel: {x}, allow: (1/3/4)")
                        };
                        var sw = Stopwatch.StartNew();
                        tensors[i] = OcrUtils.ResizeNormImg(channel3, new OcrShape(3, maxWidth, modelHeight),
                            out var owner);
                        preprocessMs += sw.Elapsed.TotalMilliseconds;
                        owners.Add(owner);
                    }
                    finally
                    {
                        if (channel3 != null && !ReferenceEquals(channel3, srcs[i]))
                        {
                            channel3.Dispose();
                        }
                    }
                }

                lock (_sessionLock)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    var sw = Stopwatch.StartNew();
                    var inputTensor = BuildStackedBatchTensor(tensors, RecStackBatch, modelHeight, maxWidth);
                    using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _session.Run([
                        NamedOnnxValue.CreateFromTensor(_session.InputNames[0], inputTensor)
                    ]);
                    var output = results[0];
                    if (output.ElementType is not TensorElementType.Float)
                        throw new Exception($"Unexpected output tensor type: {output.ElementType}");

                    if (output.ValueType is not OnnxValueType.ONNX_TYPE_TENSOR)
                        throw new Exception($"Unexpected output tensor value type: {output.ValueType}");
                    var tensor = output.AsTensor<float>();
                    inferMs = sw.Elapsed.TotalMilliseconds;
                    // DML tensor 内存在 Run 返回后释放，解码在锁内直接读 buffer
                    var decodeStopwatch = Stopwatch.StartNew();
                    decodeResults = DecodeStackedTensor(tensor, srcs.Length);
                    decodeMs = decodeStopwatch.Elapsed.TotalMilliseconds;
                }
            }
            else
            {
                var resultTensors = srcs
                    .Select(src =>
                    {
                        Mat? channel3 = default;
                        try
                        {
                            channel3 = src.Channels() switch
                            {
                                4 => src.CvtColor(ColorConversionCodes.BGRA2BGR),
                                1 => src.CvtColor(ColorConversionCodes.GRAY2BGR),
                                3 => src,
                                var x => throw new Exception($"Unexpect src channel: {x}, allow: (1/3/4)")
                            };
                            var sw = Stopwatch.StartNew();
                            var result = OcrUtils.ResizeNormImg(channel3, new OcrShape(3, maxWidth, modelHeight),
                                out var owner);
                            preprocessMs += sw.Elapsed.TotalMilliseconds;
                            lock (owners)
                            {
                                owners.Add(owner);
                            }

                            return result;
                        }
                        finally
                        {
                            if (channel3 != null && !ReferenceEquals(channel3, src))
                            {
                                channel3.Dispose();
                            }
                        }
                    })
                    .Select(inputTensor =>
                        {
                            lock (_sessionLock)
                            {
                                ObjectDisposedException.ThrowIf(_disposed, this);
                                var sw = Stopwatch.StartNew();
                                using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _session.Run([
                                    NamedOnnxValue.CreateFromTensor(_session.InputNames[0], inputTensor)
                                ]);
                                var output = results[0];
                                if (output.ElementType is not TensorElementType.Float)
                                    throw new Exception($"Unexpected output tensor type: {output.ElementType}");

                                if (output.ValueType is not OnnxValueType.ONNX_TYPE_TENSOR)
                                    throw new Exception($"Unexpected output tensor value type: {output.ValueType}");
                                var tensor = output.AsTensor<float>();
                                var data = tensor.ToArray();
                                inferMs += sw.Elapsed.TotalMilliseconds;
                                return (tensor.Dimensions.ToArray(), data);
                            }
                        }
                    ).ToArray();

                var decodeStopwatch = Stopwatch.StartNew();
                decodeResults = resultTensors
                    .SelectMany(resultTensor =>
                    {
                        var resultArray = resultTensor.Item2;
                        var resultShape = resultTensor.Item1;
                        var labelCount = resultShape[2];
                        var charCount = resultShape[1];
                        return Enumerable.Range(0, resultShape[0])
                            .Select(i => DecodeOne(resultArray, i * charCount * labelCount, charCount, labelCount));
                    }).ToArray();
                decodeMs = decodeStopwatch.Elapsed.TotalMilliseconds;
            }
        }
        finally
        {
            owners.ForEach(x => { x.Dispose(); });
        }

        timings = new RecTimings(preprocessMs, inferMs, decodeMs, srcs.Length, maxWidth);
        _log?.Invoke(stacked
            ? $"[OCR][Rec] 桶宽{maxWidth} 行数{srcs.Length} 堆叠批{RecStackBatch} | 预处理 {preprocessMs:F1}ms 推理 {inferMs:F1}ms(均 {inferMs / Math.Max(1, srcs.Length):F1}ms/行) 解码 {decodeMs:F1}ms"
            : $"[OCR][Rec] 桶宽{maxWidth} 行数{srcs.Length} | 预处理 {preprocessMs:F1}ms 推理 {inferMs:F1}ms(均 {inferMs / Math.Max(1, srcs.Length):F1}ms/行) 解码 {decodeMs:F1}ms");
        return decodeResults;
    }

    private static DenseTensor<float> BuildStackedBatchTensor(Tensor<float>[] tensors, int batchCount, int height,
        int width)
    {
        if (tensors.Length == 1 && batchCount == 1)
        {
            return (DenseTensor<float>)tensors[0];
        }

        var batch = new DenseTensor<float>(new[] { batchCount, 3, height, width });
        for (var i = 0; i < tensors.Length; i++)
        {
            if (tensors[i] is not DenseTensor<float> dense)
                throw new InvalidOperationException("堆叠批要求预处理输出为 DenseTensor<float>");
            dense.Buffer.CopyTo(batch.Buffer.Slice(i * dense.Buffer.Length));
        }

        return batch;
    }

    private OcrRecognizerResult[] DecodeStackedTensor(Tensor<float> tensor, int count)
    {
        var dims = tensor.Dimensions.ToArray();
        var labelCount = dims[2];
        var charCount = dims[1];
        var results = new OcrRecognizerResult[count];
        if (tensor is DenseTensor<float> dense)
        {
            var span = dense.Buffer.Span;
            for (var i = 0; i < count; i++)
            {
                results[i] = DecodeOne(span, i * charCount * labelCount, charCount, labelCount);
            }
        }
        else
        {
            var data = tensor.ToArray();
            for (var i = 0; i < count; i++)
            {
                results[i] = DecodeOne(data, i * charCount * labelCount, charCount, labelCount);
            }
        }

        return results;
    }

    private OcrRecognizerResult DecodeOne(ReadOnlySpan<float> data, int batchOffset, int charCount, int labelCount)
    {
        StringBuilder sb = new();
        var lastIndex = 0;
        float score = 0;
        for (var n = 0; n < charCount; ++n)
        {
            var rowOffset = batchOffset + n * labelCount;
            var maxVal = float.MinValue;
            var maxIndex = 0;
            for (var c = 0; c < labelCount; c++)
            {
                var value = data[rowOffset + c];
                if (value > maxVal)
                {
                    maxVal = value;
                    maxIndex = c;
                }
            }

            if (maxIndex > 0 && !(n > 0 && maxIndex == lastIndex))
            {
                score += maxVal;
                sb.Append(OcrUtils.GetLabelByIndex(maxIndex, _labels));
            }

            lastIndex = maxIndex;
        }

        var text = sb.ToString();
        return new OcrRecognizerResult(text, text.Length > 0 ? score / text.Length : 0);
    }

}
