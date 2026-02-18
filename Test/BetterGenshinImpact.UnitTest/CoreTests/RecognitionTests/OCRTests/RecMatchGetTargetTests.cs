using BetterGenshinImpact.Core.Recognition.OCR.Engine;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests.OCRTests;

public class RecMatchGetTargetTests
{
    // 用 CreateLabelDict 构造一个最小标签环境来测试 GetTarget 的算法
    // 因为 GetTarget 只依赖 _labelDict 和 _labelLengths，可以通过 OcrUtils.CreateLabelDict 直接测试

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
}
