using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using BetterGenshinImpact.Core.Recognition.OCR.Engine;
using BetterGenshinImpact.Core.Recognition.OCR.Engine.data;
using BetterGenshinImpact.Core.Recognition.OCR.Paddle;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.OCR.Ncnn;

/// <summary>
///     ncnn Vulkan 后端的识别器：单行识别，按宽度分桶懒加载会话。
///     归一化复用 OcrUtils.ResizeNormImg（与 DML 路线数学一致），CTC 解码与 Rec.DecodeOne 同语义。
///     模型按桶宽各一份（pnnx 常量折叠要求静态形状），命名 rec_w{width}.param/bin。
///     B580 实测（v6 small，C# 全流程）：w320 8.6ms / w480 10.5ms，对比 DML 每次 Run ~20ms 地板。
/// </summary>
internal sealed class NcnnRec : IOcrRecognizer
{
    private const int ModelHeight = 48;

    /// <summary>rec 主干下采样倍率：输出时间步 = 输入宽度 / 8。</summary>
    private const int Downsample = 8;

    private const int BucketStep = 160;
    private const int MaxBucketWidth = 960;

    private readonly NcnnShim _shim;
    private readonly string _modelDir;
    private readonly int _gpuIndex;
    private readonly int _numThreads;
    private readonly IReadOnlyList<string> _labels;
    private readonly Dictionary<int, (nint Net, float[] Buffer)> _sessions = new();
    private readonly object _sessionLock = new();

    public NcnnRec(string shimDllPath, string modelDir, IReadOnlyList<string> labels,
        int gpuIndex = 0, int numThreads = 4)
    {
        if (!File.Exists(shimDllPath))
            throw new FileNotFoundException("ncnn_shim.dll 未找到", shimDllPath);
        _modelDir = modelDir;
        _labels = labels;
        _gpuIndex = gpuIndex;
        _numThreads = numThreads;
        _shim = new NcnnShim(shimDllPath);
    }

    public string GetConfigName => "PP-OCRv6 (ncnn)";

    public (int Batch, int Width)[] GetWarmupBuckets()
    {
        var buckets = new List<(int, int)>();
        for (var width = BucketStep; width <= MaxBucketWidth; width += BucketStep)
        {
            buckets.Add((1, width));
        }

        return buckets.ToArray();
    }

    public void Warmup(int batch, int width)
    {
        var (net, buffer) = GetSession(width);
        _ = Infer(net, buffer, width, out var ptr, out _);
        _shim.FreeBuffer(ptr);
    }

    public OcrRecognizerResult[] Run(Mat[] srcs, int batchSize = 0)
    {
        if (srcs.Length == 0) return [];
        // ncnn 无批维：逐行推理（单行 8.6~10.5ms，远低于 DML 逐行 20ms）
        var results = new OcrRecognizerResult[srcs.Length];
        for (var i = 0; i < srcs.Length; i++)
        {
            results[i] = Run(srcs[i]);
        }

        return results;
    }

    public OcrRecognizerResult Run(Mat src)
    {
        if (src.Empty()) throw new ArgumentException("src size should not be 0, wrong input picture provided?");
        var width = ChooseBucket(src.Width, src.Height);
        var tensor = OcrUtils.ResizeNormImg(src, new OcrShape(3, width, ModelHeight), out var owner);
        try
        {
            if (tensor is not DenseTensor<float> dense)
                throw new InvalidOperationException("ResizeNormImg 应返回 DenseTensor<float>");
            var (net, buffer) = GetSession(width);
            dense.Buffer.CopyTo(buffer);
            var rc = Infer(net, buffer, width, out var outPtr, out var outCount);
            if (rc != 0) throw new InvalidOperationException($"ncnn rec 推理失败: {rc}");
            try
            {
                var managed = new float[outCount];
                Marshal.Copy(outPtr, managed, 0, outCount);
                var charCount = Math.Max(1, width / Downsample);
                if (outCount % charCount != 0)
                    throw new InvalidOperationException($"ncnn rec 输出维度异常: {outCount} 不能被 {charCount} 整除");
                return DecodeOne(managed, charCount, outCount / charCount);
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

    /// <summary>按行图宽高比计算输入桶宽（向上取整到 160，限制在 [160, 960]）。</summary>
    private static int ChooseBucket(int width, int height)
    {
        if (height <= 0) return BucketStep;
        var desired = (int)Math.Ceiling(width * (double)ModelHeight / height);
        var bucket = (int)Math.Ceiling(desired / (double)BucketStep) * BucketStep;
        return Math.Clamp(bucket, BucketStep, MaxBucketWidth);
    }

    private int Infer(nint net, float[] buffer, int width, out nint outPtr, out int outCount)
        => _shim.Infer(net, "in0", "out0", buffer, width, ModelHeight, 3, out outPtr, out outCount);

    private (nint Net, float[] Buffer) GetSession(int width)
    {
        lock (_sessionLock)
        {
            if (_sessions.TryGetValue(width, out var entry)) return entry;
            var paramPath = Path.Combine(_modelDir, $"rec_w{width}.param");
            var modelPath = Path.Combine(_modelDir, $"rec_w{width}.bin");
            if (!File.Exists(paramPath) || !File.Exists(modelPath))
                throw new FileNotFoundException($"ncnn rec 模型缺失（桶宽 {width}）", paramPath);
            var net = _shim.Create(paramPath, modelPath, _gpuIndex, _numThreads);
            if (net == nint.Zero)
                throw new InvalidOperationException($"ncnn rec 会话创建失败（桶宽 {width}）");
            entry = (net, new float[3 * ModelHeight * width]);
            _sessions[width] = entry;
            return entry;
        }
    }

    /// <summary>CTC 贪心解码单行（与 Rec.DecodeOne 同语义：并列取最小索引，连续重复去重，0 为空白）。</summary>
    private OcrRecognizerResult DecodeOne(float[] data, int charCount, int labelCount)
    {
        System.Text.StringBuilder sb = new();
        var lastIndex = 0;
        float score = 0;
        for (var n = 0; n < charCount; ++n)
        {
            var rowOffset = n * labelCount;
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
                sb.Append(GetLabel(maxIndex));
            }

            lastIndex = maxIndex;
        }

        var text = sb.ToString();
        return new OcrRecognizerResult(text, text.Length > 0 ? score / text.Length : 0);
    }

    /// <summary>与 OcrUtils.GetLabelByIndex 同语义；尾部填充类返回空串（v6 输出类比 字典+2 多 0~1 个）。</summary>
    private string GetLabel(int index)
    {
        if (index > 0 && index <= _labels.Count) return _labels[index - 1];
        if (index == _labels.Count + 1) return " ";
        return "";
    }

    public void Dispose()
    {
        lock (_sessionLock)
        {
            foreach (var (net, _) in _sessions.Values)
            {
                if (net != nint.Zero) _shim.Destroy(net);
            }

            _sessions.Clear();
        }

        _shim.Dispose();
    }
}
