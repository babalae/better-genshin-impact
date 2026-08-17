using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.ONNX;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.VisualBasic.FileIO;
using Newtonsoft.Json;
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
/// 元素分类输出顺序由 ONNX 自定义元数据 <c>element_types</c> 定义。
/// </remarks>
internal sealed class AvatarGridIconRecognizer : IDisposable
{
    private const int InputSize = 115;
    private const int ElementRoiSize = 48;
    private const int ElementInputSize = 64;
    private const string PrototypePath = @"Assets\Model\AvatarGridIcon\avatar.csv";
    private const string ElementTypesMetadataKey = "element_types";

    private readonly InferenceSession _session;
    private readonly List<AvatarPrototype> _prototypes;
    private readonly string[] _elementTypes;

    private sealed record AvatarPrototype(string CharacterName, string ElementType, string WeaponType, float[] Embedding);

    /// <summary>
    /// 初始化头像 ONNX 模型会话并加载角色头像原型表。
    /// </summary>
    public AvatarGridIconRecognizer()
    {
        _session = App.ServiceProvider.GetRequiredService<BgiOnnxFactory>()
            .CreateInferenceSession(BgiOnnxModel.AvatarGridIcon);
        try
        {
            _prototypes = LoadPrototypes();
            if (_prototypes.Count == 0)
            {
                throw new InvalidDataException("角色头像原型表为空。");
            }

            _elementTypes = ParseElementTypes(_session.ModelMetadata.CustomMetadataMap);
        }
        catch
        {
            _session.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 识别一个角色格子，返回相似度最高的角色候选。
    /// </summary>
    /// <param name="mat">角色头像格子裁剪图，BGR 格式；方法内部会 resize 到 115x115。</param>
    /// <returns>按角色名聚合后的最高分识别候选。</returns>
    public AvatarGridIconCandidate Recognize(Mat mat)
    {
        var (imageTensor, elementImageTensor) = CreateInputTensors(mat);
        return Recognize(imageTensor, elementImageTensor, false);
    }

    /// <summary>
    /// 使用独立裁剪的头像和元素图标识别角色；需要时以元素分类头覆盖原型表中的元素类型。
    /// </summary>
    public AvatarGridIconCandidate Recognize(Mat avatarMat, Mat elementMat, bool recognizeElementType)
    {
        using Mat resizedAvatar = avatarMat.Resize(new Size(InputSize, InputSize));
        using Mat resizedElement = elementMat.Resize(new Size(ElementInputSize, ElementInputSize));
        var imageTensor = CreateNormalizedRgbTensor(resizedAvatar);
        var elementImageTensor = CreateNormalizedRgbTensor(resizedElement);
        return Recognize(imageTensor, elementImageTensor, recognizeElementType);
    }

    private AvatarGridIconCandidate Recognize(
        DenseTensor<float> imageTensor,
        DenseTensor<float> elementImageTensor,
        bool recognizeElementType)
    {
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_image", imageTensor),
            NamedOnnxValue.CreateFromTensor("input_element_image", elementImageTensor)
        };
        using var results = _session.Run(inputs);
        var embedding = results.FirstOrDefault(r => r.Name == "embedding")
            ?? throw new InvalidDataException("角色头像模型输出缺少 embedding。");
        float[] feature = embedding.AsEnumerable<float>().ToArray();
        ValidateEmbeddingLength(feature, _prototypes[0].Embedding.Length, "角色头像模型输出向量");
        NormalizeVectorInPlace(feature, "角色头像模型输出向量");

        var candidate = _prototypes
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

        if (!recognizeElementType || candidate == AvatarGridIconCandidate.Empty)
        {
            return candidate;
        }

        var logits = results.First(result => result.Name == "element_logits").AsEnumerable<float>().ToArray();
        var predictedElementType = PredictElementType(logits, _elementTypes);
        return candidate with { ElementType = predictedElementType };
    }

    internal static string[] ParseElementTypes(IReadOnlyDictionary<string, string> metadata)
    {
        if (!metadata.TryGetValue(ElementTypesMetadataKey, out string? serializedElementTypes))
        {
            throw new InvalidOperationException(
                $"角色头像模型缺少必需的元数据：{ElementTypesMetadataKey}");
        }

        string[]? elementTypes;
        try
        {
            elementTypes = JsonConvert.DeserializeObject<string[]>(serializedElementTypes);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"角色头像模型元数据 {ElementTypesMetadataKey} 不是有效的 JSON 字符串数组。",
                exception);
        }

        if (elementTypes is null || elementTypes.Length == 0)
        {
            throw new InvalidOperationException(
                $"角色头像模型元数据 {ElementTypesMetadataKey} 必须是非空字符串数组。");
        }

        if (elementTypes.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException(
                $"角色头像模型元数据 {ElementTypesMetadataKey} 不能包含空标签。");
        }

        return elementTypes;
    }

    internal static string PredictElementType(IReadOnlyList<float> logits, IReadOnlyList<string> elementTypes)
    {
        if (logits.Count == 0 || logits.Count != elementTypes.Count)
        {
            throw new InvalidOperationException(
                $"角色头像模型元素输出数量异常：logits={logits.Count}, elements={elementTypes.Count}");
        }

        int predictedIndex = 0;
        for (int index = 1; index < logits.Count; index++)
        {
            if (logits[index] > logits[predictedIndex])
            {
                predictedIndex = index;
            }
        }
        return elementTypes[predictedIndex];
    }

    /// <summary>
    /// 按模型训练协议生成头像和元素图标两个输入张量。
    /// </summary>
    /// <remarks>
    /// 完整头像缩放为 115x115；元素输入必须从该图左上角裁剪 48x48 后再缩放为 64x64。
    /// 两个输入均执行 BGR→RGB 及 [-1,1] 归一化，不能用完整头像代替元素输入。
    /// </remarks>
    internal static (DenseTensor<float> Image, DenseTensor<float> ElementImage) CreateInputTensors(Mat mat)
    {
        using Mat resized = mat.Resize(new Size(InputSize, InputSize));
        using Mat elementRoi = resized.SubMat(0, ElementRoiSize, 0, ElementRoiSize);
        using Mat resizedElementRoi = elementRoi.Resize(new Size(ElementInputSize, ElementInputSize));
        return (CreateNormalizedRgbTensor(resized), CreateNormalizedRgbTensor(resizedElementRoi));
    }

    private static DenseTensor<float> CreateNormalizedRgbTensor(Mat bgr)
    {
        using Mat rgb = bgr.CvtColor(ColorConversionCodes.BGR2RGB);
        var tensor = new DenseTensor<float>(new[] { 1, 3, rgb.Height, rgb.Width });
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

        return tensor;
    }

    /// <summary>
    /// 获取指定角色的元素类型
    /// </summary>
    /// <param name="characterName">角色标准名称。</param>
    /// <returns>角色元素类型。</returns>
    public string GetElementType(string characterName)
    {
        return FindPrototype(characterName).ElementType;
    }

    /// <summary>
    /// 获取指定角色的武器筛选类型。
    /// </summary>
    /// <param name="characterName">角色标准名称。</param>
    /// <returns><c>avatar.csv</c> 的 <c>weapon_type</c>，例如“单手剑”。</returns>
    public string GetWeaponType(string characterName)
    {
        return FindPrototype(characterName).WeaponType;
    }

    /// <summary>
    /// 从 avatar.csv 加载角色头像原型向量。
    /// </summary>
    /// <returns>角色头像原型列表。</returns>
    private static List<AvatarPrototype> LoadPrototypes()
    {
        var prototypePath = Global.Absolute(PrototypePath);
        if (!File.Exists(prototypePath))
        {
            throw new FileNotFoundException($"角色头像原型表不存在：{prototypePath}", prototypePath);
        }

        using var parser = new TextFieldParser(prototypePath, Encoding.UTF8)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");

        var headers = parser.ReadFields()
            ?? throw new InvalidDataException("角色头像原型表缺少表头。");
        int characterNameIndex = RequireColumnIndex(headers, "character_name");
        int elementTypeIndex = RequireColumnIndex(headers, "element_type");
        int weaponTypeIndex = RequireColumnIndex(headers, "weapon_type");
        int embeddingIndex = RequireColumnIndex(headers, "embedding");
        int requiredColumnCount = new[] { characterNameIndex, elementTypeIndex, weaponTypeIndex, embeddingIndex }.Max() + 1;

        List<AvatarPrototype> prototypes = [];
        int? expectedEmbeddingLength = null;
        while (!parser.EndOfData)
        {
            var columns = parser.ReadFields()
                ?? throw new InvalidDataException($"角色头像原型表第 {parser.LineNumber} 行为空。");
            if (columns.Length < requiredColumnCount)
            {
                throw new InvalidDataException($"角色头像原型表第 {parser.LineNumber} 行列数不足。");
            }

            string characterName = columns[characterNameIndex].Trim();
            string elementType = columns[elementTypeIndex].Trim();
            string weaponType = columns[weaponTypeIndex].Trim();
            if (string.IsNullOrWhiteSpace(characterName)
                || string.IsNullOrWhiteSpace(elementType)
                || string.IsNullOrWhiteSpace(weaponType))
            {
                throw new InvalidDataException($"角色头像原型表第 {parser.LineNumber} 行存在空白角色、元素或武器字段。");
            }

            var bytes = Convert.FromBase64String(columns[embeddingIndex].Trim());
            if (bytes.Length == 0 || bytes.Length % sizeof(float) != 0)
            {
                throw new InvalidDataException($"角色头像原型表第 {parser.LineNumber} 行 embedding 长度无效。");
            }

            int totalFloats = bytes.Length / sizeof(float);
            if (expectedEmbeddingLength is null)
            {
                expectedEmbeddingLength = totalFloats;
            }
            else if (expectedEmbeddingLength.Value != totalFloats)
            {
                throw new InvalidDataException($"角色头像原型表第 {parser.LineNumber} 行 embedding 长度为 {totalFloats}，期望 {expectedEmbeddingLength.Value}。");
            }

            float[] embedding = new float[totalFloats];
            Buffer.BlockCopy(bytes, 0, embedding, 0, bytes.Length);
            NormalizeVectorInPlace(embedding, $"角色头像原型向量 {characterName}");
            prototypes.Add(new AvatarPrototype(characterName, elementType, weaponType, embedding));
        }

        return prototypes;
    }

    private AvatarPrototype FindPrototype(string characterName)
    {
        return _prototypes.FirstOrDefault(prototype => prototype.CharacterName == characterName)
            ?? throw new InvalidDataException($"角色头像原型表缺少角色：{characterName}");
    }

    private static int RequireColumnIndex(string[] headers, string columnName)
    {
        int index = Array.FindIndex(headers, h => string.Equals(h?.Trim(), columnName, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            throw new InvalidDataException($"角色头像原型表缺少列：{columnName}");
        }

        return index;
    }

    private static void ValidateEmbeddingLength(float[] vector, int expectedLength, string name)
    {
        if (vector.Length != expectedLength)
        {
            throw new InvalidDataException($"{name} 长度为 {vector.Length}，期望 {expectedLength}。");
        }
    }

    /// <summary>
    /// 对 embedding 向量执行 L2 归一化，便于后续用点积计算余弦相似度。
    /// </summary>
    /// <param name="vector">待归一化的向量。</param>
    /// <param name="name">异常消息中的向量名称。</param>
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

    /// <summary>
    /// 释放 ONNX 推理会话。
    /// </summary>
    public void Dispose()
    {
        _session.Dispose();
    }
}
