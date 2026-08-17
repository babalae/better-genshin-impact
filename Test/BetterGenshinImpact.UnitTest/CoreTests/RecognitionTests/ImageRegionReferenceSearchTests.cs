using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.Area.Converter;
using BetterGenshinImpact.UnitTest.GameTaskTests.AutoFishingTests;
using OpenCvSharp;
using CvSize = OpenCvSharp.Size;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests;

public class ImageRegionReferenceSearchTests
{
    public static TheoryData<SearchAnchorMode, int, int, Rect> ExplicitAnchorCases => new()
    {
        { SearchAnchorMode.TopLeft, 300, 200, new Rect(20, 40, 40, 40) },
        { SearchAnchorMode.TopRight, 300, 200, new Rect(120, 40, 40, 40) },
        { SearchAnchorMode.BottomLeft, 200, 300, new Rect(20, 140, 40, 40) },
        { SearchAnchorMode.BottomRight, 200, 300, new Rect(20, 140, 40, 40) },
        { SearchAnchorMode.BottomRight, 300, 200, new Rect(120, 40, 40, 40) },
        { SearchAnchorMode.Center, 300, 200, new Rect(70, 40, 40, 40) },
        { SearchAnchorMode.Center, 200, 300, new Rect(20, 90, 40, 40) },
    };

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

    [Theory]
    [MemberData(nameof(ExplicitAnchorCases))]
    public void TryGetReferenceSearchRegion_ExplicitAnchor_TransformsSearchBox(
        SearchAnchorMode anchorMode,
        int imageWidth,
        int imageHeight,
        Rect expected)
    {
        using var screen = new Mat(imageHeight, imageWidth, MatType.CV_8UC3, Scalar.Black);
        using var region = new GameCaptureRegion(screen.Clone(), 0, 0, drawContent: new FakeDrawContent());
        var ro = new RecognitionObject
        {
            RecognitionType = RecognitionTypes.Ocr,
            ReferenceImageSize = new CvSize(100, 100),
            ReferenceBoundingBox = new Rect(10, 20, 10, 10),
            SearchOptions = new SearchOptions
            {
                AnchorMode = anchorMode,
                ReferenceSearchBox = new Rect(10, 20, 20, 20),
                ExpandPercent = new SearchExpandRatio(0, 0, 0, 0),
            },
        };

        var success = ImageRegionReferenceSearchHelper.TryGetReferenceSearchRegion(
            region,
            ro,
            out var actual,
            out var templateSize);

        Assert.True(success);
        Assert.Equal(expected, actual);
        Assert.Equal(new CvSize(20, 20), templateSize);
    }

    public static TheoryData<Rect, int, int, Rect> AutoResponsiveAnchorCases => new()
    {
        { new Rect(100, 100, 20, 20), 1400, 1000, new Rect(100, 100, 20, 20) },
        { new Rect(490, 100, 20, 20), 1400, 1000, new Rect(690, 100, 20, 20) },
        { new Rect(880, 100, 20, 20), 1400, 1000, new Rect(1280, 100, 20, 20) },
        { new Rect(100, 490, 20, 20), 1000, 1400, new Rect(100, 690, 20, 20) },
        { new Rect(100, 880, 20, 20), 1000, 1400, new Rect(100, 1280, 20, 20) },
    };

    [Theory]
    [MemberData(nameof(AutoResponsiveAnchorCases))]
    public void TryGetReferenceSearchRegion_Auto_PreservesResponsiveLayout(
        Rect referenceBoundingBox,
        int imageWidth,
        int imageHeight,
        Rect expected)
    {
        using var screen = new Mat(imageHeight, imageWidth, MatType.CV_8UC3, Scalar.Black);
        using var region = new GameCaptureRegion(screen.Clone(), 0, 0, drawContent: new FakeDrawContent());
        var ro = new RecognitionObject
        {
            RecognitionType = RecognitionTypes.Ocr,
            ReferenceImageSize = new CvSize(1000, 1000),
            ReferenceBoundingBox = referenceBoundingBox,
            SearchOptions = new SearchOptions
            {
                AnchorMode = SearchAnchorMode.Auto,
                ExpandPercent = new SearchExpandRatio(0, 0, 0, 0),
            },
        };

        var success = ImageRegionReferenceSearchHelper.TryGetReferenceSearchRegion(
            region,
            ro,
            out var actual,
            out _);

        Assert.True(success);
        Assert.Equal(expected, actual);
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
    public void Find_WithReferenceSearchBox_SearchesIndependentAnchoredRegion()
    {
        using var template = CreateTemplate();
        using var screen = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.Black);
        PutTemplate(screen, template, new Rect(650, 300, 32, 32));
        var ro = CreateRecognitionObject(template);
        ro.SearchOptions!.ReferenceSearchBox = new Rect(600, 250, 160, 120);
        ro.SearchOptions.ExpandPercent = new SearchExpandRatio(0, 0, 0, 0);

        using var region = new GameCaptureRegion(screen.Clone(), 0, 0, drawContent: new FakeDrawContent());
        using var result = region.Find(ro);

        Assert.True(result.IsExist());
        Assert.Equal(new Rect(650, 300, 32, 32), result.ToRect());
    }

    [Fact]
    public void Find_WithPercentExpand_UsesCurrentScreenshotWidthAndHeight()
    {
        using var template = CreateTemplate();
        using var screen = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.Black);
        PutTemplate(screen, template, new Rect(270, 190, 32, 32));
        var ro = CreateRecognitionObject(template);
        ro.SearchOptions!.ExpandSize = new CvSize(0, 0);
        ro.SearchOptions.ExpandPercent = new SearchExpandRatio(0, 0, 0.05, 0.05);

        using var region = new GameCaptureRegion(screen.Clone(), 0, 0, drawContent: new FakeDrawContent());
        using var result = region.Find(ro);

        Assert.True(result.IsExist());
        Assert.Equal(new Rect(270, 190, 32, 32), result.ToRect());
    }

    [Fact]
    public void TryGetReferenceSearchRegion_PercentExpand_UsesFourScreenshotEdges()
    {
        using var screen = new Mat(200, 300, MatType.CV_8UC3, Scalar.Black);
        using var region = new GameCaptureRegion(screen.Clone(), 0, 0, drawContent: new FakeDrawContent());
        var ro = new RecognitionObject
        {
            RecognitionType = RecognitionTypes.Ocr,
            ReferenceImageSize = new CvSize(100, 100),
            ReferenceBoundingBox = new Rect(20, 20, 10, 10),
            SearchOptions = new SearchOptions
            {
                AnchorMode = SearchAnchorMode.TopLeft,
                ReferenceSearchBox = new Rect(20, 20, 20, 20),
                ExpandPercent = new SearchExpandRatio(0.1, 0.1, 0.2, 0.05),
            },
        };

        var success = ImageRegionReferenceSearchHelper.TryGetReferenceSearchRegion(
            region,
            ro,
            out var actual,
            out _);

        Assert.True(success);
        // 左右分别按 300px 宽计算 30/60px，上下分别按 200px 高计算 20/10px。
        Assert.Equal(new Rect(10, 20, 130, 70), actual);
    }

    [Fact]
    public void TryGetReferenceSearchRegion_OutOfBoundsSearchBox_IsClamped()
    {
        using var screen = new Mat(100, 200, MatType.CV_8UC3, Scalar.Black);
        using var region = new GameCaptureRegion(screen.Clone(), 0, 0, drawContent: new FakeDrawContent());
        var ro = new RecognitionObject
        {
            RecognitionType = RecognitionTypes.Ocr,
            ReferenceImageSize = new CvSize(200, 100),
            ReferenceBoundingBox = new Rect(10, 10, 10, 10),
            SearchOptions = new SearchOptions
            {
                AnchorMode = SearchAnchorMode.TopLeft,
                ReferenceSearchBox = new Rect(-50, -20, 80, 50),
                ExpandPercent = new SearchExpandRatio(0, 0, 0, 0),
            },
        };

        var success = ImageRegionReferenceSearchHelper.TryGetReferenceSearchRegion(
            region,
            ro,
            out var actual,
            out _);

        Assert.True(success);
        Assert.Equal(new Rect(0, 0, 30, 30), actual);
    }

    [Fact]
    public void TryGetReferenceSearchRegion_ScaledSearchBoxTooSmallForTemplate_ReturnsFalse()
    {
        using var screen = new Mat(200, 200, MatType.CV_8UC3, Scalar.Black);
        using var region = new GameCaptureRegion(screen.Clone(), 0, 0, drawContent: new FakeDrawContent());
        var ro = new RecognitionObject
        {
            RecognitionType = RecognitionTypes.TemplateMatch,
            ReferenceImageSize = new CvSize(100, 100),
            ReferenceBoundingBox = new Rect(20, 20, 30, 30),
            SearchOptions = new SearchOptions
            {
                AnchorMode = SearchAnchorMode.TopLeft,
                ReferenceSearchBox = new Rect(20, 20, 10, 10),
                ExpandPercent = new SearchExpandRatio(0, 0, 0, 0),
            },
        };

        var success = ImageRegionReferenceSearchHelper.TryGetReferenceSearchRegion(
            region,
            ro,
            out _,
            out var templateSize);

        Assert.False(success);
        Assert.Equal(new CvSize(60, 60), templateSize);
    }

    [Fact]
    public void Find_WithZeroPercentExpand_IgnoresPixelExpand()
    {
        using var template = CreateTemplate();
        using var screen = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.Black);
        PutTemplate(screen, template, new Rect(270, 150, 32, 32));
        var ro = CreateRecognitionObject(template);
        ro.SearchOptions!.ExpandSize = new CvSize(100, 100);
        ro.SearchOptions.ExpandPercent = new SearchExpandRatio(0, 0, 0, 0);

        using var region = new GameCaptureRegion(screen.Clone(), 0, 0, drawContent: new FakeDrawContent());
        using var result = region.Find(ro);

        Assert.True(result.IsEmpty());
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
