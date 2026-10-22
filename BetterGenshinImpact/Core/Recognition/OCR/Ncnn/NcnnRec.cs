using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using BetterGenshinImpact.Core.Recognition.OCR.Engine;
using BetterGenshinImpact.Core.Recognition.OCR.Engine.data;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.OCR.Ncnn;

/// <summary>
///     ncnn Vulkan 后端的识别器（实验性）：单行文本识别，对应 DML 路线 Rec 的单行职责。
///     归一化复用 OcrUtils.ResizeNormImg（(x/255-0.5)*2 = x/127.5-1，与 ncnn 参考实现一致），
///     CTC 解码复用标量逻辑（与 Rec.DecodeOne 同语义）。
///     模型按宽度分桶（160/320/.../960 各一份 pnnx 转换产物），原因：pnnx 常量折叠要求静态形状。
///     B580 实测（v6 small）：w160 3.7ms / w320 4.6ms / w480 5.4ms / w960 8.0ms，
///     对比 DML 每次 Run ~20ms 的派发地板。
/// </summary>
public sealed class NcnnRec : IDisposable
{
    private const int ModelHeight = 48;

    /// <summary>rec 主干下采样倍率：输出时间步 = 输入宽度 / 8。</summary>
    private const int Downsample = 8;

    private readonly NcnnShim _shim;
    private readonly nint _net;
    private readonly IReadOnlyList<string> _labels;
    private readonly float[] _inputBuffer;

    /// <summary>输入桶宽（本模型固定宽度输入）。</summary>
    public int Width { get; }

    /// <summary>
    ///     按桶宽加载对应模型：<paramref name="modelDir" /> 下需存在 rec_w{width}.param / rec_w{width}.bin。
    /// </summary>
    /// <param name="labels">字符字典（不含 blank/空格），来源为模型的 inference.yml character_dict。</param>
    public NcnnRec(string shimDllPath, string modelDir, int width, IReadOnlyList<string> labels,
        int gpuIndex = 0, int numThreads = 4)
    {
        if (!File.Exists(shimDllPath))
            throw new FileNotFoundException("ncnn_shim.dll 未找到", shimDllPath);
        Width = width;
        var paramPath = Path.Combine(modelDir, $"rec_w{width}.param");
        var modelPath = Path.Combine(modelDir, $"rec_w{width}.bin");
        if (!File.Exists(paramPath) || !File.Exists(modelPath))
            throw new FileNotFoundException($"ncnn rec 模型缺失（桶宽 {width}）", paramPath);

        _labels = labels;
        _shim = new NcnnShim(shimDllPath);
        _net = _shim.Create(paramPath, modelPath, gpuIndex, numThreads);
        if (_net == nint.Zero)
            throw new InvalidOperationException("ncnn 会话创建失败（模型加载错误或 Vulkan 不可用）");
        _inputBuffer = new float[3 * ModelHeight * width];
    }

    public void Dispose()
    {
        if (_net != nint.Zero) _shim.Destroy(_net);
        _shim.Dispose();
    }

    /// <summary>单行识别：src 为 BGR 行裁剪图。</summary>
    public OcrRecognizerResult Run(Mat src)
    {
        var tensor = OcrUtils.ResizeNormImg(src, new OcrShape(3, Width, ModelHeight), out var owner);
        try
        {
            if (tensor is not DenseTensor<float> dense)
                throw new InvalidOperationException("ResizeNormImg 应返回 DenseTensor<float>");
            dense.Buffer.CopyTo(_inputBuffer);
            // pnnx 转换的模型层名为 in0/out0
            var rc = _shim.Infer(_net, "in0", "out0", _inputBuffer,
                Width, ModelHeight, 3, out var outBuffer, out var outCount);
            if (rc != 0)
                throw new InvalidOperationException($"ncnn 推理失败: {rc}");
            try
            {
                var managed = new float[outCount];
                Marshal.Copy(outBuffer, managed, 0, outCount);
                // 输出 [1, T, C]：T = 宽度/8，C 由模型决定（v6 small 为 18710）
                var charCount = Math.Max(1, Width / Downsample);
                if (outCount % charCount != 0)
                    throw new InvalidOperationException($"ncnn 输出维度异常: {outCount} 不能被 {charCount} 整除");
                return DecodeOne(managed, charCount, outCount / charCount);
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
                sb.Append(GetLabel(maxIndex));
            }

            lastIndex = maxIndex;
        }

        var text = sb.ToString();
        return new OcrRecognizerResult(text, text.Length > 0 ? score / text.Length : 0);
    }

    /// <summary>
    ///     与 OcrUtils.GetLabelByIndex 同语义，但对尾部填充类（字典数+2 之后）返回空串而不是抛异常——
    ///     v6 模型的输出类数比 字典数+2 多 1 个填充类。
    /// </summary>
    private string GetLabel(int index)
    {
        if (index > 0 && index <= _labels.Count) return _labels[index - 1];
        if (index == _labels.Count + 1) return " ";
        return "";
    }
}
