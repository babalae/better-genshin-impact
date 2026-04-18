using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

public sealed class BgiRedNetPredictor : IDisposable
{
    private const int DefaultInputSize = 224;

    private static readonly float[] ImagenetMean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] ImagenetStd = [0.229f, 0.224f, 0.225f];

    private readonly InferenceSession _session;
    private readonly string[]? _labels;
    private readonly string _inputName;
    private readonly int _inputWidth;
    private readonly int _inputHeight;

    /// <summary>
    /// 使用 BgiOnnxFactory 创建这个类的实例
    /// </summary>
    internal BgiRedNetPredictor(BgiOnnxModel model, InferenceSession session, string? labelRelativePath = null)
    {
        _session = session;

        var input = _session.InputMetadata.FirstOrDefault();
        if (input.Key is null || input.Value is null)
        {
            throw new InvalidDataException("ONNX 模型输入信息为空");
        }

        _inputName = input.Key;
        var dimensions = input.Value.Dimensions;
        if (dimensions.Length < 4)
        {
            throw new InvalidDataException($"ONNX 模型输入维度不正确，预期 >=4，实际 {dimensions.Length}");
        }

        _inputHeight = dimensions[^2] > 0 ? dimensions[^2] : DefaultInputSize;
        _inputWidth = dimensions[^1] > 0 ? dimensions[^1] : DefaultInputSize;
        _labels = LoadLabels(labelRelativePath ?? Path.ChangeExtension(model.ModelRelativePath, ".labels.txt"));
    }

    public RedNetPrediction Predict(Image<Rgb24> image)
    {
        using var resized = image.Clone(ctx => ctx.Resize(_inputWidth, _inputHeight));
        var tensorInput = BuildInputTensor(resized);
        using var results = _session.Run([
            NamedOnnxValue.CreateFromTensor(_inputName, tensorInput)
        ]);

        var logits = results.First().AsEnumerable<float>().ToArray();
        if (logits.Length == 0)
        {
            throw new InvalidDataException("ONNX 模型输出为空");
        }

        var probabilities = Softmax(logits);
        var maxIndex = 0;
        var maxValue = probabilities[0];
        for (var i = 1; i < probabilities.Length; i++)
        {
            if (probabilities[i] <= maxValue) continue;
            maxValue = probabilities[i];
            maxIndex = i;
        }

        var label = maxIndex >= 0 && _labels is not null && maxIndex < _labels.Length
            ? _labels[maxIndex]
            : $"Class_{maxIndex}";
        return new RedNetPrediction(maxIndex, label, maxValue);
    }

    private DenseTensor<float> BuildInputTensor(Image<Rgb24> image)
    {
        var tensor = new DenseTensor<float>([1, 3, _inputHeight, _inputWidth]);
        for (var y = 0; y < _inputHeight; y++)
        {
            for (var x = 0; x < _inputWidth; x++)
            {
                var pixel = image[x, y];
                var r = pixel.R / 255f;
                var g = pixel.G / 255f;
                var b = pixel.B / 255f;

                tensor[0, 0, y, x] = (r - ImagenetMean[0]) / ImagenetStd[0];
                tensor[0, 1, y, x] = (g - ImagenetMean[1]) / ImagenetStd[1];
                tensor[0, 2, y, x] = (b - ImagenetMean[2]) / ImagenetStd[2];
            }
        }

        return tensor;
    }

    private static float[] Softmax(float[] logits)
    {
        var max = logits.Max();
        var result = new float[logits.Length];
        var sum = 0d;

        for (var i = 0; i < logits.Length; i++)
        {
            var value = Math.Exp(logits[i] - max);
            result[i] = (float)value;
            sum += value;
        }

        if (sum <= 0)
        {
            return result;
        }

        for (var i = 0; i < result.Length; i++)
        {
            result[i] = (float)(result[i] / sum);
        }

        return result;
    }

    private static string[]? LoadLabels(string labelRelativePath)
    {
        var labelAbsolutePath = Core.Config.Global.Absolute(labelRelativePath);
        if (!File.Exists(labelAbsolutePath))
        {
            return null;
        }

        var ext = Path.GetExtension(labelAbsolutePath);
        if (ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            var labels = JsonSerializer.Deserialize<string[]>(File.ReadAllText(labelAbsolutePath));
            return labels is { Length: > 0 } ? labels : null;
        }

        var lines = File.ReadAllLines(labelAbsolutePath)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .ToArray();
        return lines.Length > 0 ? lines : null;
    }

    public void Dispose()
    {
        _session.Dispose();
    }
}

public readonly record struct RedNetPrediction(int ClassIndex, string ClassLabel, float Confidence);
