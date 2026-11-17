using System;
using System.Linq;
using BetterGenshinImpact.Core.Recognition.OCR.Paddle;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenVinoSharp;
using OvTensor = OpenVinoSharp.Tensor;

namespace BetterGenshinImpact.Core.Recognition.OpenVino;

/// <summary>
///     OpenVINO（C# API）推理会话：直接加载现有 .onnx 模型文件（无需转换产物），
///     同一 (模型, 设备, 缓存, 固定形状) 组合复用进程内已编译模型；InferRequest 随会话创建、复用。
///     Det / Rec 自带会话锁，Run 内部无需再加锁；YOLO 侧另行串行化。
///     传入 <c>fixedInputShape</c> 时模型按静态形状编译（NPU 要求），小于固定形状的输入自动补零边。
/// </summary>
internal sealed class OpenVinoSession : IOcrInferenceSession
{
    private readonly CompiledModel _compiledModel;
    private readonly InferRequest _inferRequest;
    private readonly ulong _outputCount;
    private readonly long[]? _fixedInputShape;
    private bool _disposed;

    /// <param name="fixedInputShape">
    ///     可选的固定输入形状（如 rec 的 [1,3,48,320]）：编译前 reshape 模型，推理时把不足部分补零。
    ///     为 null 时保持动态形状（CPU 路线，或 GPU 显式关闭宽度桶时）。
    /// </param>
    public OpenVinoSession(string modelPath, string deviceId, bool enableCache, long[]? fixedInputShape = null)
    {
        _fixedInputShape = fixedInputShape;
        _compiledModel = OpenVinoCompiledModelCache.GetOrCompile(modelPath, deviceId, enableCache,
            OpenVinoRuntime.CacheDirectory, fixedInputShape);
        _inferRequest = _compiledModel.create_infer_request();
        _outputCount = Math.Max(1, _compiledModel.get_outputs_size());
        InputName = ResolveInputName();
    }

    public string InputName { get; }

    /// <summary>固定形状的最后一维宽度（供 Rec 预处理压宽）；动态形状为 null。</summary>
    public int? FixedInputWidth => _fixedInputShape == null ? null : (int)_fixedInputShape[^1];

    /// <summary>固定形状的高度（NCHW 的 H，供 Det 静态画布分支判定）；动态形状为 null。</summary>
    public int? FixedInputHeight => _fixedInputShape == null ? null : (int)_fixedInputShape[2];

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
        using var tensor = CreateInputTensor(dense, dims);
        _inferRequest.set_input_tensor(tensor);
        _inferRequest.infer();
        return CollectOutputs();
    }

    /// <summary>
    ///     建原生输入张量并拷入数据：动态形状按输入实际维度；
    ///     固定形状不足部分补零（与 PaddleOCR 官方 rec 预处理的零 padding 一致，
    ///     CTC 解码对补零区输出 blank，不影响识别结果）。
    /// </summary>
    private OvTensor CreateInputTensor(DenseTensor<float> dense, int[] dims)
    {
        if (_fixedInputShape == null)
        {
            var tensor = new OvTensor(new Shape(dims.Select(d => (long)d).ToArray()), ElementType.F32);
            // 归一化张量（池化缓冲）→ 原生张量的一次拷贝；与 ncnn 路线的 Buffer.CopyTo 语义一致。
            dense.Buffer.Span.CopyTo(tensor.get_span<float>());
            return tensor;
        }

        if (dims.Length != _fixedInputShape.Length)
            throw new InvalidOperationException(
                $"固定形状推理要求输入维数一致：期望 {_fixedInputShape.Length} 维，实际 {dims.Length} 维");
        var padded = new OvTensor(new Shape(_fixedInputShape), ElementType.F32);
        var destination = padded.get_span<float>();
        destination.Clear();
        if (dims.Zip(_fixedInputShape).All(pair => pair.First == pair.Second))
        {
            dense.Buffer.Span.CopyTo(destination);
            return padded;
        }

        // 逐行拷贝：当前仅支持最后一维（NCHW 的宽度）不足，其余维必须与固定形状一致。
        for (var i = 0; i < dims.Length - 1; i++)
        {
            if (dims[i] != _fixedInputShape[i])
                throw new InvalidOperationException(
                    $"固定形状推理仅支持最后一维补零：第 {i} 维 {dims[i]} != {_fixedInputShape[i]}");
        }

        var source = dense.Buffer.Span;
        var rowLength = dims[^1];
        var fixedRowLength = (int)_fixedInputShape[^1];
        if (rowLength > fixedRowLength)
            throw new InvalidOperationException(
                $"输入超出固定形状：最后一维 {rowLength} > {_fixedInputShape[^1]}，请调大固定宽度配置");
        var rowCount = source.Length / rowLength;
        for (var row = 0; row < rowCount; row++)
        {
            source.Slice(row * rowLength, rowLength).CopyTo(destination.Slice(row * fixedRowLength, rowLength));
        }

        return padded;
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
