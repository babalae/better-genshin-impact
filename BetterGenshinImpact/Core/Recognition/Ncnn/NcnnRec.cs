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
///     ncnn Vulkan 后端的识别器：单行识别。
///     <para>
///         两种输入模式，由 <c>useBuckets</c> 决定：
///         ① 分桶（默认）：按行宽高比选 160 步长桶（160–960），每桶一份 pnnx 静态模型（rec_w{width}.param/bin）；
///         ② 动态单模型（分桶关闭且 <c>rec_dyn.param/bin</c> 存在）：pnnx 双形状转换的动态宽度模型，
///         每行按精确缩放宽度推理，零 padding；产物缺失时回落分桶并记录警告。
///     </para>
///     归一化复用 OcrUtils.ResizeNormImg（与 DML 路线数学一致），CTC 解码走 NcnnRecDecoder。
///     shim 由装配方注入并持有生命周期，本类只负责会话与推理。
/// </summary>
internal sealed class NcnnRec : IOcrRecognizer
{
    private readonly ILogger _logger;

    private const int ModelHeight = 48;

    private const int BucketStep = 160;
    private const int MaxBucketWidth = 960;

    /// <summary>动态模式的输入宽度边界；下限保证至少 1 个时间步（下采样 8），上限约束缓冲与单行计算量。</summary>
    private const int MinDynamicWidth = 8;
    private const int MaxDynamicWidth = 2000;

    private const string DynamicParamName = "rec_dyn.param";
    private const string DynamicBinName = "rec_dyn.bin";

    private readonly INcnnShim _shim;
    private readonly string _modelDir;
    private readonly int _gpuIndex;
    private readonly NcnnTuningSettings _tuning;
    private readonly IReadOnlyList<string> _labels;
    private readonly NcnnBucketCache<NcnnSession> _sessions;

    /// <summary>是否使用动态宽度单模型（分桶关闭且产物在位）。</summary>
    private readonly bool _dynamicModel;

    /// <summary>分桶关闭但动态产物缺失时的固定桶宽（最大档）。</summary>
    private readonly int _fixedWidth;

    public NcnnRec(INcnnShim shim, string modelDir, IReadOnlyList<string> labels,
        NcnnTuningSettings tuning, int gpuIndex = 0, bool useBuckets = true, ILogger? logger = null)
    {
        _shim = shim;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        _modelDir = modelDir;
        _labels = labels;
        _gpuIndex = gpuIndex;
        _tuning = tuning.NormalizedCopy();
        _dynamicModel = !useBuckets && File.Exists(Path.Combine(modelDir, DynamicParamName))
                        && File.Exists(Path.Combine(modelDir, DynamicBinName));
        _fixedWidth = MaxBucketWidth;
        if (!useBuckets && !_dynamicModel)
            _logger.LogWarning("[OCR][Rec][ncnn] 分桶已关闭但 {Param} 缺失，回落到固定 {Width} 桶宽", DynamicParamName, _fixedWidth);
        // 会话按需懒加载：分桶按桶宽，动态模式单会话（键 0）
        _sessions = new NcnnBucketCache<NcnnSession>(CreateSession);
    }

    public string GetConfigName => "PP-OCRv6 (ncnn)";

    public (int Batch, int Width)[] GetWarmupBuckets()
    {
        if (_dynamicModel) return [(1, 320)];
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
        var session = _sessions.GetOrCreate(SessionKey(width));
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
        var width = SelectInputWidth(src);
        var sw = Stopwatch.StartNew();
        var tensor = OcrUtils.ResizeNormImg(src, new OcrShape(3, width, ModelHeight), out var owner);
        var preMs = sw.Elapsed.TotalMilliseconds;
        try
        {
            if (tensor is not DenseTensor<float> dense)
                throw new InvalidOperationException("ResizeNormImg 应返回 DenseTensor<float>");
            var session = _sessions.GetOrCreate(SessionKey(width));
            dense.Buffer.CopyTo(session.InputBuffer);
            // ResizeNormImg 返回实际缩放宽度（≤ 请求宽度）：不足部分清零（归一化后为中性灰 0.0），
            // 避免桶宽大于行宽时读到上一行的残留数据；动态模式二者相等，无尾部。
            var copied = (int)dense.Buffer.Length;
            var total = width * ModelHeight * 3;
            if (copied < total) Array.Clear(session.InputBuffer, copied, total - copied);
            var output = session.Infer(NcnnInput.FromFloat("in0", session.InputBuffer, width, ModelHeight, 3));
            var (charCount, labelCount) =
                NcnnRecDecoder.ResolveOutputShape(output.Info, output.Data.Length, width);
            var inferMs = sw.Elapsed.TotalMilliseconds - preMs;
            var decodeSw = Stopwatch.StartNew();
            var result = NcnnRecDecoder.Decode(output.Data, charCount, labelCount, _labels);
            _logger.LogDebug(
                "[OCR][Rec][ncnn] 桶宽{Bucket} | 归一化 {Pre:F1}ms 推理 {Inf:F1}ms 解码 {Dec:F1}ms",
                width, preMs, inferMs, decodeSw.Elapsed.TotalMilliseconds);
            return result;
        }
        finally
        {
            owner.Dispose();
        }
    }
    /// <summary>选择本行输入宽度：动态模式用精确缩放宽度，分桶模式向上取整到桶步长。</summary>
    private int SelectInputWidth(Mat src)
    {
        if (_dynamicModel)
        {
            if (src.Height <= 0) return MinDynamicWidth;
            var desired = (int)Math.Ceiling(ModelHeight * src.Width / (double)src.Height);
            return Math.Clamp(desired, MinDynamicWidth, MaxDynamicWidth);
        }

        return _fixedWidth > 0 ? _fixedWidth : ChooseBucket(src.Width, src.Height);
    }

    /// <summary>动态模式全部行共用会话 0；分桶模式按桶宽分会话。</summary>
    private int SessionKey(int width) => _dynamicModel ? 0 : width;

    /// <summary>分桶模式：按行图宽高比计算输入桶宽（向上取整到 160，限制在 [160, 960]）。</summary>
    private static int ChooseBucket(int width, int height)
    {
        if (height <= 0) return BucketStep;
        var desired = (int)Math.Ceiling(width * (double)ModelHeight / height);
        var bucket = (int)Math.Ceiling(desired / (double)BucketStep) * BucketStep;
        return Math.Clamp(bucket, BucketStep, MaxBucketWidth);
    }

    private NcnnSession CreateSession(int key)
    {
        string paramPath;
        string modelPath;
        int inputBufferSize;
        if (_dynamicModel)
        {
            paramPath = Path.Combine(_modelDir, DynamicParamName);
            modelPath = Path.Combine(_modelDir, DynamicBinName);
            inputBufferSize = 3 * ModelHeight * MaxDynamicWidth;
        }
        else
        {
            paramPath = Path.Combine(_modelDir, $"rec_w{key}.param");
            modelPath = Path.Combine(_modelDir, $"rec_w{key}.bin");
            inputBufferSize = 3 * ModelHeight * key;
        }

        if (!File.Exists(paramPath) || !File.Exists(modelPath))
            throw new FileNotFoundException($"ncnn rec 模型缺失（{( _dynamicModel ? "动态" : $"桶宽 {key}" )}）", paramPath);
        return new NcnnSession(_shim, _tuning.ToNetOptions(paramPath, modelPath, _gpuIndex),
            _tuning.ParallelSessions, inputBufferSize);
    }

    public void Dispose() => _sessions.Dispose();
}
