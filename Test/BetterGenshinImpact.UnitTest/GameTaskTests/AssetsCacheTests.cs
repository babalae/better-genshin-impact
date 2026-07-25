using BetterGenshinImpact.GameTask.Common.Element.Assets;
using System.Collections.Concurrent;

namespace BetterGenshinImpact.UnitTest.GameTaskTests;

public class AssetsCacheTests
{
    [Fact]
    public void Get_WithSameCaptureSize_ShouldReturnSameInstance()
    {
        var first = MapAssets.Get(1920, 1080);
        var second = MapAssets.Get(1920, 1080);

        Assert.Same(first, second);
    }

    [Fact]
    public void Get_WithDifferentCaptureSize_ShouldReturnDifferentInstanceAndScaledRect()
    {
        var fullHd = MapAssets.Get(1920, 1080);
        var hd = MapAssets.Get(1280, 720);

        Assert.NotSame(fullHd, hd);
        Assert.Equal(212, fullHd.MimiMapRect.Width);
        Assert.Equal(141, hd.MimiMapRect.Width);
    }

    [Fact]
    public void Get_ConcurrentlyWithSameCaptureSize_ShouldReturnSameInstance()
    {
        var results = new ConcurrentBag<MapAssets>();

        Parallel.For(0, 32, _ => results.Add(MapAssets.Get(1600, 900)));

        var first = results.First();
        Assert.All(results, result => Assert.Same(first, result));
    }

    [Theory]
    [InlineData(0, 1080)]
    [InlineData(1920, 0)]
    [InlineData(-1, 1080)]
    public void Get_WithInvalidCaptureSize_ShouldThrow(int captureWidth, int captureHeight)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MapAssets.Get(captureWidth, captureHeight));
    }
}
