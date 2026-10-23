using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OCR.Engine;
using BetterGenshinImpact.Core.Recognition.OCR.Engine.data;
using BetterGenshinImpact.Core.Recognition.OCR.Paddle;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.Ncnn;

/// <summary>
///     ncnn Vulkan 后端的识别器：单行识别，按宽度分桶懒加载会话。
///     归一化复用 OcrUtils.ResizeNormImg（与 DML 路线数学一致），CTC 解码走 NcnnRecDecoder。
///     模型按桶宽各一份（pnnx 常量折叠要求静态形状），命名 rec_w{width}.param/bin。
///     B580 实测（v6 small，C# 全流程）：w320 8.6ms / w480 10.5ms，对比 DML 每次 Run ~20ms 地板。
///     shim 由装配方注入并持有生命周期，本类只负责桶会话与推理。
/// </summary>
internal sealed class NcnnRec : IOcrRecognizer
{
    private static readonly ILogger Logger = App.GetLogger<NcnnRec>();

    private const int ModelHeight = 48;

    private const int BucketStep = 160;
    private const int MaxBucketWidth = 960;

    private readonly INcnnShim _shim;
    private readonly string _modelDir;
    private readonly int _gpuIndex;
    private readonly NcnnTuningSettings _tuning;
    private readonly IReadOnlyList<string> _labels;
    private readonly NcnnBucketCache<NcnnSession> _sessions;

    /// <summary>关闭分桶时的固定桶宽；0 表示按行宽高比自适应选桶。</summary>
    private readonly int _fixedWidth;

    public NcnnRec(INcnnShim shim, string modelDir, IReadOnlyList<string> labels,
        NcnnTuningSettings tuning, int gpuIndex = 0, bool useBuckets = true)
    {
        _shim = shim;
        _modelDir = modelDir;
        _labels = labels;
        _gpuIndex = gpuIndex;
        _tuning = tuning.NormalizedCopy();
        _fixedWidth = useBuckets ? 0 : MaxBucketWidth;
        _sessions = new NcnnBucketCache<NcnnSession>(CreateBucketSession);
    }

    public string GetConfigName => "PP-OCRv6 (ncnn)";

    public (int Batch, int Width)[] GetWarmupBuckets()
    {
        if (_fixedWidth > 0) return [(1, _fixedWidth)];
        var buckets = new List<(int, int)>();
        for (var width = BucketStep; width <= MaxBucketWidth; width += BucketStep)
        {
            buckets.Add((1, width));
        }

        return buckets.ToArray();
    }

    public void Warmup(int batch, int width)
    {
        var session = _sessions.GetOrCreate(width);
        Array.Clear(session.InputBuffer);
        _ = session.Infer(NcnnInput.FromFloat("in0", session.InputBuffer, width, ModelHeight, 3));
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
            var session = _sessions.GetOrCreate(width);
            dense.Buffer.CopyTo(session.InputBuffer);
            var output = session.Infer(NcnnInput.FromFloat("in0", session.InputBuffer, width, ModelHeight, 3));
            var (charCount, labelCount) =
                NcnnRecDecoder.ResolveOutputShape(output.Info, output.Data.Length, width);
            var inferMs = sw.Elapsed.TotalMilliseconds - preMs;
            var decodeSw = Stopwatch.StartNew();
            var result = NcnnRecDecoder.Decode(output.Data, charCount, labelCount, _labels);
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

    /// <summary>按行图宽高比计算输入桶宽（向上取整到 160，限制在 [160, 960]）；关闭分桶时固定用最大宽度。</summary>
    private int ChooseBucket(int width, int height)
    {
        if (_fixedWidth > 0) return _fixedWidth;
        if (height <= 0) return BucketStep;
        var desired = (int)Math.Ceiling(width * (double)ModelHeight / height);
        var bucket = (int)Math.Ceiling(desired / (double)BucketStep) * BucketStep;
        return Math.Clamp(bucket, BucketStep, MaxBucketWidth);
    }

    private NcnnSession CreateBucketSession(int width)
    {
        var paramPath = Path.Combine(_modelDir, $"rec_w{width}.param");
        var modelPath = Path.Combine(_modelDir, $"rec_w{width}.bin");
        if (!File.Exists(paramPath) || !File.Exists(modelPath))
            throw new FileNotFoundException($"ncnn rec 模型缺失（桶宽 {width}）", paramPath);
        return new NcnnSession(_shim, _tuning.ToNetOptions(paramPath, modelPath, _gpuIndex),
            _tuning.ParallelSessions, 3 * ModelHeight * width);
    }

    public void Dispose() => _sessions.Dispose();
}
