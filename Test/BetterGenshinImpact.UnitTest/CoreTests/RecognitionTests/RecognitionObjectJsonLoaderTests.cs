using BetterGenshinImpact.Core.Recognition;
using OpenCvSharp;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests;

public class RecognitionObjectJsonLoaderTests
{
    public static TheoryData<double[], SearchExpandRatio> ExpandPercentCases => new()
    {
        { [0], new SearchExpandRatio(0, 0, 0, 0) },
        { [0.05], new SearchExpandRatio(0.05, 0.05, 0.05, 0.05) },
        { [0.1, 0.2], new SearchExpandRatio(0.1, 0.2, 0.1, 0.2) },
        { [0.1, 0.2, 0.3, 0.4], new SearchExpandRatio(0.1, 0.2, 0.3, 0.4) },
    };

    [Theory]
    [MemberData(nameof(ExpandPercentCases))]
    public void Load_SearchExpandPercent_UsesXamlThicknessOrder(
        double[] values,
        SearchExpandRatio expected)
    {
        var config = CreateConfig(new RecognitionSearchJsonConfig
        {
            Anchor = nameof(SearchAnchorMode.TopRight),
            Box = "rect(100, 80, 300, 200)",
            Expand = [99, 88],
            ExpandPercent = [.. values],
        });

        var recognitionObject = RecognitionObjectJsonLoader.Load(config, "Target", CreateContext());

        Assert.NotNull(recognitionObject.SearchOptions);
        Assert.Equal(SearchAnchorMode.TopRight, recognitionObject.SearchOptions.AnchorMode);
        Assert.Equal(new Rect(100, 80, 300, 200), recognitionObject.SearchOptions.ReferenceSearchBox);
        Assert.Equal(new Size(99, 88), recognitionObject.SearchOptions.ExpandSize);
        Assert.Equal(expected, recognitionObject.SearchOptions.ExpandPercent);
    }

    public static TheoryData<double[]> InvalidExpandPercentCases => new()
    {
        { [] },
        { [0.1, 0.2, 0.3] },
        { [0.1, 0.2, 0.3, 0.4, 0.5] },
        { [-0.1] },
        { [double.NaN] },
        { [double.PositiveInfinity] },
    };

    [Theory]
    [MemberData(nameof(InvalidExpandPercentCases))]
    public void Load_InvalidSearchExpandPercent_Throws(double[] values)
    {
        var config = CreateConfig(new RecognitionSearchJsonConfig
        {
            ExpandPercent = [.. values],
        });

        Assert.Throws<InvalidOperationException>(() =>
            RecognitionObjectJsonLoader.Load(config, "Target", CreateContext()));
    }

    [Fact]
    public void Clone_CopiesReferenceSearchOptions()
    {
        var recognitionObject = new RecognitionObject
        {
            RecognitionType = RecognitionTypes.Ocr,
            SearchOptions = new SearchOptions
            {
                AnchorMode = SearchAnchorMode.Center,
                ReferenceSearchBox = new Rect(10, 20, 30, 40),
                ExpandSize = new Size(5, 6),
                ExpandPercent = new SearchExpandRatio(0.1, 0.2, 0.3, 0.4),
            },
        };

        var cloned = recognitionObject.Clone();

        Assert.NotSame(recognitionObject.SearchOptions, cloned.SearchOptions);
        Assert.Equal(recognitionObject.SearchOptions.ReferenceSearchBox, cloned.SearchOptions!.ReferenceSearchBox);
        Assert.Equal(recognitionObject.SearchOptions.ExpandSize, cloned.SearchOptions.ExpandSize);
        Assert.Equal(recognitionObject.SearchOptions.ExpandPercent, cloned.SearchOptions.ExpandPercent);
    }

    private static RecognitionObjectJsonFile CreateConfig(RecognitionSearchJsonConfig search)
    {
        return new RecognitionObjectJsonFile
        {
            Objects = new Dictionary<string, RecognitionObjectJsonConfig>
            {
                ["Target"] = new RecognitionObjectJsonConfig
                {
                    Type = nameof(RecognitionTypes.Ocr),
                    Reference = new RecognitionReferenceJsonConfig
                    {
                        Size = [1920, 1080],
                        Bbox = "rect(200, 150, 32, 32)",
                    },
                    Search = search,
                },
            },
        };
    }

    private static RecognitionObjectJsonLoadContext CreateContext()
    {
        return new RecognitionObjectJsonLoadContext
        {
            CaptureWidth = 1920,
            CaptureHeight = 1080,
            TemplateLoader = (_, _) => throw new InvalidOperationException("OCR 配置不应加载模板图片"),
        };
    }
}
