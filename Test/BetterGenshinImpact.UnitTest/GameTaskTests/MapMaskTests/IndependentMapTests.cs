using System.Diagnostics;
using System.Text.Json;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.GameTask.MapMask;
using BetterGenshinImpact.Core.Recognition.OpenCv.FeatureMatch;
using BetterGenshinImpact.Infrastructure.Temp;
using BetterGenshinImpact.Model.MaskMap;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.Tavern.Model;
using BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests.OCRTests;
using OpenCvSharp;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.MapMaskTests;

/// <summary>独立地图真实截图：标题识别、原生资源定位及网页点位投影必须共同正确。</summary>
public class IndependentMapTests(PaddleFixture paddle, ITestOutputHelper output) : IClassFixture<PaddleFixture>
{
    /// <summary>实际 CPU OCR，与生产共用标题裁剪和解析。</summary>
    private readonly PaddleFixture _paddle = paddle;

    /// <summary>记录各分支诊断及实际耗时。</summary>
    private readonly ITestOutputHelper _output = output;

    /// <summary>同名底图更新后必须重新生成特征，不能继续返回旧底图中的位置。</summary>
    [Fact]
    public void FeatureCache_UpdatedMapImage_RebuildsExistingFeatures()
    {
        using var temp = new TempFileService($"map-test-{Environment.ProcessId}-{Guid.NewGuid():N}",
            NullLogger<TempFileService>.Instance);
        using var scope = temp.CreateScope(new TempCategory("map-test", "地图特征回归"));
        var path = scope.GetPath("map.png");
        var keyPointsPath = scope.GetPath("map_SIFT.kp.bin");
        var descriptorsPath = scope.GetPath("map_SIFT.mat.png");
        var map = new FeatureCacheMap();
        using var image = new Mat(180, 512, MatType.CV_8UC1, Scalar.Black);

        // 先缓存左侧地形，再把同一组地形移到右侧，模拟资源包替换同名底图。
        foreach (var x in new[] { 32, 64, 96 })
        foreach (var y in new[] { 32, 64, 96 })
            Cv2.Rectangle(image, new Rect(x, y, 12, 12), Scalar.White, -1);
        image.SaveImage(path);
        map.Extract(path);
        var original = FeatureStorageHelper.LoadKeyPointArray(keyPointsPath)!;
        Assert.NotEmpty(original);
        Assert.True(original.Average(p => p.Pt.X) < 150);

        using var shifted = new Mat(image.Size(), image.Type(), Scalar.Black);
        using (var source = new Mat(image, new Rect(0, 0, 200, image.Height)))
        using (var target = new Mat(shifted, new Rect(300, 0, 200, image.Height)))
            source.CopyTo(target);
        shifted.SaveImage(path);
        var updatedAt = DateTime.UtcNow.AddMinutes(-1);
        File.SetLastWriteTimeUtc(path, updatedAt);
        File.SetLastWriteTimeUtc(keyPointsPath, updatedAt.AddMinutes(-1));
        File.SetLastWriteTimeUtc(descriptorsPath, updatedAt.AddMinutes(-1));
        map.Extract(path);
        var refreshed = FeatureStorageHelper.LoadKeyPointArray(keyPointsPath)!;
        Assert.NotEmpty(refreshed);
        Assert.True(refreshed.Average(p => p.Pt.X) > 300);
        using var descriptors = FeatureStorageHelper.LoadDescriptorMat(descriptorsPath);
        Assert.NotNull(descriptors);
        Assert.Equal(refreshed.Length, descriptors.Rows);
    }

    /// <summary>只暴露真实地图特征生成入口，不加载共享地图目录或修改运行时素材。</summary>
    private sealed class FeatureCacheMap() : SceneBaseMap(MapTypes.Enkanomiya,
        new Size(512, 180), default, 1024, 0, 0)
    {
        /// <summary>通过生产入口生成或更新临时底图的特征文件。</summary>
        internal void Extract(string path) => ExtractAndSaveFeature(path);
    }

    /// <summary>冷启动不预热提瓦特，也不能用已知场景替代真实标题识别。</summary>
    [Theory]
    [InlineData("enkanomiya-west", MapTypes.Enkanomiya, 539, 444, 1484)]
    [InlineData("enkanomiya-east", MapTypes.Enkanomiya, -287, 384, 1440)]
    [InlineData("enkanomiya-close", MapTypes.Enkanomiya, 415, 400, 821)]
    [InlineData("enkanomiya-yachimatahime", MapTypes.Enkanomiya, -707, 1200, 1201)]
    [InlineData("enkanomiya-yachimatahime-4k", MapTypes.Enkanomiya, -707, 1200, 1201)]
    [InlineData("moon-highland", MapTypes.MoonCanon, -1688, -641, 1663)]
    [InlineData("moon-highland-4k", MapTypes.MoonCanon, -1688, -641, 1663)]
    [InlineData("moon-highland-close", MapTypes.MoonCanon, -1606, -582, 1233)]
    [InlineData("moon-eye", MapTypes.MoonCanon, -2441, -1501, 1442)]
    [InlineData("moon-sea", MapTypes.MoonCanon, 318, 1280, 2516)]
    public async Task Match_RealScreenshot_UsesCaptionAndNativeCoordinates(string name, MapTypes expectedScene,
        float gameX, float gameY, int width)
    {
        using var color = MapMaskTestImages.Load(name);
        using var gray = new Mat();
        Cv2.CvtColor(color, gray, ColorConversionCodes.BGR2GRAY);
        using var reader = CreateReader();
        var scene = reader.Read(gray);
        Assert.Equal(expectedScene, scene);
        var map = (SceneBaseMap)MapManager.GetMap(scene!.Value, "SIFT");
        using var matcher = new BigMapCascadeMatcher(map) { LayerButtonReader = LayerButtonGeometry.Resolve };
        var timer = Stopwatch.StartNew();
        var result = await matcher.MatchAsync(gray, color);
        _output.WriteLine($"{name}: {timer.ElapsedMilliseconds}ms {result}; {matcher.LastDiagnostics}");
        AssertViewport(result, gameX, gameY, width);
        Assert.Contains("L3:", matcher.LastDiagnostics);
        Assert.Contains("L4:", matcher.LastDiagnostics);
        Assert.Contains("L5:", matcher.LastDiagnostics);
        if (name.StartsWith("enkanomiya-yachimatahime", StringComparison.Ordinal))
        {
            // 外围孤岛只有一个锚点，必须由完整地表素材定位，不能伪造 L3 多点几何结果。
            Assert.Equal("L4-SIFT", result!.Via);
            Assert.Equal(0, result.Floor);
        }
        if (name is "enkanomiya-west" or "moon-highland" or "moon-highland-4k")
        {
            Assert.Contains("离线参考定位通过", matcher.LastDiagnostics);
        }
    }

    /// <summary>不同场景重复切换及缩放，标题缓存不能把新地图识别为上一张。</summary>
    [Fact]
    public void Read_SceneSwitchSequence_DoesNotReuseOldScene()
    {
        using var reader = CreateReader();
        foreach (var (name, scene) in new[]
                 {
                     ("enkanomiya-west", MapTypes.Enkanomiya), ("moon-highland", MapTypes.MoonCanon),
                     ("moon-highland-close", MapTypes.MoonCanon), ("city-surface", MapTypes.Teyvat),
                     ("enkanomiya-close", MapTypes.Enkanomiya), ("moon-sea", MapTypes.MoonCanon)
                 })
        {
            using var color = MapMaskTestImages.Load(name);
            using var gray = color.CvtColor(ColorConversionCodes.BGR2GRAY);
            Assert.Equal(scene, reader.Read(gray));
            Assert.Equal(scene, reader.Read(gray));
        }
    }

    /// <summary>每场景的所有已校准锚点独立验证，不能用同一公式生成期望值。</summary>
    [Theory]
    [InlineData(MapTypes.Enkanomiya, 7, 15)]
    [InlineData(MapTypes.TheChasm, 9, 15)]
    [InlineData(MapTypes.SeaOfBygoneEras, 34, 30)]
    [InlineData(MapTypes.AncientSacredMountain, 36, 20)]
    [InlineData(MapTypes.TempleOfSpace, 37, 15)]
    [InlineData(MapTypes.MoonCanon, 40, 15)]
    public void WebPoints_IndependentScene_AlignsWithOfflineGameAnchors(MapTypes scene, int mapId, double tolerance)
    {
        Assert.Equal(mapId, GameWebMapCoordinateConverter.GetMysMapId(scene));
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "MapMask", $"web-anchors-{scene}.json");
        var pairs = JsonSerializer.Deserialize<double[][]>(File.ReadAllText(path))!;
        Assert.True(pairs.Length >= 15);
        foreach (var pair in pairs)
        {
            var (x, y) = GameWebMapCoordinateConverter.MysWebToGame(pair[0], pair[1], scene);
            Assert.InRange(Math.Sqrt(Math.Pow(x - pair[2], 2) + Math.Pow(y - pair[3], 2)), 0, tolerance);
        }
    }

    /// <summary>酒馆六种独立坐标系的真实锚点，覆盖与官方底图不同的比例和原点。</summary>
    [Theory]
    [InlineData(MapTypes.Enkanomiya)]
    [InlineData(MapTypes.TheChasm)]
    [InlineData(MapTypes.SeaOfBygoneEras)]
    [InlineData(MapTypes.AncientSacredMountain)]
    [InlineData(MapTypes.TempleOfSpace)]
    [InlineData(MapTypes.MoonCanon)]
    public void TavernPoints_IndependentScene_AlignsWithOfflineGameAnchors(MapTypes scene)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "MapMask", $"tavern-anchors-{scene}.json");
        var pairs = JsonSerializer.Deserialize<double[][]>(File.ReadAllText(path))!;
        Assert.True(pairs.Length >= 15);
        foreach (var pair in pairs)
        {
            var (x, y) = GameWebMapCoordinateConverter.KongyingTavernToGame(pair[0], pair[1], scene);
            Assert.InRange(Math.Sqrt(Math.Pow(x - pair[2], 2) + Math.Pow(y - pair[3], 2)), 0, 20);
        }
    }

    /// <summary>之前只保存大陆 ID 的选中类别也能在切图后取得同名点位，且不跨类别或混入大陆。</summary>
    [Fact]
    public void TavernLabels_SwitchScene_ExpandsOldSelectionAndFiltersOtherScenes()
    {
        MaskMapPointLabel[] selected = [new() { LabelId = "1", LabelIds = ["1"], Name = "宝箱", ParentId = "10" }];
        ItemTypeVo[] items =
        [
            new() { Id = 1, AreaId = 2, Name = "宝箱", TypeIdList = [10] },
            new() { Id = 2, AreaId = 15, Name = "宝箱", TypeIdList = [10] },
            new() { Id = 3, AreaId = 56, Name = "宝箱", TypeIdList = [10] },
            new() { Id = 4, AreaId = 56, Name = "宝箱", TypeIdList = [5] },
            new() { Id = 5, AreaId = 53, Name = "宝箱", TypeIdList = [10] }
        ];
        Assert.Equal("1", Assert.Single(MaskMapPointService.ResolveKongyingSceneLabels(selected, items, MapTypes.Teyvat)).LabelId);
        Assert.Equal("2", Assert.Single(MaskMapPointService.ResolveKongyingSceneLabels(selected, items, MapTypes.Enkanomiya)).LabelId);
        Assert.Equal("3", Assert.Single(MaskMapPointService.ResolveKongyingSceneLabels(selected, items, MapTypes.MoonCanon)).LabelId);
        Assert.Null(MaskMapPointService.GetKongyingScene(53));
    }

    /// <summary>独立地图上打开详情面板时保持场景，面板文字不能触发切换到提瓦特。</summary>
    [Fact]
    public void Read_DetailPanel_DoesNotReplaceKnownScene()
    {
        using var reader = CreateReader();
        using var color = MapMaskTestImages.Load("moon-highland");
        using var gray = color.CvtColor(ColorConversionCodes.BGR2GRAY);
        Assert.Equal(MapTypes.MoonCanon, reader.Read(gray));
        using var popup = MapMaskTestImages.Load("city-waypoint-popup");
        using var popupGray = popup.CvtColor(ColorConversionCodes.BGR2GRAY);
        using (var source = new Mat(popupGray, new Rect(700, 0, 260, 540)))
        using (var target = new Mat(gray, new Rect(700, 0, 260, 540))) source.CopyTo(target);
        Assert.Equal(MapTypes.MoonCanon, reader.Read(gray));
        using var matcher = new BigMapCascadeMatcher((SceneBaseMap)MapManager.GetMap(MapTypes.MoonCanon, "SIFT"));
        AssertViewport(matcher.Match(gray), -1688, -641, 1663);
    }

    /// <summary>L3 直接使用独立场景的离线锚点，不能依赖 L4 教学或提瓦特锚点。</summary>
    [Theory]
    [InlineData("enkanomiya-west", MapTypes.Enkanomiya, 539, 444, 1484)]
    [InlineData("moon-highland", MapTypes.MoonCanon, -1688, -641, 1663)]
    public void Anchors_IndependentScene_WorkWithoutSift(string name, MapTypes scene, float x, float y, int width)
    {
        using var color = MapMaskTestImages.Load(name);
        using var gray = color.CvtColor(ColorConversionCodes.BGR2GRAY);
        var map = (SceneBaseMap)MapManager.GetMap(scene, "SIFT");
        using var matcher = new BigMapAnchorMatcher(map);
        var result = matcher.Match(gray, null);
        _output.WriteLine(matcher.LastDiagnostics);
        Assert.NotNull(result);
        var rect = result.Viewport;
        var game = map.ConvertImageCoordinatesToGenshinMapCoordinates(new Point2f((float)(rect.X + rect.Width / 2), (float)(rect.Y + rect.Height / 2)))!.Value;
        Assert.InRange(game.DistanceTo(new Point2f(x, y)), 0, 20);
        Assert.InRange(rect.Width, width * 0.97, width * 1.03);
    }

    /// <summary>从真实 OCR 引擎读取裁剪文字。</summary>
    private BigMapSceneReader CreateReader() => new() { Recognize = image => _paddle.Get().OcrWithoutDetector(image) };

    /// <summary>固定地形视口坐标及宽高比，错误地图与错误比例均不能通过。</summary>
    private static void AssertViewport(MapMatchResult? result, float x, float y, int width)
    {
        Assert.NotNull(result);
        Assert.InRange(result.GenshinPosition.DistanceTo(new Point2f(x, y)), 0, 20);
        Assert.InRange(result.Viewport2048.Width, width * 0.97, width * 1.03);
        Assert.InRange(result.Viewport2048.Width / (double)result.Viewport2048.Height, 1.77, 1.79);
    }
}
