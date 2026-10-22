using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

using BetterGenshinImpact.Core.Recognition.OCR.Engine;
using BetterGenshinImpact.Core.Recognition.OCR.Engine.data;

namespace BetterGenshinImpact.Core.Recognition.OCR.Ncnn;

/// <summary>
///     ncnn Vulkan 后端的识别器（实验性）：单行文本识别，对应 DML 路线 Rec 的单行职责。
///     归一化复用 OcrUtils.ResizeNormImg（(x/255-0.5)*2 = x/127.5-1，与 ncnn 参考实现一致），
///     CTC 解码复用标量逻辑（与 Rec.DecodeOne 同语义）。
///     预期收益：B580 上单行 ~10.7ms，对比 DML 每次 Run ~20ms 的派发地板。
/// </summary>
public sealed class NcnnRec : IDisposable
{
    private const string InputName = "input";
    private const string OutputName = "out";

    private readonly NcnnShim _shim;
    private readonly nint _net;
    private readonly IReadOnlyList<string> _labels;
    private readonly float[] _inputBuffer;

    /// <summary>输入桶宽（本模型固定宽度输入）。</summary>
    public int Width { get; }

    public NcnnRec(string shimDllPath, string paramPath, string modelPath, string dictPath,
        int width = 320, int gpuIndex = 0, int numThreads = 4)
    {
        if (!File.Exists(shimDllPath))
            throw new FileNotFoundException("ncnn_shim.dll 未找到", shimDllPath);
        Width = width;
        _shim = new NcnnShim(shimDllPath);
        _net = _shim.Create(paramPath, modelPath, gpuIndex, numThreads);
        if (_net == nint.Zero)
            throw new InvalidOperationException("ncnn 会话创建失败（模型加载错误或 Vulkan 不可用）");
        // ppocr_keys_v1：6623 行 + 空格 + 空白 = 6625 类，GetLabelByIndex 语义与主路线一致
        _labels = File.ReadAllLines(dictPath);
        _inputBuffer = new float[3 * 48 * width];
    }

    public void Dispose()
    {
        if (_net != nint.Zero) _shim.Destroy(_net);
        _shim.Dispose();
    }

    /// <summary>单行识别：src 为 BGR 行裁剪图（建议高宽比接近真实文本行）。</summary>
    public OcrRecognizerResult Run(Mat src)
    {
        // 归一化到 [-1,1] 的 CHW 张量，与 DML 路线 ResizeNormImg 完全一致
        var tensor = OcrUtils.ResizeNormImg(src, new OcrShape(3, Width, 48), out var owner);
        if (tensor is not DenseTensor<float> dense)
            throw new InvalidOperationException("ResizeNormImg 应返回 DenseTensor<float>");
        try
        {
            dense.Buffer.CopyTo(_inputBuffer);
            var rc = _shim.Infer(_net, InputName, OutputName, _inputBuffer,
                Width, 48, 3, out var outBuffer, out var outCount);
            if (rc != 0)
                throw new InvalidOperationException($"ncnn 推理失败: {rc}");
            try
            {
                var managed = new float[outCount];
                Marshal.Copy(outBuffer, managed, 0, outCount);
                // 输出 [1, T, C]：C = 字典数 + 2（空白/占位），T = outCount / C
                var labelCount = _labels.Count + 2;
                var charCount = outCount / labelCount;
                return DecodeOne(managed, charCount, labelCount);
            }
            finally
            {
                _shim.FreeBuffer(outBuffer);
            }
        }
        finally
        {
            owner.Dispose();
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
                sb.Append(OcrUtils.GetLabelByIndex(maxIndex, _labels));
            }

            lastIndex = maxIndex;
        }

        var text = sb.ToString();
        return new OcrRecognizerResult(text, text.Length > 0 ? score / text.Length : 0);
    }
}
