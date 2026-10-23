using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OCR.Engine;
using BetterGenshinImpact.Core.Recognition.OCR.Engine.data;
using BetterGenshinImpact.Core.Recognition.OCR.Paddle;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.Ncnn;

/// <summary>
///     ncnn Vulkan 后端的识别器：单行识别，按宽度分桶懒加载会话。
///     归一化复用 OcrUtils.ResizeNormImg（与 DML 路线数学一致），CTC 解码与 Rec.DecodeOne 同语义。
///     模型按桶宽各一份（pnnx 常量折叠要求静态形状），命名 rec_w{width}.param/bin。
///     B580 实测（v6 small，C# 全流程）：w320 8.6ms / w480 10.5ms，对比 DML 每次 Run ~20ms 地板。
/// </summary>
internal sealed class NcnnRec : IOcrRecognizer
{
    private static readonly ILogger Logger = App.GetLogger<NcnnRec>();

    private const int ModelHeight = 48;

    /// <summary>rec 主干下采样倍率：输出时间步 = 输入宽度 / 8。</summary>
    private const int Downsample = 8;

    private const int BucketStep = 160;
    private const int MaxBucketWidth = 960;

    private readonly NcnnShim _shim;
    private readonly string _modelDir;
    private readonly int _gpuIndex;
    private readonly NcnnTuningSettings _tuning;
    private readonly IReadOnlyList<string> _labels;
    private readonly Dictionary<int, SessionEntry> _sessions = new();
    private readonly object _sessionLock = new();

    /// <summary>单个宽度桶的会话池与 blob 名缓存。</summary>
    private sealed class SessionEntry(NcnnSessionPool pool, float[] buffer)
    {
        public NcnnSessionPool Pool { get; } = pool;
        public float[] Buffer { get; } = buffer;
        public string InputName { get; set; } = "in0";
        public string OutputName { get; set; } = "out0";
        public bool BlobsResolved { get; set; }
    }

    public NcnnRec(string shimDllPath, string modelDir, IReadOnlyList<string> labels,
        NcnnTuningSettings tuning, int gpuIndex = 0)
    {
        if (!File.Exists(shimDllPath))
            throw new FileNotFoundException("ncnn_shim.dll 未找到", shimDllPath);
        _modelDir = modelDir;
        _labels = labels;
        _gpuIndex = gpuIndex;
        _tuning = tuning.NormalizedCopy();
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
        var entry = GetSession(width);
        _ = RunSession(entry, entry.Buffer, width);
    }

    public OcrRecognizerResult[] Run(Mat[] srcs, int batchSize = 0)
    {
        if (srcs.Length == 0) return [];
        var results = new OcrRecognizerResult[srcs.Length];
        // ncnn 无批维：逐行推理；并行会话数 > 1 时按池大小并行，默认 1 保持历史顺序行为。
        if (_tuning.ParallelSessions > 1 && srcs.Length > 1)
        {
            Parallel.For(0, srcs.Length, new ParallelOptions { MaxDegreeOfParallelism = _tuning.ParallelSessions },
                i => results[i] = Run(srcs[i]));
            return results;
        }

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
        var sw = Stopwatch.StartNew();
        var tensor = OcrUtils.ResizeNormImg(src, new OcrShape(3, width, ModelHeight), out var owner);
        var preMs = sw.Elapsed.TotalMilliseconds;
        try
        {
            if (tensor is not DenseTensor<float> dense)
                throw new InvalidOperationException("ResizeNormImg 应返回 DenseTensor<float>");
            var entry = GetSession(width);
            dense.Buffer.CopyTo(entry.Buffer);
            var (managed, info) = RunSession(entry, entry.Buffer, width);
            var (charCount, labelCount) = ResolveOutputShape(info, managed.Length, width);
            var inferMs = sw.Elapsed.TotalMilliseconds - preMs;
            var decodeSw = Stopwatch.StartNew();
            var result = DecodeOne(managed, charCount, labelCount);
            Logger.LogDebug(
                "[OCR][Rec][ncnn] 桶宽{Bucket} | 归一化 {Pre:F1}ms 推理 {Inf:F1}ms 解码 {Dec:F1}ms",
                width, preMs, inferMs, decodeSw.Elapsed.TotalMilliseconds);
            return result;
        }
        finally
        {
            owner.Dispose();
        }
    }

    /// <summary>
    ///     解析输出形状：优先用 shim 返回的 w/h/c（fp32 unpacked 后），
    ///     旧 DLL 或无元信息时按「时间步 = 桶宽 / 8」推断。
    /// </summary>
    private (int CharCount, int LabelCount) ResolveOutputShape(NcnnTensorInfo? info, int count, int width)
    {
        if (info != null)
        {
            var labels = info.Channels > 0 ? info.Channels : 0;
            var steps = info.Width > 0 ? info.Width : 0;
            if (labels > 1 && steps > 0 && steps * labels == count) return (steps, labels);

            var fallbackSteps = Math.Max(1, width / Downsample);
            if (labels > 1 && count % labels == 0 && count / labels > 0) return (count / labels, labels);
            if (count % fallbackSteps == 0) return (fallbackSteps, count / fallbackSteps);

            throw new InvalidOperationException(
                $"ncnn rec 输出维度异常: dims={info.Dims} {info.ShapeText} count={count}（桶宽 {width}）");
        }

        var charCount = Math.Max(1, width / Downsample);
        if (count % charCount != 0)
            throw new InvalidOperationException($"ncnn rec 输出维度异常: {count} 不能被 {charCount} 整除");
        return (charCount, count / charCount);
    }

    /// <summary>按行图宽高比计算输入桶宽（向上取整到 160，限制在 [160, 960]）。</summary>
    private static int ChooseBucket(int width, int height)
    {
        if (height <= 0) return BucketStep;
        var desired = (int)Math.Ceiling(width * (double)ModelHeight / height);
        var bucket = (int)Math.Ceiling(desired / (double)BucketStep) * BucketStep;
        return Math.Clamp(bucket, BucketStep, MaxBucketWidth);
    }

    /// <summary>单次推理：返回带元信息的紧凑 fp32。</summary>
    private (float[] Data, NcnnTensorInfo Info) RunSession(SessionEntry entry, float[] buffer, int width)
        => entry.Pool.Use(net =>
        {
            if (!entry.BlobsResolved) ResolveBlobs(entry, net);
            var outputs = _shim.InferEx(net,
                [NcnnInput.FromFloat(entry.InputName, buffer, width, ModelHeight, 3)],
                [entry.OutputName]);
            if (outputs.Count == 0)
                throw new InvalidOperationException($"ncnn rec 未返回输出（桶宽 {width}）");
            return (outputs[0].Data, outputs[0].Info);
        });

    private void ResolveBlobs(SessionEntry entry, nint net)
    {
        var inputs = _shim.QueryInputBlobs(net);
        var outputs = _shim.QueryOutputBlobs(net);
        if (inputs.Length > 0 && !string.IsNullOrWhiteSpace(inputs[0])) entry.InputName = inputs[0];
        if (outputs.Length > 0 && !string.IsNullOrWhiteSpace(outputs[0])) entry.OutputName = outputs[0];
        entry.BlobsResolved = true;
    }

    private SessionEntry GetSession(int width)
    {
        lock (_sessionLock)
        {
            if (_sessions.TryGetValue(width, out var entry)) return entry;
            var paramPath = Path.Combine(_modelDir, $"rec_w{width}.param");
            var modelPath = Path.Combine(_modelDir, $"rec_w{width}.bin");
            if (!File.Exists(paramPath) || !File.Exists(modelPath))
                throw new FileNotFoundException($"ncnn rec 模型缺失（桶宽 {width}）", paramPath);
            var options = _tuning.ToNetOptions(paramPath, modelPath, _gpuIndex);
            var pool = new NcnnSessionPool(_shim, options, _tuning.ParallelSessions);
            entry = new SessionEntry(pool, new float[3 * ModelHeight * width]);
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
            foreach (var entry in _sessions.Values) entry.Pool.Dispose();
            _sessions.Clear();
        }

        _shim.Dispose();
    }
}
