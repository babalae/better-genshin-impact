using System;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace BetterGenshinImpact.Core.Recognition.OCR.Paddle;

/// <summary>
///     OCR 检测 / 识别模型的推理会话抽象。ONNX Runtime 与 OpenVINO C# API 后端共用
///     Det / Rec 的全部预处理与后处理，只有会话实现不同（推理后端插件化的落点之一）。
///     实现方负责线程安全语义说明：Det / Rec 自带会话锁，Run 内部无需再加锁。
/// </summary>
internal interface IOcrInferenceSession : IDisposable
{
    /// <summary>模型输入节点名。</summary>
    string InputName { get; }

    /// <summary>
    ///     固定输入形状的最后一维宽度（NCHW 的 W）；动态形状会话返回 null。
    ///     Rec 预处理据此把超宽文本行压缩到固定宽度内（NPU 静态形状要求）。
    /// </summary>
    int? FixedInputWidth { get; }

    /// <summary>
    ///     固定输入形状的高度（NCHW 的 H）；动态形状会话返回 null。
    ///     Det 预处理在宽高均固定时走静态画布分支（长边等比缩放 + 补零，Intel 官方 GPU/NPU 方案）。
    /// </summary>
    int? FixedInputHeight { get; }

    /// <summary>运行一次推理，按输出节点顺序返回每个输出的形状与数据副本（DML 释放语义下数据必须在锁内拷出）。</summary>
    OcrInferenceResult[] Run(Tensor<float> input);
}

/// <summary>单个输出张量的形状与数据副本。</summary>
internal sealed record OcrInferenceResult(int[] Shape, float[] Data);

/// <summary>ONNX Runtime 会话适配器：Det / Rec 原生路径。</summary>
internal sealed class OrtOcrInferenceSession(InferenceSession session) : IOcrInferenceSession
{
    public string InputName { get; } = session.InputNames[0];

    /// <summary>ONNX Runtime 路线保持动态宽度。</summary>
    public int? FixedInputWidth => null;

    /// <summary>ONNX Runtime 路线保持动态高度。</summary>
    public int? FixedInputHeight => null;

    public OcrInferenceResult[] Run(Tensor<float> input)
    {
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results =
            session.Run([NamedOnnxValue.CreateFromTensor(InputName, input)]);
        return results.Select(value =>
        {
            var tensor = value.AsTensor<float>();
            return new OcrInferenceResult(tensor.Dimensions.ToArray(), tensor.ToArray());
        }).ToArray();
    }

    public void Dispose() => session.Dispose();
}
