using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using System.Diagnostics;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.MapMask;
using OpenCvSharp;
using Xunit.Abstractions;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.MapMaskTests;

/// <summary>真实截图端到端回归，固定坐标断言防止把错误层或畸形视口视为成功。</summary>
public class BigMapCascadeMatcherTests(ITestOutputHelper output)
{
    /// <summary>输出匹配耗时和定位结果，便于比较冷启动及连续帧。</summary>
    private readonly ITestOutputHelper _output = output;

    /// <summary>真实截图的提前发布必须坐标正确，地下背景不能在提前发布时冒认为地表楼层。</summary>
    [Theory]
    [InlineData("mine-runtime-overlay", 3902, 10005, 4733, "克什尼克矿区")]
    [InlineData("mine-runtime-overlay-4k", 3902, 10005, 4733, "克什尼克矿区")]
    [InlineData("city-waypoint-popup", 4314, 9988, 5778, "新基捷城")]
    public async Task MatchAsync_RealUndergroundImage_PublishesValidCoordinatesBeforeFinalResult(
        string name, float x, float y, int width, string layerName)
    {
        using var matcher = CreateMatcher();
        using var color = MapMaskTestImages.Load(name);
        using var gray = new Mat();
        Cv2.CvtColor(color, gray, ColorConversionCodes.BGR2GRAY);
        var updates = new List<MapMatchResult>();
        var timer = Stopwatch.StartNew();
        var result = await matcher.MatchAsync(gray, color, resultReady: (ready, diagnostics) =>
        {
            AssertViewport(ready, x, y, width);
            Assert.False(gray.IsDisposed);
            Assert.DoesNotContain("L4-SIFT", ready.Via);
            Assert.Contains("L1:", diagnostics);
            Assert.Contains("L5:", diagnostics);
            if (ready.Via is "L3-Anchors" or "L4-Background") Assert.Null(ready.Floor);
            updates.Add(ready);
            _output.WriteLine($"提前发布 {timer.ElapsedMilliseconds}ms: {ready.Via}, Floor={ready.Floor}");
        });
        Assert.NotEmpty(updates);
        AssertViewport(result, x, y, width);
        Assert.Equal(-1, result!.Floor);
        Assert.Equal(layerName, result.LayerName);
        Assert.Equal(result, updates[^1]);
        _output.WriteLine($"安全回收及学习完成 {timer.ElapsedMilliseconds}ms");
    }

    /// <summary>由固定地形标志及地图素材坐标确定的视口中心和宽度。</summary>
    [Theory]
    [InlineData("city-surface", 4162, 9831, 3860)]
    [InlineData("city-surface-zoom", 4104, 9847, 4254)]
    [InlineData("station-surface", 2718, 9474, 5472)]
    public void Match_Surface_ReturnsExpectedCoordinates(string name, float x, float y, int width)
    {
        using var matcher = CreateMatcher();
        var result = Match(matcher, name);
        AssertViewport(result, x, y, width);
        Assert.Equal(0, result!.Floor);
        Assert.Equal("L4-SIFT", result.Via);
    }

    /// <summary>用户新增失败样本：详情面板、最大放大和连续缩放均须保留真实坐标与楼层。</summary>
    [Theory]
    [InlineData("city-waypoint-popup", 4314, 9988, 5778)]
    [InlineData("city-waypoint-popup-4k", 4314, 9988, 5778)]
    [InlineData("city-b1-wide-new", 3500, 10038, 5777)]
    [InlineData("city-b1-close-new", 4315, 9993, 1643)]
    [InlineData("city-b1-mid-new", 4315, 9993, 1949)]
    [InlineData("city-b1-zoom-new", 4315, 9993, 2542)]
    public void Match_NewCityScreenshots_ReturnsSelectedLayer(string name, float x, float y, int width)
    {
        using var matcher = CreateMatcher();
        Assert.NotNull(Match(matcher, "city-surface"));
        MapMatchResult? result = null;
        for (var i = 0; i < 10 && result == null; i++)
        {
            result = Match(matcher, name);
        }
        AssertViewport(result, x, y, width);
        Assert.Equal(-1, result!.Floor);
        Assert.Equal("新基捷城", result.LayerName);
    }

    /// <summary>真实运行失败截图保留日志和 Game Bar 遮挡；无地表预热时首帧就应定位正确矿区。</summary>
    [Theory]
    [InlineData("mine-runtime-overlay")]
    [InlineData("mine-runtime-overlay-4k")]
    public void Match_RuntimeScreenshot_ColdStartReturnsLayerInFirstFrame(string name)
    {
        using var matcher = CreateMatcher();
        var result = Match(matcher, name);
        AssertViewport(result, 3902, 10005, 4733);
        Assert.Equal("克什尼克矿区", result!.LayerName);
        Assert.Equal(-1, result.Floor);
        Assert.Contains("全局粗检索", matcher.LastDiagnostics);
        Assert.Contains("检测到", matcher.LastDiagnostics);

        var next = Match(matcher, name);
        AssertViewport(next, 3902, 10005, 4733);
        Assert.Contains("本地参考定位通过", matcher.LastDiagnostics);
        Assert.Contains("上次分层匹配通过", matcher.LastDiagnostics);
    }

    /// <summary>关闭地图后保留已确认的锚点；清空 SIFT 层候选后，L3 仍能独立给出位置而不冒认楼层。</summary>
    [Fact]
    public void Match_LearnedRuntimeAnchors_AfterResetWorkWithoutLayerCandidates()
    {
        using var matcher = CreateMatcher();
        Assert.Equal("克什尼克矿区", Match(matcher, "mine-runtime-overlay")?.LayerName);
        matcher.Reset();
        matcher.LayerMatcher.GetLayeredMaps().Clear();
        var result = Match(matcher, "mine-runtime-overlay");
        AssertViewport(result, 3902, 10005, 4733);
        Assert.Equal("L3-Anchors", result!.Via);
        Assert.Null(result.Floor);
        Assert.Contains("本地参考定位通过", matcher.LastDiagnostics);
        Assert.Contains("L4:", matcher.LastDiagnostics);
    }

    /// <summary>标记面板内的图标不能参与地图定位，关闭面板后仍须保持相同区域与楼层。</summary>
    [Fact]
    public void Match_MarkerPopupThenClose_PreservesFullViewportAndLayer()
    {
        using var matcher = CreateMatcher();
        Assert.NotNull(Match(matcher, "city-surface"));
        MapMatchResult? popup = null;
        for (var i = 0; i < 10 && popup == null; i++)
        {
            popup = Match(matcher, "mine-marker-popup");
        }
        AssertViewport(popup, 3678, 9965, 6303);
        Assert.Equal("克什尼克矿区", popup!.LayerName);
        Assert.Equal(-1, popup.Floor);
        Assert.Contains("已排除右侧弹框", matcher.LastDiagnostics);

        var closed = Match(matcher, "mine-b1-failure");
        AssertViewport(closed, 3678, 9965, 6257);
        Assert.Equal(popup.LayerName, closed!.LayerName);
        Assert.Contains("上次分层匹配通过", matcher.LastDiagnostics);
    }

    /// <summary>详情面板开关和缩放连续变化不能清掉相同的地下选中层或改变整屏坐标系。</summary>
    [Fact]
    public void Match_WaypointPopupAndZoomSequence_KeepsSelectedLayer()
    {
        using var matcher = CreateMatcher();
        Assert.NotNull(Match(matcher, "city-surface"));
        MapMatchResult? first = null;
        for (var i = 0; i < 10 && first == null; i++)
        {
            first = Match(matcher, "city-b1");
        }
        Assert.Equal("新基捷城", first?.LayerName);
        foreach (var (name, x, y, width) in new[]
                 {
                     ("city-waypoint-popup", 4314f, 9988f, 5778),
                     ("city-b1-close-new", 4315f, 9993f, 1643),
                     ("city-b1-mid-new", 4315f, 9993f, 1949),
                     ("city-b1-zoom-new", 4315f, 9993f, 2542),
                     ("city-waypoint-popup-4k", 4314f, 9988f, 5778)
                 })
        {
            var result = Match(matcher, name);
            AssertViewport(result, x, y, width);
            Assert.Equal("新基捷城", result!.LayerName);
            Assert.Equal(-1, result.Floor);
            Assert.Contains("上次分层匹配通过", matcher.LastDiagnostics);
        }
    }

    /// <summary>L1/L2 为并行分支提供约束，L3 独立赢得定位；连续帧消费 L1 的比例校准。</summary>
    [Fact]
    public void Match_InazumaSurface_UsesAnchorStageBeforeSift()
    {
        using var matcher = CreateMatcher();
        var first = Match(matcher, "inazuma-surface");
        AssertViewport(first, -3225, -4016, 5759);
        Assert.Equal("L3-Anchors", first!.Via);
        Assert.NotNull(matcher.LastZoomSignal);
        Assert.Contains("L1:", matcher.LastDiagnostics);
        Assert.Contains("L2:", matcher.LastDiagnostics);
        Assert.Contains("L4:", matcher.LastDiagnostics);
        Assert.Contains("L5:", matcher.LastDiagnostics);

        var next = Match(matcher, "inazuma-surface");
        AssertViewport(next, -3225, -4016, 5759);
        Assert.Equal("L3-Anchors", next!.Via);
        Assert.Contains("缩放约束", matcher.LastDiagnostics);
    }

    /// <summary>处理中取消真实截图后，须回收各分支且保留输入；下一幅地图仍能成功定位。</summary>
    [Fact]
    public async Task MatchAsync_CancelRunningFrame_RecoversForLatestMap()
    {
        using var matcher = CreateMatcher();
        using var first = MapMaskTestImages.Load("mine-runtime-overlay-4k");
        using var gray = new Mat();
        Cv2.CvtColor(first, gray, ColorConversionCodes.BGR2GRAY);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            matcher.MatchAsync(gray, first, cancellation.Token).WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.False(gray.IsDisposed);
        Assert.False(first.IsDisposed);
        matcher.InvalidateView();
        using var next = MapMaskTestImages.Load("city-b1");
        using var nextGray = new Mat();
        Cv2.CvtColor(next, nextGray, ColorConversionCodes.BGR2GRAY);
        var result = await matcher.MatchAsync(nextGray, next).WaitAsync(TimeSpan.FromSeconds(15));
        AssertViewport(result, 4161, 9830, 3858);
        Assert.Equal("新基捷城", result!.LayerName);
        Assert.Contains("L3:", matcher.LastDiagnostics);
        Assert.Contains("L4:", matcher.LastDiagnostics);
        Assert.Contains("L5:", matcher.LastDiagnostics);
    }

    /// <summary>近、中、远真实缩放经过完整级联：锚点可见时走 L3，过近或过远则回退 L4。</summary>
    [Theory]
    [InlineData("inazuma-zoom", -3224, -4016, 3496, "L3-Anchors")]
    [InlineData("inazuma-zoom-near", -3224, -4015, 1640, "L4-SIFT")]
    [InlineData("inazuma-zoom-far", -3225, -4016, 9854, "L4-SIFT")]
    public void Match_InazumaZoom_UsesActualAvailableStage(string name, float x, float y, int width, string via)
    {
        using var matcher = CreateMatcher();
        var result = Match(matcher, name);
        AssertViewport(result, x, y, width);
        Assert.Equal(via, result!.Via);
        Assert.NotNull(matcher.LastZoomSignal);
    }

    /// <summary>L1 校准须跟随真实缩放变化，不能使先前比例阻止后续锚点定位。</summary>
    [Fact]
    public void Match_AnchorZoomChanges_UpdatesScaleCalibration()
    {
        using var matcher = CreateMatcher();
        Assert.Equal("L3-Anchors", Match(matcher, "inazuma-surface")!.Via);
        var zoomed = Match(matcher, "inazuma-zoom");
        AssertViewport(zoomed, -3224, -4016, 3496);
        Assert.Equal("L3-Anchors", zoomed!.Via);
        var restored = Match(matcher, "inazuma-surface");
        AssertViewport(restored, -3225, -4016, 5759);
        Assert.Equal("L3-Anchors", restored!.Via);
    }

    /// <summary>L2 的地下信号必须阻止 L3 把已定位的平面视口误报成地表。</summary>
    [Fact]
    public void Match_AnchorPositionWithUndergroundSignal_DoesNotClaimSurface()
    {
        using var matcher = CreateMatcher();
        // 隔离层信号对级联的作用；模板读取由真实地下截图测试覆盖。
        matcher.LayerButtonReader = _ => new LayerButtonSignal(true, 1);
        matcher.LayerMatcher.GetLayeredMaps().Clear();
        var result = Match(matcher, "inazuma-surface");
        AssertViewport(result, -3225, -4016, 5759);
        Assert.Equal("L3-Anchors", result!.Via);
        Assert.Null(result.Floor);
        Assert.Contains("L5:", matcher.LastDiagnostics);
    }

    /// <summary>冷启动跨帧推进；平移复用当前层，选中同序号的其它地区时不可沿用旧层名。</summary>
    [Fact]
    public void Match_CityUnderground_ColdSearchThenPan_ReturnsCorrectLayerAndViewport()
    {
        using var matcher = CreateMatcher();
        MapMatchResult? first = null;
        for (var i = 0; i < 80 && first == null; i++)
        {
            first = Match(matcher, "city-b1");
        }

        AssertViewport(first, 4161, 9830, 3858);
        Assert.Equal(-1, first!.Floor);
        Assert.Equal("新基捷城", first.LayerName);
        var next = Match(matcher, "city-b1-pan");
        AssertViewport(next, 4103, 9847, 4254);
        Assert.Equal(first.LayerName, next!.LayerName);

        var east = Match(matcher, "city-b1-east");
        AssertViewport(east, 4103, 9847, 4254);
        Assert.Equal("克什尼克矿区", east!.LayerName);
    }

    /// <summary>地下保留的地表背景可用于定位，但不能把它标成地表层。</summary>
    [Theory]
    [InlineData("station-b1", 5472)]
    [InlineData("station-b2", 5472)]
    [InlineData("station-b2-zoom", 3792)]
    public void Match_UndergroundBackground_ReturnsLocationWithoutSurfaceFloor(string name, int width)
    {
        using var matcher = CreateMatcher();
        var result = Match(matcher, name);
        AssertViewport(result, 2718, 9474, width);
        Assert.NotEqual(0, result!.Floor);
    }

    /// <summary>关闭地图后重新打开，缓存不应使同一区域的地下信号污染地表结果。</summary>
    [Fact]
    public void Reset_UndergroundThenSurface_ReturnsSurface()
    {
        using var matcher = CreateMatcher();
        Assert.NotNull(Match(matcher, "station-b1"));
        matcher.Reset();
        var result = Match(matcher, "station-surface");
        AssertViewport(result, 2718, 9474, 5472);
        Assert.Equal(0, result!.Floor);
    }

    /// <summary>地表切地下沿用区域先验，少量帧内找到当前区域，无需再次全大陆轮转。</summary>
    [Fact]
    public void Match_SurfaceThenUnderground_ReusesAreaPrior()
    {
        using var matcher = CreateMatcher();
        Assert.NotNull(Match(matcher, "city-surface"));
        MapMatchResult? underground = null;
        for (var i = 0; i < 3 && underground == null; i++)
        {
            underground = Match(matcher, "city-b1");
        }
        AssertViewport(underground, 4161, 9830, 3858);
        Assert.Equal("新基捷城", underground!.LayerName);
        Assert.Equal(-1, underground.Floor);
    }

    /// <summary>切换第二层后不能被第一层保留的相同地形误导，必须识别选中层的外观。</summary>
    [Fact]
    public void Match_CitySecondFloor_DoesNotReuseFirstFloorBackgroundAsLayerIdentity()
    {
        using var matcher = CreateMatcher();
        Assert.NotNull(Match(matcher, "city-surface-zoom"));
        MapMatchResult? result = null;
        for (var i = 0; i < 10 && result == null; i++)
        {
            result = Match(matcher, "city-b2");
        }

        AssertViewport(result, 4103, 9847, 4254);
        Assert.Equal(-2, result!.Floor);
        Assert.Equal("地底的列车站", result.LayerName);
    }

    /// <summary>用户提供的小缩放矿区截图：缩略图与原始 4K 都必须通过完整定位。</summary>
    [Theory]
    [InlineData("mine-b1-failure")]
    [InlineData("mine-b1-failure-4k")]
    public void Match_MineAtSmallZoom_ReturnsSelectedLayer(string name)
    {
        using var matcher = CreateMatcher();
        Assert.NotNull(Match(matcher, "city-surface"));
        MapMatchResult? result = null;
        for (var i = 0; i < 10 && result == null; i++)
        {
            result = Match(matcher, name);
        }

        AssertViewport(result, 3678, 9965, 6257);
        Assert.Equal(-1, result!.Floor);
        Assert.Equal("克什尼克矿区", result.LayerName);
        Assert.Equal("L5-LayerSIFT", result.Via);

        var next = Match(matcher, name);
        AssertViewport(next, 3678, 9965, 6257);
        Assert.Equal(result.LayerName, next!.LayerName);
    }

    /// <summary>直接在地下打开地图，没有地表位置先验时也必须找到用户截图的正确层。</summary>
    [Fact]
    public void Match_MineColdStart_ReturnsSelectedLayer()
    {
        using var matcher = CreateMatcher();
        MapMatchResult? result = null;
        for (var i = 0; i < 80 && result == null; i++)
        {
            result = Match(matcher, "mine-b1-failure");
        }

        AssertViewport(result, 3678, 9965, 6257);
        Assert.Equal("克什尼克矿区", result!.LayerName);
        Assert.Equal(-1, result.Floor);
        Assert.Equal("L5-LayerSIFT", result.Via);
    }

    /// <summary>高度放大的局部在快速层图中缺少细节时，完整轮转后须回退到高分辨率。</summary>
    [Fact]
    public void Match_HighZoomDetail_RetriesAtHigherResolution()
    {
        using var matcher = CreateMatcher();
        // 此测试隔离分辨率回退；真实截图的按钮信号另由生产入口回归覆盖。
        matcher.LayerButtonReader = _ => new LayerButtonSignal(true, 1);
        var layers = matcher.LayerMatcher.GetLayeredMaps();
        layers.RemoveAll(l => l.LayerId != "LayeredMap_31_-1");
        var layer = Assert.Single(layers);
        using var source = new Mat(layer.ImagePath, ImreadModes.Grayscale);
        using var crop = new Mat(source, new Rect(400, 100, 200, 112));
        using var query = new Mat();
        using var color = new Mat();
        Cv2.Resize(crop, query, new Size(960, 540));
        Cv2.CvtColor(query, color, ColorConversionCodes.GRAY2BGR);

        Assert.Null(matcher.Match(query, color));
        var result = matcher.Match(query, color);
        Assert.NotNull(result);
        Assert.Equal("L5-LayerSIFT", result.Via);
        Assert.Equal(layer.Name, result.LayerName);
        Assert.Equal(-1, result.Floor);
        Assert.InRange(result.Viewport2048.Width, 198, 202);
    }

    /// <summary>空帧、纯色和少量圆形图标都不能产生位置，输入 Mat 必须保持可用。</summary>
    [Fact]
    public void Match_FeaturelessImage_ReturnsNullAndPreservesInput()
    {
        using var matcher = CreateMatcher();
        using var empty = new Mat();
        using var image = new Mat(540, 960, MatType.CV_8UC1, Scalar.Gray);
        Assert.Null(matcher.Match(empty));
        Assert.Contains("输入为空", matcher.LastDiagnostics);
        Assert.Null(matcher.Match(image));
        Assert.Contains("特征不足", matcher.LastDiagnostics);
        Cv2.Circle(image, 480, 270, 50, Scalar.White, -1);
        Assert.Null(matcher.Match(image));
        Assert.False(image.IsDisposed);
        Assert.Equal(960, image.Width);
        matcher.Reset();
        Assert.Empty(matcher.LastDiagnostics);
    }

    /// <summary>元数据加载必须包含正楼层，且无重复 ID。</summary>
    [Fact]
    public void GetLayeredMaps_LoadsMetadataIncludingPositiveFloors()
    {
        using var matcher = CreateMatcher();
        var layers = matcher.LayerMatcher.GetLayeredMaps();
        Assert.NotEmpty(layers);
        Assert.Contains(layers, l => l.Floor > 0);
        Assert.Equal(layers.Count, layers.Select(l => l.LayerId).Distinct().Count());
        Assert.All(layers, l => Assert.True(l.Scale > 0));
    }

    /// <summary>使用生产按钮识别，禁止测试通过注入错误信号掩盖实际入口的问题。</summary>
    private static BigMapCascadeMatcher CreateMatcher() => new(new TeyvatMap())
    {
        LayerButtonReader = LayerButtonGeometry.Resolve
    };

    /// <summary>加载并匹配一张真实截图，所有原生输入均由本方法释放。</summary>
    private MapMatchResult? Match(BigMapCascadeMatcher matcher, string name)
    {
        using var color = MapMaskTestImages.Load(name);
        using var gray = new Mat();
        Cv2.CvtColor(color, gray, ColorConversionCodes.BGR2GRAY);
        var sw = Stopwatch.StartNew();
        var result = matcher.Match(gray, color);
        _output.WriteLine($"{name}: {sw.ElapsedMilliseconds}ms {result}; {matcher.LastDiagnostics}");
        Assert.False(gray.IsDisposed);
        return result;
    }

    /// <summary>坐标误差不超过 20 游戏单位，宽度误差不超过 3%，宽高比必须对应截图。</summary>
    private static void AssertViewport(MapMatchResult? result, float x, float y, int width)
    {
        Assert.NotNull(result);
        Assert.InRange(result.GenshinPosition.DistanceTo(new Point2f(x, y)), 0, 20);
        Assert.InRange(result.Viewport2048.Width, width * 0.97, width * 1.03);
        Assert.InRange(result.Viewport2048.Width / (double)result.Viewport2048.Height, 1.77, 1.79);
        Assert.InRange(result.Confidence, 0.2, 1);
    }
}
