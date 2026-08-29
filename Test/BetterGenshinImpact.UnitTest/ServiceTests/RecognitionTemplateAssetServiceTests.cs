using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Service;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace BetterGenshinImpact.UnitTest.ServiceTests;

public class RecognitionTemplateAssetServiceTests
{
    public static TheoryData<SearchExpandRatio, double[]> CompactExpandPercentCases => new()
    {
        { new SearchExpandRatio(0.05, 0.05, 0.05, 0.05), [0.05] },
        { new SearchExpandRatio(0.1, 0.2, 0.1, 0.2), [0.1, 0.2] },
        { new SearchExpandRatio(0.1, 0.2, 0.3, 0.4), [0.1, 0.2, 0.3, 0.4] },
    };

    [Theory]
    [MemberData(nameof(CompactExpandPercentCases))]
    public void Prepare_SearchExpandPercent_WritesShortestXamlThicknessForm(
        SearchExpandRatio ratio,
        double[] expected)
    {
        var service = new RecognitionTemplateAssetService();
        var draft = CreateDraft() with
        {
            ReferenceSearchBox = new Rect(80, 60, 300, 180),
            SearchExpandWidth = 123,
            SearchExpandHeight = 456,
            SearchExpandPercent = ratio,
        };

        var plan = service.Prepare(draft);
        var root = JObject.Parse(plan.JsonContent);
        var search = (JObject)root["objects"]!["Target"]!["search"]!;

        Assert.Equal("rect(80, 60, 300, 180)", search["box"]!.Value<string>());
        Assert.Null(search["expand"]);
        Assert.Equal(expected, search["expandPercent"]!.Values<double>());
    }

    [Fact]
    public void Prepare_ZeroExpandPercent_SuppressesDefaultPixelExpand()
    {
        var service = new RecognitionTemplateAssetService();
        var plan = service.Prepare(CreateDraft() with
        {
            SearchExpandPercent = new SearchExpandRatio(0, 0, 0, 0),
        });
        var root = JObject.Parse(plan.JsonContent);
        var search = (JObject)root["objects"]!["Target"]!["search"]!;

        Assert.Equal([0d], search["expandPercent"]!.Values<double>());
        Assert.Null(search["expand"]);
    }

    [Fact]
    public void Prepare_PixelExpandMode_WritesOnlyPixelExpand()
    {
        var service = new RecognitionTemplateAssetService();
        var plan = service.Prepare(CreateDraft() with
        {
            SearchExpandWidth = 12,
            SearchExpandHeight = 18,
            SearchExpandPercent = null,
        });
        var root = JObject.Parse(plan.JsonContent);
        var search = (JObject)root["objects"]!["Target"]!["search"]!;

        Assert.Equal([12, 18], search["expand"]!.Values<int>());
        Assert.Null(search["expandPercent"]);
    }

    private static RecognitionTemplateDraft CreateDraft()
    {
        var taskDirectory = Path.Combine(Path.GetTempPath(), "bgi-recognition-template-tests", Guid.NewGuid().ToString("N"));
        return new RecognitionTemplateDraft
        {
            JsonPath = Path.Combine(taskDirectory, "Recognition.json"),
            AssetsRootPath = taskDirectory,
            ObjectName = "Target",
            TemplateFileName = "target.png",
            Selection = new Rect(100, 80, 40, 30),
            ReferenceWidth = 1920,
            ReferenceHeight = 1080,
        };
    }
}
