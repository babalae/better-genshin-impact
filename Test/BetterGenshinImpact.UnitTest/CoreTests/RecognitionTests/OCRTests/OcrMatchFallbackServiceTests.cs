using BetterGenshinImpact.Core.Recognition.OCR;
using OpenCvSharp;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests.OCRTests;

public class OcrMatchFallbackServiceTests
{
    #region LevenshteinDistance

    [Fact]
    public void LevenshteinDistance_IdenticalStrings_ReturnsZero()
    {
        Assert.Equal(0, OcrMatchFallbackService.LevenshteinDistance("abc", "abc"));
    }

    [Fact]
    public void LevenshteinDistance_EmptyAndNonEmpty_ReturnsLength()
    {
        Assert.Equal(3, OcrMatchFallbackService.LevenshteinDistance("", "abc"));
        Assert.Equal(3, OcrMatchFallbackService.LevenshteinDistance("abc", ""));
    }

    [Fact]
    public void LevenshteinDistance_BothEmpty_ReturnsZero()
    {
        Assert.Equal(0, OcrMatchFallbackService.LevenshteinDistance("", ""));
    }

    [Fact]
    public void LevenshteinDistance_SingleSubstitution()
    {
        // "确认" vs "确忍" — 一个字符替换
        Assert.Equal(1, OcrMatchFallbackService.LevenshteinDistance("确认", "确忍"));
    }

    [Fact]
    public void LevenshteinDistance_Insertion()
    {
        Assert.Equal(1, OcrMatchFallbackService.LevenshteinDistance("ac", "abc"));
    }

    [Fact]
    public void LevenshteinDistance_Deletion()
    {
        Assert.Equal(1, OcrMatchFallbackService.LevenshteinDistance("abc", "ac"));
    }

    [Fact]
    public void LevenshteinDistance_CompletelyDifferent()
    {
        Assert.Equal(3, OcrMatchFallbackService.LevenshteinDistance("abc", "xyz"));
    }

    #endregion

    #region ComputeTextSimilarity

    [Fact]
    public void ComputeTextSimilarity_ExactMatch_ReturnsOne()
    {
        Assert.Equal(1.0, OcrMatchFallbackService.ComputeTextSimilarity("确认", "确认"));
    }

    [Fact]
    public void ComputeTextSimilarity_TextContainsTarget_ReturnsOne()
    {
        // "确认购买" 包含 "确认"
        Assert.Equal(1.0, OcrMatchFallbackService.ComputeTextSimilarity("确认购买", "确认"));
    }

    [Fact]
    public void ComputeTextSimilarity_TargetContainsText_ReturnsRatio()
    {
        // "确认" 被 "确认购买" 包含，长度比 = 2/4
        Assert.Equal(0.5, OcrMatchFallbackService.ComputeTextSimilarity("确认", "确认购买"));
    }

    [Fact]
    public void ComputeTextSimilarity_EmptyTarget_ReturnsOne()
    {
        Assert.Equal(1.0, OcrMatchFallbackService.ComputeTextSimilarity("任意文字", ""));
    }

    [Fact]
    public void ComputeTextSimilarity_EmptyText_ReturnsZero()
    {
        Assert.Equal(0.0, OcrMatchFallbackService.ComputeTextSimilarity("", "确认"));
    }

    [Fact]
    public void ComputeTextSimilarity_SingleCharDifference()
    {
        // "确忍" vs "确认" — 距离1, 最大长度2, 相似度 = 1 - 1/2 = 0.5
        Assert.Equal(0.5, OcrMatchFallbackService.ComputeTextSimilarity("确忍", "确认"));
    }

    [Fact]
    public void ComputeTextSimilarity_CompletelyDifferent_ReturnsZero()
    {
        // 完全不同的字符串
        Assert.Equal(0.0, OcrMatchFallbackService.ComputeTextSimilarity("甲乙", "丙丁"));
    }

    #endregion

    #region OcrMatch / OcrMatchDirect 集成测试（使用 FakeOcrService）

    [Fact]
    public void OcrMatch_WhenRegionContainsTarget_ReturnsOne()
    {
        var fakeOcr = new FakeOcrService(new OcrResult([
            new OcrResultRegion(default, "确认购买", 0.9f)
        ]));
        var sut = new OcrMatchFallbackService(fakeOcr);

        using var mat = new Mat(50, 200, MatType.CV_8UC3, Scalar.White);
        var score = sut.OcrMatch(mat, "确认");

        Assert.Equal(1.0, score);
    }

    [Fact]
    public void OcrMatch_MultipleRegions_ReturnsBestScore()
    {
        var fakeOcr = new FakeOcrService(new OcrResult([
            new OcrResultRegion(default, "其他文字", 0.9f),
            new OcrResultRegion(default, "确认", 0.9f)
        ]));
        var sut = new OcrMatchFallbackService(fakeOcr);

        using var mat = new Mat(50, 200, MatType.CV_8UC3, Scalar.White);
        var score = sut.OcrMatch(mat, "确认");

        Assert.Equal(1.0, score);
    }

    [Fact]
    public void OcrMatch_NoRegions_ReturnsZero()
    {
        var fakeOcr = new FakeOcrService(new OcrResult([]));
        var sut = new OcrMatchFallbackService(fakeOcr);

        using var mat = new Mat(50, 200, MatType.CV_8UC3, Scalar.White);
        var score = sut.OcrMatch(mat, "确认");

        Assert.Equal(0.0, score);
    }

    [Fact]
    public void OcrMatchDirect_ExactMatch_ReturnsOne()
    {
        var fakeOcr = new FakeOcrService(ocrWithoutDetectorResult: "确认");
        var sut = new OcrMatchFallbackService(fakeOcr);

        using var mat = new Mat(50, 200, MatType.CV_8UC3, Scalar.White);
        var score = sut.OcrMatchDirect(mat, "确认");

        Assert.Equal(1.0, score);
    }

    [Fact]
    public void OcrMatchDirect_PartialMatch_ReturnsPartialScore()
    {
        var fakeOcr = new FakeOcrService(ocrWithoutDetectorResult: "确忍");
        var sut = new OcrMatchFallbackService(fakeOcr);

        using var mat = new Mat(50, 200, MatType.CV_8UC3, Scalar.White);
        var score = sut.OcrMatchDirect(mat, "确认");

        Assert.Equal(0.5, score, 0.01);
    }

    #endregion

    /// <summary>
    /// 用于测试 OcrMatchFallbackService 的假 IOcrService。
    /// </summary>
    private class FakeOcrService : IOcrService
    {
        private readonly OcrResult? _ocrResult;
        private readonly string _ocrWithoutDetectorResult;

        public FakeOcrService(OcrResult? ocrResult = null, string ocrWithoutDetectorResult = "")
        {
            _ocrResult = ocrResult;
            _ocrWithoutDetectorResult = ocrWithoutDetectorResult;
        }

        public string Ocr(Mat mat) => _ocrResult?.Text ?? "";

        public string OcrWithoutDetector(Mat mat) => _ocrWithoutDetectorResult;

        public OcrResult OcrResult(Mat mat) => _ocrResult ?? new OcrResult([]);
    }
}
