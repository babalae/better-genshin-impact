using System;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace BetterGenshinImpact.Core.Recognition.OCR.Paddle;

/// <summary>
///     OCR 检测 / 识别模型的推理会话抽象（推理后端插件化的落点之一）：
///     任意后端只要提供本接口，就能复用 Det / Rec 的全部预处理与后处理，只有会话实现不同。
///     实现方负责线程安全语义说明：Det / Rec 自带会话锁，Run 内部无需再加锁。
/// </summary>
internal interface IOcrInferenceSession : IDisposable
{
    /// <summary>模型输入节点名。</summary>
    string InputName { get; }

    /// <summary>运行一次推理，按输出节点顺序返回每个输出的形状与数据副本（DML 释放语义下数据必须在锁内拷出）。</summary>
    OcrInferenceResult[] Run(Tensor<float> input);
}

/// <summary>单个输出张量的形状与数据副本。</summary>
internal sealed record OcrInferenceResult(int[] Shape, float[] Data);

/// <summary>ONNX Runtime 会话适配器：Det / Rec 原生路径（含 OpenVINO 等执行提供程序）。</summary>
internal sealed class OrtOcrInferenceSession(InferenceSession session) : IOcrInferenceSession
{
    public string InputName { get; } = session.InputNames[0];

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
