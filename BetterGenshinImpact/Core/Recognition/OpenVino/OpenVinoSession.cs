using System;
using System.Linq;
using BetterGenshinImpact.Core.Recognition.OCR.Paddle;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenVinoSharp;
using OvTensor = OpenVinoSharp.Tensor;

namespace BetterGenshinImpact.Core.Recognition.OpenVino;

/// <summary>
///     OpenVINO（C# API）推理会话：直接加载现有 .onnx 模型文件（无需转换产物），
///     同一 (模型, 设备, 缓存) 组合复用进程内已编译模型；InferRequest 随会话创建、复用。
///     Det / Rec 自带会话锁，Run 内部无需再加锁；YOLO 侧另行串行化。
/// </summary>
internal sealed class OpenVinoSession : IOcrInferenceSession
{
    private readonly CompiledModel _compiledModel;
    private readonly InferRequest _inferRequest;
    private readonly ulong _outputCount;
    private bool _disposed;

    public OpenVinoSession(string modelPath, string deviceId, bool enableCache)
    {
        _compiledModel = OpenVinoCompiledModelCache.GetOrCompile(modelPath, deviceId, enableCache,
            OpenVinoRuntime.CacheDirectory);
        _inferRequest = _compiledModel.create_infer_request();
        _outputCount = Math.Max(1, _compiledModel.get_outputs_size());
        InputName = ResolveInputName();
    }

    public string InputName { get; }

    /// <summary>输入节点名：优先取模型里第一个输入的真实名称，匿名节点回落常量。</summary>
    private string ResolveInputName()
    {
        try
        {
            var name = _compiledModel.get_input(0).get_any_name();
            return string.IsNullOrWhiteSpace(name) ? "in0" : name;
        }
        catch
        {
            return "in0";
        }
    }

    public OcrInferenceResult[] Run(Tensor<float> input)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (input is not DenseTensor<float> dense)
            throw new InvalidOperationException("OCR 输入张量应为 DenseTensor<float>");
        var dims = input.Dimensions.ToArray();
        using var tensor = new OvTensor(new Shape(dims.Select(d => (long)d).ToArray()), ElementType.F32);
        // 归一化张量（池化缓冲）→ 原生张量的一次拷贝；与 ncnn 路线的 Buffer.CopyTo 语义一致。
        dense.Buffer.Span.CopyTo(tensor.get_span<float>());
        _inferRequest.set_input_tensor(tensor);
        _inferRequest.infer();
        return CollectOutputs();
    }

    /// <summary>按输出顺序收集全部输出张量（数据拷出副本，与 DML 释放语义同等对待）。</summary>
    private OcrInferenceResult[] CollectOutputs()
    {
        var results = new OcrInferenceResult[_outputCount];
        for (ulong i = 0; i < _outputCount; i++)
        {
            using var output = _inferRequest.get_output_tensor(i);
            var dims = output.Shape.get_dims().Select(d => (int)d).ToArray();
            var data = output.get_span<float>().ToArray();
            results[i] = new OcrInferenceResult(dims, data);
        }

        return results;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _inferRequest.Dispose();
    }
}
