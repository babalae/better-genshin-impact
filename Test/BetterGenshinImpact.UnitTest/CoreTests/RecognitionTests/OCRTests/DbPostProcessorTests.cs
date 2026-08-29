using BetterGenshinImpact.Core.Recognition.OCR.Paddle;
using OpenCvSharp;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests.OCRTests;

public class DbPostProcessorTests
{
    [Fact]
    public void Run_HighConfidenceRectangle_ReturnsExpandedOrderedBox()
    {
        using var pred = CreateProbabilityMap(1.0);
        var processor = new DbPostProcessor(0.3f, 0.6f, 1000, 1.5f, 3, false);

        var results = processor.Run(pred, pred.Size());

        var result = Assert.Single(results);
        Assert.True(result.Score > 0.99f);
        Assert.Equal(4, result.Points.Length);
        Assert.True(result.Rect.Size.Width > 50 || result.Rect.Size.Height > 50);
        Assert.True(result.Rect.Size.Width > 20 && result.Rect.Size.Height > 20);
        Assert.True(result.Points[0].X <= result.Points[1].X);
        Assert.True(result.Points[0].Y <= result.Points[3].Y);
    }

    [Fact]
    public void Run_ScoreBelowBoxThreshold_ReturnsEmpty()
    {
        using var pred = CreateProbabilityMap(0.5);
        var processor = new DbPostProcessor(0.3f, 0.6f, 1000, 1.5f, 3, false);

        var results = processor.Run(pred, pred.Size());

        Assert.Empty(results);
    }

    private static Mat CreateProbabilityMap(double probability)
    {
        var pred = new Mat(100, 100, MatType.CV_32FC1, Scalar.Black);
        using var textRegion = pred[new Rect(10, 20, 50, 20)];
        textRegion.SetTo(new Scalar(probability));
        return pred;
    }
}
