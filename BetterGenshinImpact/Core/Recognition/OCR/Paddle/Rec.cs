using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using BetterGenshinImpact.Core.Recognition.OCR.Engine;
using BetterGenshinImpact.Core.Recognition.OCR.Engine.data;
using BetterGenshinImpact.Core.Recognition.ONNX;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.OCR.Paddle;

public class Rec : IOcrRecognizer
{
    private static readonly ILogger Logger = App.GetLogger<Rec>();

    private readonly object _sessionLock = new();
    private readonly InferenceSession _session;
    private readonly bool _stackedEnabled;
    private readonly IReadOnlyList<string> labels;
    private readonly OcrVersionConfig config;
    private readonly BgiOnnxSessionFactory bgiOnnxFactory;
    private bool _disposed;

    public Rec(BgiOnnxModel model, IReadOnlyList<string> labels, OcrVersionConfig config,
        BgiOnnxSessionFactory bgiOnnxFactory)
    {
        this.labels = labels;
        this.config = config;
        this.bgiOnnxFactory = bgiOnnxFactory;
        _session = bgiOnnxFactory.CreateInferenceSession(model, true);
        _stackedEnabled = bgiOnnxFactory.Runtime.Engine == InferenceDeviceType.DirectML
                          && !bgiOnnxFactory.CpuOcr;
    }

    /// <summary>
    ///     识别输入形状分桶的档位步长：批宽向上取整到 160 的倍数，
    ///     使模型输入形状只有有限几档，避免 OpenVINO 等按输入形状编译的
    ///     执行提供程序在 GPU/NPU 上每个新宽度都触发一次模型重编译。
    /// </summary>
    private const int RecBucketStep = 160;

    /// <summary>识别输入宽度档位上限：覆盖模型高度约 20 倍宽高比的常用范围，更宽的行在首次使用时编译。</summary>
    private const int RecWarmupMaxWidth = 960;

    /// <summary>
    ///     DirectML 堆叠批大小。DML 每次 Run 有约 20ms 的 CPU 侧派发地板（180 个算子逐个提交，GPU 近乎空闲），
    ///     多行逐行 Run 会让地板成本乘以行数；堆叠成固定批 8 一次 Run 摊薄。
    ///     批维也是 DML 按形状编译的一部分，所以补零行到固定批而不是按实际行数分批，
    ///     否则批 1~8 × 6 档宽会产生 48 个需要预热编译的形状。
    /// </summary>
    private const int RecStackBatch = 8;

    /// <summary>识别输入预热档位：(批大小, 桶宽)。堆叠路径的批维同样需要预热编译。</summary>
    internal (int Batch, int Width)[] GetWarmupBuckets()
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

    /// <summary>用零张量预热指定档位，提前触发该形状的底层编译（按形状编译的 EP 首次耗时数秒）。</summary>
    internal void Warmup(int batch, int width)
    {
        var input = new DenseTensor<float>(new[] { batch, 3, config.Shape.Height, width });
        lock (_sessionLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var results = _session.Run([
                NamedOnnxValue.CreateFromTensor(_session.InputNames[0], input)
            ]);
        }
    }

    // _labels = File.ReadAllLines(labelFilePath);

    public void Dispose()
    {
        lock (_sessionLock)
        {
            if (_disposed) return;
            _disposed = true;
            _session.Dispose();
        }
    }

    /// <summary>
    ///     Run OCR recognition on multiple images in batches.
    /// </summary>
    /// <param name="srcs">Array of images for OCR recognition.</param>
    /// <param name="batchSize">Size of the batch to run OCR recognition on.</param>
    /// <returns>Array of <see cref="OcrRecognizerResult" /> instances corresponding to OCR recognition results of the images.</returns>
    public OcrRecognizerResult[] Run(Mat[] srcs, int batchSize = 0)
    {
        if (srcs.Length == 0) return [];

        var chooseBatchSize = batchSize != 0 ? batchSize : Math.Min(8, Environment.ProcessorCount);

        return srcs
            .Select((x, i) => (mat: x, i))
            .OrderBy(x => x.mat.Width)
            .Chunk(chooseBatchSize)
            .Select(x => (result: RunMulti(x.Select(x1 => x1.mat).ToArray()), ids: x.Select(x1 => x1.i).ToArray()))
            .SelectMany(x => x.result.Zip(x.ids, (result, i) => (result, i)))
            .OrderBy(x => x.i)
            .Select(x => x.result)
            .ToArray();
    }

    public OcrRecognizerResult Run(Mat src)
    {
        return RunMulti([src]).Single();
    }

    private OcrRecognizerResult[] RunMulti(Mat[] srcs)
    {
        if (srcs.Length == 0) return [];

        for (var i = 0; i < srcs.Length; ++i)
        {
            var src = srcs[i];
            if (src.Empty())
                throw new ArgumentException($"src[{i}] size should not be 0, wrong input picture provided?");
        }

        var modelHeight = config.Shape.Height;
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
                    // 多线程推理会出现问题，加锁解决。
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
                    // 因为一个已知bug,tensor中内存在dml下使用完后会被释放掉,锁之外的代码会报错：解码在锁内直接读 buffer
                    var decodeStopwatch = Stopwatch.StartNew();
                    decodeResults = DecodeStackedTensor(tensor, srcs.Length);
                    decodeMs = decodeStopwatch.Elapsed.TotalMilliseconds;
                }
            }
            else
            {
                OcrRecognizerResult[][] perSrcResults;
                perSrcResults = srcs
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
                            // Only dispose Mats created in this scope
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
                                // 多线程推理会出现问题，加锁解决。
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
                                inferMs += sw.Elapsed.TotalMilliseconds;
                                // 因为一个已知bug,tensor中内存在dml下使用完后会被释放掉,锁之外的代码会报错：
                                // 解码在锁内直接读 buffer，同时省去整块输出的 ToArray 拷贝
                                var decodeStopwatch = Stopwatch.StartNew();
                                var lineResults = DecodeStackedTensor(tensor, 1);
                                decodeMs += decodeStopwatch.Elapsed.TotalMilliseconds;
                                return lineResults;
                            }
                        }
                    ).ToArray();
                decodeResults = perSrcResults.SelectMany(x => x).ToArray();
            }
        }
        finally
        {
            owners.ForEach(x => { x.Dispose(); });
        }

        if (stacked)
        {
            Logger.LogDebug(
                "[OCR][Rec] 桶宽{BucketWidth} 行数{LineCount} 堆叠批{Batch} | 预处理 {Pre:F1}ms 推理 {Inf:F1}ms(均 {Avg:F1}ms/行) 解码 {Dec:F1}ms",
                maxWidth, srcs.Length, RecStackBatch, preprocessMs, inferMs,
                inferMs / Math.Max(1, srcs.Length), decodeMs);
        }
        else
        {
            Logger.LogDebug(
                "[OCR][Rec] 桶宽{BucketWidth} 行数{LineCount} | 预处理 {Pre:F1}ms 推理 {Inf:F1}ms(均 {Avg:F1}ms/行) 解码 {Dec:F1}ms",
                maxWidth, srcs.Length, preprocessMs, inferMs,
                inferMs / Math.Max(1, srcs.Length), decodeMs);
        }
        return decodeResults;
    }


    /// <summary>把各行的 [1,3,H,W] 张量沿批维堆叠成 [批,3,H,W]；不足的批位保持零填充，模型输出空白行。</summary>
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

    /// <summary>在锁内直接从输出张量解码前 count 行（DML tensor 内存在 Run 返回后释放，不能带出锁）。</summary>
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
                results[i] = DecodeOne(span, (long)i * charCount * labelCount, charCount, labelCount);
            }
        }
        else
        {
            var data = tensor.ToArray();
            for (var i = 0; i < count; i++)
            {
                results[i] = DecodeOne(data, (long)i * charCount * labelCount, charCount, labelCount);
            }
        }

        return results;
    }

    /// <summary>对一行 [charCount, labelCount] 的概率做 CTC 贪心解码（与原有逐行逻辑一致）。</summary>
    private OcrRecognizerResult DecodeOne(ReadOnlySpan<float> data, long batchOffset, int charCount, int labelCount)
    {
        StringBuilder sb = new();
        var lastIndex = 0;
        float score = 0;
        for (var n = 0; n < charCount; ++n)
        {
            var rowOffset = batchOffset + (long)n * labelCount;
            var maxVal = float.MinValue;
            var maxIndex = 0;
            for (var c = 0; c < labelCount; c++)
            {
                var value = data[(int)(rowOffset + c)];
                if (value > maxVal)
                {
                    maxVal = value;
                    maxIndex = c;
                }
            }

            if (maxIndex > 0 && !(n > 0 && maxIndex == lastIndex))
            {
                score += maxVal;
                sb.Append(OcrUtils.GetLabelByIndex(maxIndex, labels));
            }

            lastIndex = maxIndex;
        }

        var text = sb.ToString();
        return new OcrRecognizerResult(text, text.Length > 0 ? score / text.Length : 0);
    }

    // 显式接口实现：预热与批档属于内部细节，不扩大公共 API
    (int Batch, int Width)[] IOcrRecognizer.GetWarmupBuckets() => GetWarmupBuckets();
    void IOcrRecognizer.Warmup(int batch, int width) => Warmup(batch, width);

    public string GetConfigName => config.Name;
}
