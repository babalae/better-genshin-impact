using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using BetterGenshinImpact.Core.Recognition.OCR.Engine;
using BetterGenshinImpact.Core.Recognition.OCR.Engine.data;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Helpers;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.OCR.Paddle;

/// <summary>
/// OCR 识别器，支持标准文字识别和基于动态规划的模糊匹配。
/// 模糊匹配将目标字符串与模型原始输出序列做子序列匹配，返回 0~1 的置信度分数，
/// 比先识别再字符串匹配更能容忍 OCR 噪声。
/// </summary>
public class Rec : IDisposable
{
    private readonly InferenceSession _session;
    private readonly IReadOnlyList<string> _labels;
    private readonly OcrVersionConfig _config;
    private readonly bool _allowDuplicateChar;
    private readonly float _threshold;

    // 模糊匹配相关字段

    /// <summary>标签长度集合（降序），用于从长到短贪心匹配目标字符串</summary>
    private readonly int[] _labelLengths;

    /// <summary>标签字符串→索引字典，索引从1开始（0为CTC空白符）</summary>
    private readonly IReadOnlyDictionary<string, int> _labelDict;

    /// <summary>按标签索引的权重数组，用于加权推理分数；为 null 时不加权</summary>
    private readonly float[]? _weights;

    /// <summary>目标字符串→标签索引序列的 LRU 缓存，加速重复查询</summary>
    private readonly CacheHelper.LruCache<string, int[]> _targetCache = new(128);

    /// <summary>
    /// ONNX 推理输出的命名张量结构，替代匿名元组 (int[], float[])。
    /// </summary>
    private readonly record struct TensorResult(int Batch, int TimeSteps, int LabelCount, float[] Data);

    public Rec(
        BgiOnnxModel model,
        IReadOnlyList<string> labels,
        OcrVersionConfig config,
        BgiOnnxFactory bgiOnnxFactory,
        bool allowDuplicateChar = false,
        Dictionary<string, float>? extraWeights = null,
        float threshold = 0.5f)
    {
        _session = bgiOnnxFactory.CreateInferenceSession(model, true);
        _labels = labels;
        _config = config;
        _allowDuplicateChar = allowDuplicateChar;
        _threshold = threshold;

        _labelDict = OcrUtils.CreateLabelDict(labels, out var labelLengths);
        _labelLengths = labelLengths;
        _weights = extraWeights is { Count: > 0 }
            ? OcrUtils.CreateWeights(extraWeights, _labelDict, labels.Count)
            : null;
    }

    public void Dispose()
    {
        lock (_session)
        {
            _session.Dispose();
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 对多张图像按批次执行 OCR 识别。
    /// </summary>
    public OcrRecognizerResult[] Run(Mat[] srcs, int batchSize = 0)
        => RunBatch(srcs, RunMulti, batchSize);

    public OcrRecognizerResult Run(Mat src)
        => RunMulti([src]).Single();

    private OcrRecognizerResult[] RunMulti(Mat[] srcs)
    {
        if (srcs.Length == 0) return [];

        for (var i = 0; i < srcs.Length; ++i)
        {
            var src = srcs[i];
            if (src.Empty())
                throw new ArgumentException($"src[{i}] size should not be 0, wrong input picture provided?");
        }

        var resultTensors = RunInference(srcs);

        return resultTensors.SelectMany(tensor =>
        {
            GCHandle dataHandle = default;
            try
            {
                dataHandle = GCHandle.Alloc(tensor.Data, GCHandleType.Pinned);
                var dataPtr = dataHandle.AddrOfPinnedObject();

                return Enumerable.Range(0, tensor.Batch)
                    .Select(i =>
                    {
                        StringBuilder sb = new();
                        var lastIndex = 0;
                        float score = 0;
                        var maxIdx = new int[2];
                        using var fullMat = Mat.FromPixelData(tensor.TimeSteps, tensor.LabelCount,
                            MatType.CV_32FC1,
                            dataPtr + i * tensor.TimeSteps * tensor.LabelCount * sizeof(float));
                        for (var n = 0; n < tensor.TimeSteps; ++n)
                        {
                            using var row = fullMat.Row(n);
                            row.MinMaxIdx(out _, out var maxVal, [], maxIdx);

                            if (maxIdx[1] > 0 && maxVal >= _threshold && (_allowDuplicateChar || !(n > 0 && maxIdx[1] == lastIndex)))
                            {
                                score += (float)maxVal;
                                sb.Append(OcrUtils.GetLabelByIndex(maxIdx[1], _labels));
                            }

                            lastIndex = maxIdx[1];
                        }

                        var text = sb.ToString();
                        return new OcrRecognizerResult(text, text.Length > 0 ? score / text.Length : 0);
                    })
                    .ToArray();
            }
            finally
            {
                if (dataHandle.IsAllocated) dataHandle.Free();
            }
        }).ToArray();
    }

    /// <summary>
    /// 将目标字符串转换为标签索引序列，利用 LRU 缓存加速重复查询。
    /// 无法映射到标签的字符会被跳过。
    /// </summary>
    public int[] GetTarget(string target)
    {
        if (_targetCache.TryGet(target, out var cached) && cached is not null)
            return cached;

        var result = OcrUtils.MapStringToLabelIndices(target, _labelDict, _labelLengths);
        _targetCache.Set(target, result);
        return result;
    }

    /// <summary>
    /// 对一批图像执行模糊匹配，返回与目标字符串的最大平均置信度 (0~1)。
    /// </summary>
    /// <param name="srcs">待匹配图像数组</param>
    /// <param name="target">目标字符串</param>
    /// <param name="batchSize">每批推理图像数，0表示自动</param>
    public double RunMatch(Mat[] srcs, string target, int batchSize = 0)
    {
        if (srcs.Length == 0) return 0;
        var targetIndexes = GetTarget(target);
        if (targetIndexes.Length == 0) return 0;

        var charLevelResults = RunBatch(srcs,
            mats => ProcessForMatch(RunInference(mats), targetIndexes), batchSize);

        return GetMaxScoreFlat(charLevelResults, targetIndexes);
    }

    /// <summary>
    /// 从 ONNX 原始输出张量中提取目标字符在每个时间步的置信度。
    /// <para>
    /// 与 RunMulti（标准 OCR）不同，此方法不做 argmax（MinMaxIdx），
    /// 而是按目标字符的 label 索引直接查找对应位置的原始置信度。
    /// 这样即使目标字符不是某个时间步的最高置信度候选，DP 仍然能拿到其实际分数进行匹配。
    /// </para>
    /// </summary>
    /// <param name="resultTensors">RunInference 返回的 (shape, data) 张量数组</param>
    /// <param name="targetIndexes">目标字符串映射后的 label 索引序列</param>
    /// <returns>每张图像对应一个 (labelIndex, confidence) 数组，供 DP 匹配使用</returns>
    private (int, float)[][] ProcessForMatch(TensorResult[] resultTensors, int[] targetIndexes)
    {
        // 目标字符去重（排除 CTC 空白符 index=0）
        var targetSet = new HashSet<int>(targetIndexes);
        targetSet.Remove(0);

        return resultTensors.Select(tensor =>
        {
            var chars = new List<(int, float)>();
            for (var n = 0; n < tensor.TimeSteps * tensor.Batch; n++)
            {
                // 直接按索引查找目标字符的置信度，而非对整行取 argmax
                var rowOffset = n * tensor.LabelCount;
                foreach (var labelIdx in targetSet)
                {
                    if (labelIdx >= tensor.LabelCount) continue;
                    var raw = tensor.Data[rowOffset + labelIdx];
                    var confidence = _weights is not null
                        ? raw * _weights[labelIdx]
                        : raw;
                    if (confidence > _threshold)
                        chars.Add((labelIdx, confidence));
                }
            }
            return chars.ToArray();
        }).ToArray();
    }

    /// <summary>
    /// 将多张图像的字符级别结果展平后，计算与 target 的最大匹配分数。
    /// 分母使用 target.Length，得到的是每个目标字符的平均置信度 (0~1)。
    /// </summary>
    private static double GetMaxScoreFlat((int, float)[][] result, int[] target)
    {
        var flatResult = result.SelectMany(x => x).ToArray();
        return OcrUtils.GetMaxScoreDp(flatResult, target, target.Length);
    }

    /// <summary>
    /// 通用批处理：按宽度排序、分批推理、恢复原始顺序
    /// </summary>
    private T[] RunBatch<T>(Mat[] srcs, Func<Mat[], T[]> process, int batchSize = 0)
    {
        if (srcs.Length == 0) return [];

        var chooseBatchSize = batchSize != 0 ? batchSize : Math.Min(8, Environment.ProcessorCount);

        return srcs
            .Select((x, i) => (mat: x, i))
            .OrderBy(x => x.mat.Width)
            .Chunk(chooseBatchSize)
            .Select(chunk =>
            {
                var mats = chunk.Select(x => x.mat).ToArray();
                var result = process(mats);
                return (result, ids: chunk.Select(x => x.i).ToArray());
            })
            .SelectMany(x => x.result.Zip(x.ids, (r, i) => (r, i)))
            .OrderBy(x => x.i)
            .Select(x => x.r)
            .ToArray();
    }

    /// <summary>
    /// 执行 ONNX 推理，返回每张图像的原始 (shape, data) 张量
    /// </summary>
    private TensorResult[] RunInference(Mat[] srcs)
    {
        var modelHeight = _config.Shape.Height;
        var maxWidth = (int)Math.Ceiling(srcs.Max(src =>
        {
            var size = src.Size();
            return 1.0 * size.Width / size.Height * modelHeight;
        }));
        List<IMemoryOwner<float>> owners = [];
        try
        {
            return srcs
                // .AsParallel()
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
                        var result = OcrUtils.ResizeNormImg(channel3, new OcrShape(3, maxWidth, modelHeight),
                            out var owner);
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
                    lock (_session)
                    {
                        // 多线程推理会出现问题，加锁解决。
                        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _session.Run([
                            NamedOnnxValue.CreateFromTensor(_session.InputNames[0], inputTensor)
                        ]);
                        var output = results[0];
                        if (output.ElementType is not TensorElementType.Float)
                            throw new Exception($"Unexpected output tensor type: {output.ElementType}");

                        if (output.ValueType is not OnnxValueType.ONNX_TYPE_TENSOR)
                            throw new Exception($"Unexpected output tensor value type: {output.ValueType}");
                        var tensor = output.AsTensor<float>();
                        // 因为一个已知bug,tensor中内存在dml下使用完后会被释放掉,锁之外的代码会报错
                        var dims = tensor.Dimensions;
                        return new TensorResult(dims[0], dims[1], dims[2], tensor.ToArray());
                    }
                }).ToArray();
        }
        finally
        {
            owners.ForEach(x => { x.Dispose(); });
        }
    }

    public string GetConfigName => _config.Name;
}
