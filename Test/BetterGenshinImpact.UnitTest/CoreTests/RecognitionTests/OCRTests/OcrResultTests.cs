using BetterGenshinImpact.Core.Recognition.OCR;
using OpenCvSharp;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests.OCRTests;

public class OcrResultTests
{
    [Fact]
    public void Text_PreservesOfficialDetectionOrder()
    {
        var result = new OcrResult(
        [
            new OcrResultRegion(CreateRect(100), "first", 1),
            new OcrResultRegion(CreateRect(10), "second", 1)
        ]);

        Assert.Equal("first\nsecond", result.Text);
    }

    private static RotatedRect CreateRect(float centerY)
    {
        return new RotatedRect(new Point2f(10, centerY), new Size2f(20, 10), 0);
    }
}
