using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.ONNX;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.VisualBasic.FileIO;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 角色头像模型识别候选。
/// </summary>
/// <param name="CharacterName">角色标准名称。</param>
/// <param name="ElementType">角色元素类型，来自 avatar.csv 的 element_type。</param>
/// <param name="Score">候选与输入头像 embedding 的余弦相似度。</param>
internal sealed record AvatarGridIconCandidate(string CharacterName, string ElementType, double Score)
{
    /// <summary>
    /// 未匹配到有效候选时使用的空结果。
    /// </summary>
    public static readonly AvatarGridIconCandidate Empty = new(string.Empty, string.Empty, double.MinValue);
}

/// <summary>
/// 角色头像识别器。
/// </summary>
/// <remarks>
/// 使用 <c>Assets\Model\AvatarGridIcon\avatar.onnx</c> 提取头像特征，
/// 再与 <c>Assets\Model\AvatarGridIcon\avatar.csv</c> 中的角色原型向量做余弦相似度识别。
/// </remarks>
internal sealed class AvatarGridIconRecognizer : IDisposable
{
    private const int InputSize = 115;
    private const string PrototypePath = @"Assets\Model\AvatarGridIcon\avatar.csv";

    private readonly InferenceSession _session;
    private readonly List<AvatarPrototype> _prototypes;

    private sealed record AvatarPrototype(string CharacterName, string ElementType, string WeaponName, float[] Embedding);

    /// <summary>
    /// 初始化头像 ONNX 模型会话并加载角色头像原型表。
    /// </summary>
    public AvatarGridIconRecognizer()
    {
        _session = App.ServiceProvider.GetRequiredService<BgiOnnxFactory>()
            .CreateInferenceSession(BgiOnnxModel.AvatarGridIcon);
        _prototypes = LoadPrototypes();
    }

    /// <summary>
    /// 识别一个角色格子，返回相似度最高的角色候选。
    /// </summary>
    /// <param name="mat">角色头像格子裁剪图，BGR 格式；方法内部会 resize 到 115x115。</param>
    /// <returns>按角色名聚合后的最高分识别候选。</returns>
    public AvatarGridIconCandidate Recognize(Mat mat)
    {
        using Mat resized = mat.Resize(new Size(InputSize, InputSize));
        using Mat rgb = resized.CvtColor(ColorConversionCodes.BGR2RGB);
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
        float[] feature = results.First(r => r.Name == "embedding").AsEnumerable<float>().ToArray();
        NormalizeVectorInPlace(feature);

        return _prototypes
            // 两侧 embedding 都已 L2 归一化，点积即余弦相似度。
            .Select(prototype =>
            {
                double score = 0;
                for (int i = 0; i < feature.Length; i++)
                {
                    score += prototype.Embedding[i] * feature[i];
                }

                return new AvatarGridIconCandidate(prototype.CharacterName, prototype.ElementType, score);
            })
            .GroupBy(candidate => candidate.CharacterName)
            .Select(group => group.OrderByDescending(candidate => candidate.Score).First())
            .OrderByDescending(candidate => candidate.Score)
            .FirstOrDefault() ?? AvatarGridIconCandidate.Empty;
    }

    /// <summary>
    /// 获取指定角色的元素类型
    /// </summary>
    /// <param name="characterName">角色标准名称。</param>
    /// <returns>角色元素类型。</returns>
    public string GetElementType(string characterName)
    {
        return _prototypes.First(prototype => prototype.CharacterName == characterName).ElementType;
    }

    /// <summary>
    /// 获取指定角色的武器名称
    /// </summary>
    /// <param name="characterName">角色标准名称。</param>
    /// <returns>角色武器名称。</returns>
    public string GetWeaponName(string characterName)
    {
        return _prototypes.First(prototype => prototype.CharacterName == characterName).WeaponName;
    }

    /// <summary>
    /// 从 avatar.csv 加载角色头像原型向量。
    /// </summary>
    /// <returns>角色头像原型列表。</returns>
    private static List<AvatarPrototype> LoadPrototypes()
    {
        using var parser = new TextFieldParser(Global.Absolute(PrototypePath), Encoding.UTF8)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");

        var headers = parser.ReadFields()!;
        int characterNameIndex = Array.FindIndex(headers, h => string.Equals(h?.Trim(), "character_name", StringComparison.OrdinalIgnoreCase));
        int elementTypeIndex = Array.FindIndex(headers, h => string.Equals(h?.Trim(), "element_type", StringComparison.OrdinalIgnoreCase));
        int weaponTypeIndex = Array.FindIndex(headers, h => string.Equals(h?.Trim(), "weapon_type", StringComparison.OrdinalIgnoreCase));
        int embeddingIndex = Array.FindIndex(headers, h => string.Equals(h?.Trim(), "embedding", StringComparison.OrdinalIgnoreCase));

        List<AvatarPrototype> prototypes = [];
        while (!parser.EndOfData)
        {
            var columns = parser.ReadFields()!;
            var bytes = Convert.FromBase64String(columns[embeddingIndex].Trim());
            int totalFloats = bytes.Length / sizeof(float);
            float[] embedding = new float[totalFloats];
            Buffer.BlockCopy(bytes, 0, embedding, 0, bytes.Length);
            NormalizeVectorInPlace(embedding);
            prototypes.Add(new AvatarPrototype(
                columns[characterNameIndex].Trim(),
                columns[elementTypeIndex].Trim(),
                columns[weaponTypeIndex].Trim(),
                embedding));
        }

        return prototypes;
    }

    /// <summary>
    /// 对 embedding 向量执行 L2 归一化，便于后续用点积计算余弦相似度。
    /// </summary>
    /// <param name="vector">待归一化的向量。</param>
    private static void NormalizeVectorInPlace(float[] vector)
    {
        double norm2 = 0;
        foreach (float value in vector)
        {
            norm2 += (double)value * value;
        }

        double norm = Math.Sqrt(norm2);
        for (int i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)(vector[i] / norm);
        }
    }

    /// <summary>
    /// 释放 ONNX 推理会话。
    /// </summary>
    public void Dispose()
    {
        _session.Dispose();
    }
}
