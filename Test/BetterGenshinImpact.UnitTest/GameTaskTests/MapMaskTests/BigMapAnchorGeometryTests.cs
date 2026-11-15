using BetterGenshinImpact.GameTask.MapMask;
using BetterGenshinImpact.Infrastructure.Temp;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using OpenCvSharp;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.MapMaskTests;

/// <summary>L3 的独立几何验收：真实变换、L1 比例约束、重复地标与错误假设。</summary>
public class BigMapAnchorGeometryTests
{
    /// <summary>不规则且分散的独立锚点，避免对称排列本身造成歧义。</summary>
    private static Point2f[] Points() => [new(100, 100), new(400, 120), new(130, 350), new(600, 400), new(750, 180)];

    /// <summary>损坏缓存中的 null 帧不阻塞启动，也不丢弃同一文件内仍有效的几何参考。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Atlas_NullCachedFrames_PreservesValidReference(bool includeValidFrame)
    {
        using var temp = new TempFileService($"map-atlas-test-{Guid.NewGuid():N}", NullLogger<TempFileService>.Instance);
        using var scope = temp.CreateScope(new TempCategory("map-test", "地图锚点回归"));
        var path = scope.GetPath("anchors.json");
        var source = Points();
        var world = source.Select(p => new[] { p.X * 4 + 2000, p.Y * 4 + 5000 }).ToArray();
        File.WriteAllText(path, includeValidFrame ? "[null," + JsonConvert.SerializeObject(world) + ",null]" : "[null]");
        var atlas = new BigMapAnchorAtlas(new Size(50000, 50000), path);
        var result = atlas.Match(source, new Size(960, 540), null);
        if (includeValidFrame)
        {
            Assert.NotNull(result);
            Assert.Equal(2000, result.Viewport.X, 3);
            Assert.Equal(5000, result.Viewport.Y, 3);
        }
        else
        {
            Assert.Null(result);
        }
    }

    /// <summary>缓存满载后连续重载和采集新参考，始终保留最新组并淘汰最旧组。</summary>
    [Fact]
    public void Atlas_ReloadFullCache_PreservesRecencyAndEvictsOldestFrame()
    {
        using var temp = new TempFileService($"map-atlas-test-{Guid.NewGuid():N}", NullLogger<TempFileService>.Instance);
        using var scope = temp.CreateScope(new TempCategory("map-test", "地图锚点回归"));
        var path = scope.GetPath("anchors.json");
        var frames = Enumerable.Range(0, 16).Select(i => Points()
            .Select(p => new[] { p.X + 1000 + i * 2000, p.Y + 1000 }).ToArray()).ToArray();
        File.WriteAllText(path, JsonConvert.SerializeObject(frames));

        for (var reload = 1; reload <= 2; reload++)
        {
            var atlas = new BigMapAnchorAtlas(new Size(100000, 100000), path);
            atlas.Observe(Points().Select(p => new Point2f(p.X + 50000 + reload * 2000, p.Y + 1000)));
            var saved = JsonConvert.DeserializeObject<float[][][]>(File.ReadAllText(path))!;
            Assert.Equal(16, saved.Length);
            var newest = Enumerable.Range(1, reload).Reverse().Select(i => 50100f + i * 2000);
            var retained = frames.Take(16 - reload).Select(frame => frame[0][0]);
            Assert.Equal(newest.Concat(retained), saved.Select(frame => frame[0][0]));
        }
    }

    /// <summary>锚点坐标加上无关检测和无关参考点后，仍必须独立求出正确的整个视口。</summary>
    [Fact]
    public void Match_IndependentAnchorsWithOutliers_ReturnsViewport()
    {
        var source = Points();
        var world = source.Select(p => new Point2f(p.X * 4 + 2000, p.Y * 4 + 5000))
            .Concat([new Point2f(17000, 1000), new Point2f(9000, 13000)]);
        var matcher = new BigMapAnchorGeometry(world);
        var result = matcher.Match(source.Concat([new Point2f(850, 490), new Point2f(280, 490)]), new Size(960, 540));
        Assert.NotNull(result);
        Assert.Equal(2000, result.Viewport.X, 3);
        Assert.Equal(5000, result.Viewport.Y, 3);
        Assert.Equal(3840, result.Viewport.Width, 3);
        Assert.InRange(result.Confidence, 0.7, 0.72);
    }

    /// <summary>L1 实测比例参与约束，比例明显冲突时不能继续接受锚点假设。</summary>
    [Fact]
    public void Match_ZoomPrior_ConstrainsScale()
    {
        var source = Points();
        var matcher = new BigMapAnchorGeometry(source.Select(p => new Point2f(p.X * 4 + 2000, p.Y * 4 + 5000)));
        Assert.NotNull(matcher.Match(source, new Size(960, 540), 4));
        Assert.Null(matcher.Match(source, new Size(960, 540), 8));
    }

    /// <summary>重复整组地标必须报告歧义，不能根据任意一对点选中其中一个地区。</summary>
    [Fact]
    public void Match_RepeatedConstellation_ReturnsNull()
    {
        var source = Points();
        var first = source.Select(p => new Point2f(p.X * 4 + 2000, p.Y * 4 + 5000)).ToArray();
        var matcher = new BigMapAnchorGeometry(first.Concat(first.Select(p => new Point2f(p.X + 15000, p.Y + 10000))));
        Assert.Null(matcher.Match(source, new Size(960, 540)));
    }

    /// <summary>少于四点、共线、镜像、旋转或重复检测不构成可信的二维定位。</summary>
    [Theory]
    [InlineData("two")]
    [InlineData("three")]
    [InlineData("line")]
    [InlineData("mirror")]
    [InlineData("rotate")]
    [InlineData("duplicates")]
    public void Match_InvalidSupport_ReturnsNull(string kind)
    {
        var source = kind == "line"
            ? Enumerable.Range(0, 5).Select(i => new Point2f(100 + i * 120, 50 + i * 80)).ToArray() : Points();
        var world = source.Select(p => kind switch
        {
            "mirror" => new Point2f(5000 - p.X * 4, 5000 - p.Y * 4),
            "rotate" => new Point2f(p.X * 3 - p.Y * 2 + 2000, p.X * 2 + p.Y * 3 + 5000),
            _ => new Point2f(p.X * 4 + 2000, p.Y * 4 + 5000)
        });
        var query = kind switch
        {
            "two" => source.Take(2),
            "three" => source.Take(3),
            "duplicates" => Enumerable.Repeat(source[0], 8),
            _ => source
        };
        Assert.Null(new BigMapAnchorGeometry(world).Match(query, new Size(960, 540)));
    }
}
