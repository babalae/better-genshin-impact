using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.Area.Converter;
using BetterGenshinImpact.UnitTest.GameTaskTests.AutoFishingTests;
using OpenCvSharp;
using CvSize = OpenCvSharp.Size;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests;

public class ImageRegionReferenceSearchTests
{
    private static Mat CreateTemplate()
    {
        var template = new Mat(32, 32, MatType.CV_8UC3, Scalar.Black);
        Cv2.Rectangle(template, new Rect(4, 4, 8, 8), Scalar.White, -1);
        Cv2.Circle(template, new Point(22, 10), 5, new Scalar(160, 160, 160), -1);
        Cv2.Line(template, new Point(3, 28), new Point(28, 21), new Scalar(220, 220, 220), 2);
        return template;
    }

    private static RecognitionObject CreateRecognitionObject(Mat template)
    {
        return new RecognitionObject
        {
            Name = "ReferenceSearchTest",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = template,
            Threshold = 0.8,
            ReferenceImageSize = new CvSize(1920, 1080),
            ReferenceBoundingBox = new Rect(200, 150, 32, 32),
            SearchOptions = new SearchOptions
            {
                AnchorMode = SearchAnchorMode.TopLeft
            }
        }.InitTemplate();
    }

    private static void PutTemplate(Mat screen, Mat template, Rect targetRect)
    {
        using var resized = new Mat();
        Cv2.Resize(template, resized, new CvSize(targetRect.Width, targetRect.Height));
        using var target = new Mat(screen, targetRect);
        resized.CopyTo(target);
    }

    [Fact]
    public void Find_WithReferenceSearchOnGameCaptureRegion_UsesScaledRoiAndTemplate()
    {
        using var template = CreateTemplate();
        using var screen = new Mat(1600, 2560, MatType.CV_8UC3, Scalar.Black);
        PutTemplate(screen, template, new Rect(267, 200, 43, 43));
        var ro = CreateRecognitionObject(template);

        using var region = new GameCaptureRegion(screen.Clone(), 0, 0, drawContent: new FakeDrawContent());

        using var result = region.Find(ro);

        Assert.True(result.IsExist());
        Assert.Equal(new Rect(267, 200, 43, 43), result.ToRect());
    }

    [Fact]
    public void Find_WithReferenceSearchOnDirectScaleDerivedImageRegion_IsAllowed()
    {
        using var template = CreateTemplate();
        using var screen = new Mat(1200, 1920, MatType.CV_8UC3, Scalar.Black);
        PutTemplate(screen, template, new Rect(200, 150, 32, 32));
        var ro = CreateRecognitionObject(template);
        var drawContent = new FakeDrawContent();
        using var parent = new GameCaptureRegion(new Mat(1600, 2560, MatType.CV_8UC3, Scalar.Black), 0, 0, drawContent: drawContent);
        using var region = new ImageRegion(screen.Clone(), 0, 0, parent, new ScaleConverter(2560 / 1920d), drawContent);

        using var result = region.Find(ro);

        Assert.True(result.IsExist());
        Assert.Equal(new Rect(200, 150, 32, 32), result.ToRect());
    }

    [Fact]
    public void Find_WithReferenceSearchOnCroppedImageRegion_IsRejected()
    {
        using var template = CreateTemplate();
        using var screen = new Mat(1600, 2560, MatType.CV_8UC3, Scalar.Black);
        PutTemplate(screen, template, new Rect(267, 200, 43, 43));
        var ro = CreateRecognitionObject(template);

        using var region = new GameCaptureRegion(screen.Clone(), 0, 0, drawContent: new FakeDrawContent());
        using var cropped = region.DeriveCrop(0, 0, 400, 400);

        using var result = cropped.Find(ro);

        Assert.True(result.IsEmpty());
    }

    [Fact]
    public void Find_WithReferenceOcrOnCroppedImageRegion_IsRejectedBeforeOcr()
    {
        using var screen = new Mat(1600, 2560, MatType.CV_8UC3, Scalar.Black);
        var ro = new RecognitionObject
        {
            Name = "ReferenceOcrTest",
            RecognitionType = RecognitionTypes.Ocr,
            ReferenceImageSize = new CvSize(1920, 1080),
            ReferenceBoundingBox = new Rect(200, 150, 32, 32)
        };

        using var region = new GameCaptureRegion(screen.Clone(), 0, 0, drawContent: new FakeDrawContent());
        using var cropped = region.DeriveCrop(0, 0, 400, 400);

        using var result = cropped.Find(ro);

        Assert.True(result.IsEmpty());
    }
}
