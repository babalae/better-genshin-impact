using BetterGenshinImpact.GameTask.AutoPick;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoPickTests;

public class AutoPickAssetsTests
{
    [Fact]
    public void LoadControllerIconBlacklistTemplatesReturnsEmptyWhenDirectoryIsMissing()
    {
        var missingDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var templates = AutoPickAssets.LoadControllerIconBlacklistTemplates(missingDirectory);

        Assert.Empty(templates);
    }

    [Fact]
    public void LoadControllerIconBlacklistTemplatesLoadsPngTemplates()
    {
        var directory = Directory.CreateTempSubdirectory("bgi-controller-icon-blacklist-");
        try
        {
            var templatePath = Path.Combine(directory.FullName, "forbidden.png");
            using (var mat = new Mat(12, 12, MatType.CV_8UC3, Scalar.Black))
            {
                Cv2.Rectangle(mat, new Rect(3, 3, 6, 6), Scalar.White, -1);
                Cv2.ImWrite(templatePath, mat);
            }

            var templates = AutoPickAssets.LoadControllerIconBlacklistTemplates(directory.FullName);

            var template = Assert.Single(templates);
            Assert.Equal("ControllerIconBlacklist:forbidden", template.Name);
            Assert.False(template.TemplateImageMat?.Empty() ?? true);
            Assert.False(template.TemplateImageGreyMat?.Empty() ?? true);
            Assert.True(template.Threshold >= 0.9);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void LoadControllerIconBlacklistTemplatesReturnsEmptyWhenDirectoryIsEmpty()
    {
        var directory = Directory.CreateTempSubdirectory("bgi-controller-icon-blacklist-");
        try
        {
            var templates = AutoPickAssets.LoadControllerIconBlacklistTemplates(directory.FullName);

            Assert.Empty(templates);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void LoadControllerIconBlacklistTemplatesSkipsInvalidImages()
    {
        var directory = Directory.CreateTempSubdirectory("bgi-controller-icon-blacklist-");
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "bad.png"), "not an image");

            var templates = AutoPickAssets.LoadControllerIconBlacklistTemplates(directory.FullName);

            Assert.Empty(templates);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void HasControllerIconBlacklistTemplateSkipsTemplatesLargerThanSearchRegion()
    {
        using var capture = new Mat(24, 24, MatType.CV_8UC3, Scalar.Black);
        using var templateMat = new Mat(16, 16, MatType.CV_8UC3, Scalar.White);
        var region = new ImageRegion(capture, 0, 0);
        var template = AutoPickAssets.CreateControllerIconBlacklistTemplate("large", templateMat.Clone());

        var matched = AutoPickTrigger.HasControllerIconBlacklistTemplate(
            region,
            new Rect(0, 0, 8, 8),
            [template]);

        Assert.False(matched);
    }

    [Fact]
    public void HasControllerIconBlacklistTemplateReturnsTrueWhenTemplateMatchesSearchRegion()
    {
        using var capture = new Mat(32, 32, MatType.CV_8UC3, Scalar.Black);
        Cv2.Rectangle(capture, new Rect(10, 10, 8, 8), new Scalar(32, 32, 32), -1);
        Cv2.Line(capture, new Point(10, 10), new Point(17, 17), Scalar.White, 1);
        Cv2.Circle(capture, new Point(15, 12), 2, new Scalar(180, 180, 180), -1);
        using var templateMat = new Mat(capture, new Rect(10, 10, 8, 8));
        var region = new ImageRegion(capture, 0, 0);
        var template = AutoPickAssets.CreateControllerIconBlacklistTemplate("pattern", templateMat.Clone());

        var matched = AutoPickTrigger.HasControllerIconBlacklistTemplate(
            region,
            new Rect(8, 8, 14, 14),
            [template]);

        Assert.True(matched);
    }

    [Fact]
    public void HasControllerIconBlacklistTemplateRestoresTemplateSearchRegionAfterMatch()
    {
        using var capture = new Mat(32, 32, MatType.CV_8UC3, Scalar.Black);
        Cv2.Rectangle(capture, new Rect(10, 10, 8, 8), new Scalar(32, 32, 32), -1);
        Cv2.Line(capture, new Point(10, 10), new Point(17, 17), Scalar.White, 1);
        Cv2.Circle(capture, new Point(15, 12), 2, new Scalar(180, 180, 180), -1);
        using var templateMat = new Mat(capture, new Rect(10, 10, 8, 8));
        var region = new ImageRegion(capture, 0, 0);
        var template = AutoPickAssets.CreateControllerIconBlacklistTemplate("pattern", templateMat.Clone());
        var originalRegionOfInterest = new Rect(1, 2, 3, 4);
        template.RegionOfInterest = originalRegionOfInterest;

        _ = AutoPickTrigger.HasControllerIconBlacklistTemplate(
            region,
            new Rect(8, 8, 14, 14),
            [template]);

        Assert.Equal(originalRegionOfInterest, template.RegionOfInterest);
    }
}
