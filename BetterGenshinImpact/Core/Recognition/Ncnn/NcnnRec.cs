using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OCR.Engine;
using BetterGenshinImpact.Core.Recognition.OCR.Engine.data;
using BetterGenshinImpact.Core.Recognition.OCR.Paddle;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.Ncnn;

/// <summary>
///     ncnn Vulkan 后端的识别器：单行识别，动态宽度单模型（rec_dyn），
///     每行按精确缩放宽度推理，零 padding。
///     归一化复用 OcrUtils.ResizeNormImg（与 DML 路线数学一致），CTC 解码走 NcnnRecDecoder。
///     shim 由装配方注入并持有生命周期，本类只负责会话与推理。
/// </summary>
internal sealed class NcnnRec : IOcrRecognizer
{
    private const int ModelHeight = 48;

    /// <summary>动态宽度的输入边界；下限保证至少 1 个时间步（下采样 8），上限约束缓冲与单行计算量。</summary>
    private const int MinDynamicWidth = 8;
    private const int MaxDynamicWidth = 2000;

    private const string DynamicParamName = "rec_dyn.param";
    private const string DynamicBinName = "rec_dyn.bin";

    private readonly INcnnShim _shim;
    private readonly string _modelDir;
    private readonly int _gpuIndex;
    private readonly NcnnTuningSettings _tuning;
    private readonly IReadOnlyList<string> _labels;
    private readonly NcnnSession _session;

    public NcnnRec(INcnnShim shim, string modelDir, IReadOnlyList<string> labels,
        NcnnTuningSettings tuning, int gpuIndex = 0)
    {
        _shim = shim;
        _modelDir = modelDir;
        _labels = labels;
        _gpuIndex = gpuIndex;
        _tuning = tuning.NormalizedCopy();
        // 动态宽度单模型：构造时直接建会话（产物缺失立即失败）
        _session = NcnnSession.Create(_shim, _modelDir, DynamicParamName, DynamicBinName, _tuning, _gpuIndex,
            3 * ModelHeight * MaxDynamicWidth, "ncnn rec 动态形状模型缺失（rec_dyn.param / rec_dyn.bin）");
    }

    public string GetConfigName => "PP-OCRv6 (ncnn)";

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
        var tensor = OcrUtils.ResizeNormImg(src, new OcrShape(3, width, ModelHeight), out var owner);
        try
        {
            if (tensor is not DenseTensor<float> dense)
                throw new InvalidOperationException("ResizeNormImg 应返回 DenseTensor<float>");
            dense.Buffer.CopyTo(_session.InputBuffer);
            // ResizeNormImg 返回实际缩放宽度（≤ 请求宽度）：不足部分清零（归一化后为中性灰 0.0），
            // 避免请求宽度大于实际宽度时读到上一次推理的残留数据；正常情况二者相等，无尾部。
            var copied = (int)dense.Buffer.Length;
            var total = width * ModelHeight * 3;
            if (copied < total) Array.Clear(_session.InputBuffer, copied, total - copied);
            var output = _session.Infer(NcnnInput.FromFloat("in0", _session.InputBuffer, width, ModelHeight, 3));
            var (charCount, labelCount) =
                NcnnRecDecoder.ResolveOutputShape(output.Info, output.Data.Length, width);
            return NcnnRecDecoder.Decode(output.Data, charCount, labelCount, _labels);
        }
        finally
        {
            owner.Dispose();
        }
    }

    /// <summary>按行图宽高比计算精确缩放宽度（限制在 [MinDynamicWidth, MaxDynamicWidth]）。</summary>
    private int SelectInputWidth(Mat src)
    {
        if (src.Height <= 0) return MinDynamicWidth;
        var desired = (int)Math.Ceiling(ModelHeight * src.Width / (double)src.Height);
        return Math.Clamp(desired, MinDynamicWidth, MaxDynamicWidth);
    }

    public void Dispose() => _session.Dispose();
}
