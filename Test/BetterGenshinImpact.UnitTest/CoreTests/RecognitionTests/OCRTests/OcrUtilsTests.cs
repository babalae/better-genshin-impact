using BetterGenshinImpact.Core.Recognition.OCR.Engine;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests.OCRTests;

public class OcrUtilsTests
{
    #region CreateLabelDict

    [Fact]
    public void CreateLabelDict_SingleCharLabels_MapsCorrectly()
    {
        // 标签 ["a","b","c"] → a=1, b=2, c=3, " "=4
        IReadOnlyList<string> labels = ["a", "b", "c"];
        var dict = OcrUtils.CreateLabelDict(labels, out var lengths);

        Assert.Equal(1, dict["a"]);
        Assert.Equal(2, dict["b"]);
        Assert.Equal(3, dict["c"]);
        Assert.Equal(4, dict[" "]);
        // 所有标签都是长度1，labelLengths = [1]
        Assert.Single(lengths);
        Assert.Equal(1, lengths[0]);
    }

    [Fact]
    public void CreateLabelDict_NoZeroLength()
    {
        // 不应包含长度为0的项（防止无限循环）
        IReadOnlyList<string> labels = ["x", "y"];
        OcrUtils.CreateLabelDict(labels, out var lengths);
        Assert.DoesNotContain(0, lengths);
    }

    [Fact]
    public void CreateLabelDict_LengthsDescendingOrder()
    {
        // 多字节标签时，labelLengths 应降序排列（先试长匹配）
        IReadOnlyList<string> labels = ["a", "ab", "b"];
        OcrUtils.CreateLabelDict(labels, out var lengths);
        for (var i = 0; i < lengths.Length - 1; i++)
        {
            Assert.True(lengths[i] >= lengths[i + 1], "labelLengths 应为降序");
        }
    }

    #endregion

    #region MapStringToLabelIndices

    [Fact]
    public void MapStringToLabelIndices_SimpleMatch()
    {
        // labels: ["a","b","c"] → a=1, b=2, c=3
        IReadOnlyList<string> labels = ["a", "b", "c"];
        var dict = OcrUtils.CreateLabelDict(labels, out var lengths);

        var result = OcrUtils.MapStringToLabelIndices("abc", dict, lengths);

        Assert.Equal([1, 2, 3], result);
    }

    [Fact]
    public void MapStringToLabelIndices_SkipsUnknownChars()
    {
        // "aXb" 中 X 不在标签里，应被跳过
        IReadOnlyList<string> labels = ["a", "b"];
        var dict = OcrUtils.CreateLabelDict(labels, out var lengths);

        var result = OcrUtils.MapStringToLabelIndices("aXb", dict, lengths);

        Assert.Equal([1, 2], result);
    }

    [Fact]
    public void MapStringToLabelIndices_PrefersLongerMatch()
    {
        // 标签含 "ab" 和 "a"，输入 "ab" 应优先匹配长标签 "ab"
        IReadOnlyList<string> labels = ["a", "ab", "b"];
        var dict = OcrUtils.CreateLabelDict(labels, out var lengths);

        var result = OcrUtils.MapStringToLabelIndices("ab", dict, lengths);

        // "ab" 整体匹配为 index 2（labels 中第2个元素）
        Assert.Single(result);
        Assert.Equal(2, result[0]);
    }

    [Fact]
    public void MapStringToLabelIndices_EmptyString_ReturnsEmpty()
    {
        IReadOnlyList<string> labels = ["a", "b"];
        var dict = OcrUtils.CreateLabelDict(labels, out var lengths);

        var result = OcrUtils.MapStringToLabelIndices("", dict, lengths);

        Assert.Empty(result);
    }

    [Fact]
    public void MapStringToLabelIndices_AllUnknown_ReturnsEmpty()
    {
        IReadOnlyList<string> labels = ["a", "b"];
        var dict = OcrUtils.CreateLabelDict(labels, out var lengths);

        var result = OcrUtils.MapStringToLabelIndices("XYZ", dict, lengths);

        Assert.Empty(result);
    }

    [Fact]
    public void MapStringToLabelIndices_SpaceChar_MapsToSpaceIndex()
    {
        // 空格字符映射到 labels.Count + 1
        IReadOnlyList<string> labels = ["a", "b"];
        var dict = OcrUtils.CreateLabelDict(labels, out var lengths);

        var result = OcrUtils.MapStringToLabelIndices("a b", dict, lengths);

        // a=1, " "=3, b=2
        Assert.Equal([1, 3, 2], result);
    }

    #endregion

    #region GetMaxScoreDP

    [Fact]
    public void GetMaxScoreDP_PerfectMatch_ReturnsFullScore()
    {
        // result 中按顺序包含 target 的所有元素，置信度均为 1.0
        (int, float)[] result = [(1, 1.0f), (2, 1.0f), (3, 1.0f)];
        int[] target = [1, 2, 3];

        var score = OcrUtils.GetMaxScoreDp(result, target, target.Length);

        Assert.Equal(1.0, score);
    }

    [Fact]
    public void GetMaxScoreDP_NoMatch_ReturnsZero()
    {
        // result 中不包含 target 的任何元素
        (int, float)[] result = [(4, 1.0f), (5, 1.0f)];
        int[] target = [1, 2];

        var score = OcrUtils.GetMaxScoreDp(result, target, target.Length);

        Assert.Equal(0, score);
    }

    [Fact]
    public void GetMaxScoreDP_EmptyTarget_ReturnsZero()
    {
        (int, float)[] result = [(1, 1.0f)];
        int[] target = [];

        var score = OcrUtils.GetMaxScoreDp(result, target, 1);

        Assert.Equal(0, score);
    }

    [Fact]
    public void GetMaxScoreDP_PartialMatch_ReturnsZero()
    {
        // target 需要 [1,2,3]，但 result 只有 [1,2]，无法完整匹配
        (int, float)[] result = [(1, 1.0f), (2, 1.0f)];
        int[] target = [1, 2, 3];

        var score = OcrUtils.GetMaxScoreDp(result, target, target.Length);

        Assert.Equal(0, score);
    }

    [Fact]
    public void GetMaxScoreDP_SubsequenceMatch_SkipsNoise()
    {
        // result 中有噪声，但子序列 [1,2,3] 可匹配
        (int, float)[] result = [(9, 0.5f), (1, 0.8f), (9, 0.3f), (2, 0.9f), (3, 0.7f)];
        int[] target = [1, 2, 3];

        var score = OcrUtils.GetMaxScoreDp(result, target, target.Length);

        // (0.8 + 0.9 + 0.7) / 3 = 0.8
        Assert.Equal(0.8, score, 0.01);
    }

    [Fact]
    public void GetMaxScoreDP_PicksBestConfidence()
    {
        // target [1]，result 中有两个 index=1，应选置信度最高的
        (int, float)[] result = [(1, 0.3f), (1, 0.9f)];
        int[] target = [1];

        var score = OcrUtils.GetMaxScoreDp(result, target, 1);

        Assert.Equal(0.9, score, 0.01);
    }

    [Fact]
    public void GetMaxScoreDP_NormalizesWithAvailableCount()
    {
        // availableCount > target.Length 时分数被稀释
        (int, float)[] result = [(1, 1.0f), (2, 1.0f)];
        int[] target = [1, 2];

        var score = OcrUtils.GetMaxScoreDp(result, target, 4);

        // (1.0 + 1.0) / 4 = 0.5
        Assert.Equal(0.5, score, 0.01);
    }

    [Fact]
    public void GetMaxScoreDP_ManyFrames_TargetLengthDenominator_ScoresHigh()
    {
        // 模拟多个文字区域的字符帧合并后做匹配，分母应为 target.Length
        // 即使有很多噪声帧，只要 target 完整匹配，分数仍应很高
        (int, float)[] result = [
            (9, 0.5f), (8, 0.6f), (7, 0.4f),  // 噪声区域1
            (1, 0.9f), (2, 0.85f),              // 匹配目标 [1,2]
            (6, 0.7f), (5, 0.3f), (4, 0.5f),   // 噪声区域2
            (9, 0.2f), (8, 0.4f)                // 噪声区域3
        ];
        int[] target = [1, 2];

        // 使用 target.Length 作为分母：(0.9 + 0.85) / 2 = 0.875
        var score = OcrUtils.GetMaxScoreDp(result, target, target.Length);

        Assert.Equal(0.875, score, 0.01);
    }

    #endregion

    #region CreateWeights

    [Fact]
    public void CreateWeights_DefaultsToOne()
    {
        IReadOnlyList<string> labels = ["a", "b", "c"];
        var labelDict = OcrUtils.CreateLabelDict(labels, out _);
        var weights = OcrUtils.CreateWeights(new Dictionary<string, float>(), labelDict, labels.Count);

        // labels.Count + 2 = 5
        Assert.Equal(5, weights.Length);
        Assert.All(weights, w => Assert.Equal(1.0f, w));
    }

    [Fact]
    public void CreateWeights_AppliesExtraWeights()
    {
        IReadOnlyList<string> labels = ["a", "b", "c"];
        var extra = new Dictionary<string, float> { { "b", 2.5f } };
        var labelDict = OcrUtils.CreateLabelDict(labels, out _);

        var weights = OcrUtils.CreateWeights(extra, labelDict, labels.Count);

        // "b" 是 labels[1]，index=2
        Assert.Equal(1.0f, weights[1]); // "a"
        Assert.Equal(2.5f, weights[2]); // "b"
        Assert.Equal(1.0f, weights[3]); // "c"
    }

    [Fact]
    public void CreateWeights_IgnoresUnknownKeys()
    {
        IReadOnlyList<string> labels = ["a", "b"];
        var extra = new Dictionary<string, float> { { "z", 5.0f } };
        var labelDict = OcrUtils.CreateLabelDict(labels, out _);

        var weights = OcrUtils.CreateWeights(extra, labelDict, labels.Count);

        Assert.All(weights, w => Assert.Equal(1.0f, w));
    }

    [Fact]
    public void CreateWeights_SpaceKey_MapsToCorrectIndex()
    {
        // 空格权重应写入 labels.Count + 1 位置，与 CreateLabelDict 一致
        IReadOnlyList<string> labels = ["a", " ", "b"];
        var extra = new Dictionary<string, float> { { " ", 3.0f } };
        var labelDict = OcrUtils.CreateLabelDict(labels, out _);

        var weights = OcrUtils.CreateWeights(extra, labelDict, labels.Count);

        // labels.Count + 1 = 4，空格权重应在 weights[4]
        Assert.Equal(3.0f, weights[labels.Count + 1]);
        // labels 中 " " 的位置 index=2（即 weights[2]）不应被错误写入
        Assert.Equal(1.0f, weights[2]);
    }

    #endregion
}
