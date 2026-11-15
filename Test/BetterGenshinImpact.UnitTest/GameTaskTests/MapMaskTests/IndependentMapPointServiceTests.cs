using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.GameTask.MapMask;
using BetterGenshinImpact.Model.MaskMap;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Service.Model.MihoyoMap.Requests;
using BetterGenshinImpact.Service.Model.MihoyoMap.Responses;
using BetterGenshinImpact.Service.Model.Oauth;
using BetterGenshinImpact.Service.Tavern.Model;
using LazyCache;
using System.Globalization;
using System.Text.Json;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.MapMaskTests;

/// <summary>实际点位服务入口的场景请求、缓存隔离和图像投影回归。</summary>
public class IndependentMapPointServiceTests
{
    /// <summary>酒馆的实际服务流程须补齐旧选择，并在同名点位中只返回当前独立地图。</summary>
    [Fact]
    public async Task GetTavernPoints_OldSelection_UsesSceneLabelsAndNativeProjection()
    {
        var api = new TavernApi();
        var config = new AllConfig();
        config.MapMaskConfig.MapPointApiProvider = MapPointApiProvider.KongyingTavern;
        var service = new MaskMapPointService(new FakeLogger<MaskMapPointService>(), new CachingService(),
            null!, null!, api, config);
        MaskMapPointLabel[] selected = [new() { LabelId = "1", ParentId = "2", Name = "传送锚点" }];
        foreach (var scene in new[] { MapTypes.Enkanomiya, MapTypes.MoonCanon })
        {
            var point = Assert.Single((await service.GetPointsAsync(selected, mapType: scene)).Points);
            Assert.Equal(scene, point.MapType);
            var reference = api.References[scene];
            Assert.InRange(Math.Sqrt(Math.Pow(point.GameX - reference[2], 2) + Math.Pow(point.GameY - reference[3], 2)), 0, 20);
            Assert.True(point.ImageX > 0 && point.ImageY > 0);
        }
        Assert.Equal(1, api.MarkerCalls);
        Assert.Equal(1, api.ItemCalls);
    }

    /// <summary>同一选中类别切换地图必须请求不同 map_id；返回旧地图时可复用自己的缓存。</summary>
    [Theory]
    [InlineData(MapPointApiProvider.MihoyoMap)]
    [InlineData(MapPointApiProvider.HoYoLab)]
    public async Task GetPoints_SceneSwitch_UsesSceneRequestCacheProjectionAndDetails(MapPointApiProvider provider)
    {
        var api = new SceneApi();
        var config = new AllConfig();
        config.MapMaskConfig.MapPointApiProvider = provider;
        var service = new MaskMapPointService(new FakeLogger<MaskMapPointService>(), new CachingService(),
            api, api, null!, config);
        MaskMapPointLabel[] selected = [new() { LabelId = "3", ParentId = "1", Name = "传送锚点" }];
        var enka = Assert.Single((await service.GetPointsAsync(selected, mapType: MapTypes.Enkanomiya)).Points);
        var moon = Assert.Single((await service.GetPointsAsync(selected, mapType: MapTypes.MoonCanon)).Points);
        var world = Assert.Single((await service.GetPointsAsync(selected)).Points);
        await service.GetPointsAsync(selected, mapType: MapTypes.Enkanomiya);
        Assert.Equal(new[] { 7, 40, 2 }, api.ListMaps);
        Assert.Equal(MapTypes.Enkanomiya, enka.MapType);
        Assert.InRange(enka.GameX, -692, -690);
        Assert.InRange(enka.GameY, 1071, 1074);
        Assert.InRange(enka.ImageX, 2738, 2741);
        Assert.InRange(enka.ImageY, 974, 977);
        Assert.Equal(MapTypes.MoonCanon, moon.MapType);
        Assert.InRange(moon.GameX, -1685, -1670);
        Assert.InRange(moon.GameY, -915, -900);
        Assert.InRange(moon.ImageX, 12934, 12949);
        Assert.InRange(moon.ImageY, 4996, 5011);
        Assert.Equal(MapTypes.Teyvat, world.MapType);
        await service.GetPointInfoAsync(enka);
        await service.GetPointInfoAsync(moon);
        Assert.Equal(new[] { 7, 40 }, api.InfoMaps);
    }

    /// <summary>用确定的网页原始点位替代网络，保持实际服务的请求及转换代码。</summary>
    private sealed class SceneApi : IHoYoLabMapApiService
    {
        /// <summary>点位列表实际请求的地图。</summary>
        public List<int> ListMaps { get; } = [];

        /// <summary>点位详情实际请求的地图。</summary>
        public List<int> InfoMaps { get; } = [];

        /// <summary>返回固定的原始 API 坐标，不能从待测换算公式反向生成。</summary>
        public Task<ApiResponse<PointListData>> GetPointListAsync(PointListRequest request, CancellationToken ct = default)
        {
            ListMaps.Add(request.MapId);
            var (x, y) = request.MapId switch
            {
                7 => (889.85597, -803.78452),
                40 => (4935.883429951564, 168.06914106955446),
                _ => (0d, 0d)
            };
            return Task.FromResult(new ApiResponse<PointListData>
            {
                Data = new PointListData { PointList = [new PointItem { Id = 100 + request.MapId, LabelId = 3, XPos = x, YPos = y }] }
            });
        }

        /// <summary>记录详情请求，独立地图 ID 必须跟随点位而非当前视口。</summary>
        public Task<ApiResponse<PointInfoData>> GetPointInfoAsync(PointInfoRequest request, CancellationToken ct = default)
        {
            InfoMaps.Add(request.MapId);
            return Task.FromResult(new ApiResponse<PointInfoData> { Data = new PointInfoData() });
        }

        /// <summary>本回归直接传入已选类别，不应重新请求标签树。</summary>
        public Task<ApiResponse<LabelTreeData>> GetLabelTreeAsync(LabelTreeRequest request, CancellationToken ct = default) =>
            throw new InvalidOperationException("切换场景应保留已选标签。");
    }

    /// <summary>酒馆固定的原始点位和地区，保留生产服务的标签匹配与点位索引流程。</summary>
    private sealed class TavernApi : IKongyingTavernApiService
    {
        /// <summary>直接读取真实参考坐标，测试过程不联网。</summary>
        public Dictionary<MapTypes, double[]> References { get; } = new[] { MapTypes.Enkanomiya, MapTypes.MoonCanon }
            .ToDictionary(scene => scene, scene => JsonSerializer.Deserialize<double[][]>(File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "TestData", "MapMask", $"tavern-anchors-{scene}.json")))![0]);

        /// <summary>检查不同场景复用同一原始物品列表。</summary>
        public int ItemCalls { get; private set; }

        /// <summary>检查不同场景复用同一原始点位索引。</summary>
        public int MarkerCalls { get; private set; }

        /// <summary>返回大陆与两张独立地图的同名标签。</summary>
        public Task<IReadOnlyList<ItemTypeVo>> GetItemTypeListAsync(CancellationToken ct = default)
        {
            ItemCalls++;
            return Task.FromResult<IReadOnlyList<ItemTypeVo>>(
            [
                new() { Id = 1, AreaId = 2, Name = "传送锚点", TypeIdList = [2] },
                new() { Id = 2, AreaId = 15, Name = "传送锚点", TypeIdList = [2] },
                new() { Id = 3, AreaId = 56, Name = "传送锚点", TypeIdList = [2] }
            ]);
        }

        /// <summary>两张地图点位一起返回，生产服务必须按场景选择。</summary>
        public Task<IReadOnlyList<MarkerVo>> GetMarkerListAsync(CancellationToken ct = default)
        {
            MarkerCalls++;
            return Task.FromResult<IReadOnlyList<MarkerVo>>(References.Select(pair => new MarkerVo
            {
                Id = pair.Key == MapTypes.Enkanomiya ? 20 : 30,
                Position = string.Create(CultureInfo.InvariantCulture, $"{pair.Value[0]},{pair.Value[1]}"),
                ItemList = [new() { ItemId = pair.Key == MapTypes.Enkanomiya ? 2 : 3 }]
            }).ToArray());
        }

        /// <summary>点位服务使用既有标签图标，不需要请求图标列表。</summary>
        public Task<IReadOnlyList<IconVo>> GetIconListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<IconVo>>([]);

        /// <summary>模拟接口不应获取真实授权。</summary>
        public Task<OauthTokenResponse> GetTokenAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("离线回归不得请求授权。");
    }
}
