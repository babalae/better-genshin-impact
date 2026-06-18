using BetterGenshinImpact.Core.Config;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.VisualBasic.FileIO;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BetterGenshinImpact.GameTask.Common.Reward;

/// <summary>
/// 奖励图标 ONNX 匹配候选。
/// </summary>
/// <param name="Name">候选名称。</param>
/// <param name="Score">匹配分数。</param>
/// <param name="QualityLevel">候选稀有度；未知或不支持时为 -1。</param>
internal sealed record RewardIconCandidate(string Name, double Score, int QualityLevel)
{
    /// <summary>
    /// 无有效匹配时的空候选。
    /// </summary>
    public static readonly RewardIconCandidate Empty = new(string.Empty, double.MinValue, -1);
}

/// <summary>
/// 奖励图标 embedding 匹配器。
/// </summary>
internal sealed class RewardIconMatcher
{
    private const int InputSize = 125;

    private readonly InferenceSession _session;
    private readonly List<IconPrototype> _prototypes;

    /// <summary>
    /// 从原型表加载的图标特征。
    /// </summary>
    /// <param name="Name">显示名称。</param>
    /// <param name="QualityLevel">圣遗物稀有度；非圣遗物为 -1。</param>
    /// <param name="IsRelic">是否为圣遗物。</param>
    /// <param name="Embedding">图标特征向量。</param>
    private sealed record IconPrototype(string Name, int QualityLevel, bool IsRelic, float[] Embedding);

    /// <summary>
    /// 初始化奖励图标模型和原型表。
    /// </summary>
    internal RewardIconMatcher()
    {
        _session = new InferenceSession(Global.Absolute(@"Assets\Model\ItemV2\item.onnx"));
        _prototypes = LoadIconPrototypes();
    }

    /// <summary>
    /// 返回奖励图标模型的最高分匹配结果。
    /// </summary>
    /// <param name="mat">125×125 的 BGR 图标图像。</param>
    /// <returns>最高分图标候选。</returns>
    /// <exception cref="ArgumentOutOfRangeException">图标尺寸不是 125×125。</exception>
    internal RewardIconCandidate Match(Mat mat)
    {
        if (mat.Size().Width != InputSize || mat.Size().Height != InputSize)
        {
            throw new ArgumentOutOfRangeException(nameof(mat), $"输入图标尺寸必须为 {InputSize}x{InputSize}。");
        }

        using Mat rgb = mat.CvtColor(ColorConversionCodes.BGR2RGB);
        var tensor = new DenseTensor<float>(new[] { 1, 3, InputSize, InputSize });
        for (int y = 0; y < rgb.Height; y++)
        {
            for (int x = 0; x < rgb.Width; x++)
            {
                var pixel = rgb.At<Vec3b>(y, x);
                tensor[0, 0, y, x] = (pixel[0] / 255f - 0.5f) / 0.5f;
                tensor[0, 1, y, x] = (pixel[1] / 255f - 0.5f) / 0.5f;
                tensor[0, 2, y, x] = (pixel[2] / 255f - 0.5f) / 0.5f;
            }
        }

        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("input_image", tensor) };
        using var results = _session.Run(inputs);
        var embedding = results.First(r => r.Name == "embedding");
        float[] feature = embedding.AsEnumerable<float>().ToArray();
        NormalizeVectorInPlace(feature, "奖励图标模型特征向量");

        return _prototypes
            // 每个原型与模型特征做点积；两侧都已 L2 归一化，点积即余弦相似度。
            .Select(prototype =>
            {
                double score = 0;
                for (int i = 0; i < feature.Length; i++)
                {
                    score += prototype.Embedding[i] * feature[i];
                }

                return new
                {
                    prototype.Name,
                    prototype.QualityLevel,
                    prototype.IsRelic,
                    Score = score
                };
            })
            // 圣遗物需要保留同名不同星级；其它奖励不支持稀有度检测。
            .GroupBy(c => new
            {
                c.Name,
                QualityLevel = c.IsRelic ? c.QualityLevel : -1
            })
            // 每个显示名只保留最高分原型作为该奖励得分。
            .Select(g => g.OrderByDescending(c => c.Score).First())
            .OrderByDescending(c => c.Score)
            .Select(c => new RewardIconCandidate(c.Name, c.Score, c.IsRelic ? c.QualityLevel : -1))
            .FirstOrDefault() ?? RewardIconCandidate.Empty;
    }

    /// <summary>
    /// 从当前图标原型 CSV 加载图标特征。
    /// </summary>
    /// <returns>图标原型列表。</returns>
    private static List<IconPrototype> LoadIconPrototypes()
    {
        using var parser = new TextFieldParser(Global.Absolute(@"Assets\Model\ItemV2\item.csv"), Encoding.UTF8)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");

        var headers = parser.ReadFields()!;
        int nameIndex = Array.FindIndex(headers, h => string.Equals(h?.Trim(), "item_name", StringComparison.OrdinalIgnoreCase));
        int itemClassIdIndex = Array.FindIndex(headers, h => string.Equals(h?.Trim(), "item_class_id", StringComparison.OrdinalIgnoreCase));
        int qualityLevelIndex = Array.FindIndex(headers, h => string.Equals(h?.Trim(), "quality_level", StringComparison.OrdinalIgnoreCase));
        int embeddingIndex = Array.FindIndex(headers, h => string.Equals(h?.Trim(), "embedding", StringComparison.OrdinalIgnoreCase));

        List<IconPrototype> prototypes = [];
        while (!parser.EndOfData)
        {
            var columns = parser.ReadFields()!;
            prototypes.Add(ParseIconPrototype(columns, nameIndex, itemClassIdIndex, qualityLevelIndex, embeddingIndex));
        }

        return prototypes;
    }

    /// <summary>
    /// 将 CSV 行解析为图标原型。
    /// </summary>
    /// <param name="columns">CSV 当前行字段。</param>
    /// <param name="nameIndex">名称列索引。</param>
    /// <param name="itemClassIdIndex">分类列索引。</param>
    /// <param name="qualityLevelIndex">稀有度列索引。</param>
    /// <param name="embeddingIndex">embedding 列索引。</param>
    /// <returns>图标原型。</returns>
    private static IconPrototype ParseIconPrototype(string[] columns, int nameIndex, int itemClassIdIndex, int qualityLevelIndex, int embeddingIndex)
    {
        string name = columns[nameIndex].Trim();
        string classId = columns[itemClassIdIndex].Trim();
        bool isRelic = classId.StartsWith("relic:", StringComparison.OrdinalIgnoreCase);
        int qualityLevel = isRelic && int.TryParse(columns[qualityLevelIndex].Trim(), out var parsedQualityLevel)
            ? parsedQualityLevel
            : -1;
        string embedding = columns[embeddingIndex].Trim();
        var bytes = Convert.FromBase64String(embedding);
        int totalFloats = bytes.Length / sizeof(float);
        float[] flatData = new float[totalFloats];
        Buffer.BlockCopy(bytes, 0, flatData, 0, bytes.Length);
        NormalizeVectorInPlace(flatData, $"奖励图标原型向量 {name}");
        return new IconPrototype(name, qualityLevel, isRelic, flatData);
    }

    /// <summary>
    /// 对向量做 L2 归一化。
    /// </summary>
    /// <param name="vector">待归一化的向量。</param>
    /// <param name="name">异常消息中的向量名称。</param>
    /// <exception cref="InvalidDataException">向量 L2 范数为 0。</exception>
    private static void NormalizeVectorInPlace(float[] vector, string name)
    {
        double norm2 = 0;
        foreach (float value in vector)
        {
            norm2 += (double)value * value;
        }

        double norm = Math.Sqrt(norm2);
        if (norm <= 1e-12)
        {
            throw new InvalidDataException($"{name} 的 L2 范数为 0。");
        }

        for (int i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)(vector[i] / norm);
        }
    }
}
