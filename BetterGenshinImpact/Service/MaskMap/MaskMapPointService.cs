using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.GameTask.MapMask;
using BetterGenshinImpact.Model.MaskMap;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Service.Model.MihoyoMap.Requests;
using BetterGenshinImpact.Service.Model.MihoyoMap.Responses;
using BetterGenshinImpact.Service.Tavern.Model;
using LazyCache;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace BetterGenshinImpact.Service.MaskMap;

public sealed class MaskMapPointService : IMaskMapPointService
{
    private readonly ILogger<MaskMapPointService> _logger;
    private readonly IAppCache _cache;
    private readonly IMihoyoMapApiService _mihoyoMapApi;
    private readonly IKongyingTavernApiService _kongyingTavernApi;

    public MaskMapPointService(
        ILogger<MaskMapPointService> logger,
        IAppCache cache,
        IMihoyoMapApiService mihoyoMapApi,
        IKongyingTavernApiService kongyingTavernApi)
    {
        _logger = logger;
        _cache = cache;
        _mihoyoMapApi = mihoyoMapApi;
        _kongyingTavernApi = kongyingTavernApi;
    }

    public Task<IReadOnlyList<MaskMapPointLabel>> GetLabelCategoriesAsync(CancellationToken ct = default)
    {
        return GetProvider() switch
        {
            MapPointApiProvider.KongyingTavern => GetKongyingLabelCategoriesAsync(ct),
            _ => GetMihoyoLabelCategoriesAsync(ct)
        };
    }

    public Task<MaskMapPointsResult> GetPointsAsync(IReadOnlyList<MaskMapPointLabel> selectedItems, CancellationToken ct = default)
    {
        return GetProvider() switch
        {
            MapPointApiProvider.KongyingTavern => GetKongyingPointsAsync(selectedItems, ct),
            _ => GetMihoyoPointsAsync(selectedItems, ct)
        };
    }

    public Task<MaskMapPointInfo> GetPointInfoAsync(MaskMapPoint point, CancellationToken ct = default)
    {
        return GetProvider() switch
        {
            MapPointApiProvider.KongyingTavern => GetKongyingPointInfoAsync(point, ct),
            _ => GetMihoyoPointInfoAsync(point, ct)
        };
    }

    private static MapPointApiProvider GetProvider()
    {
        return TaskContext.Instance().Config.MapMaskConfig.MapPointApiProvider;
    }

    private async Task<IReadOnlyList<MaskMapPointLabel>> GetMihoyoLabelCategoriesAsync(CancellationToken ct)
    {
        ApiResponse<LabelTreeData>? resp = null;
        try
        {
            resp = await _mihoyoMapApi.GetLabelTreeAsync(new LabelTreeRequest(), ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "调用米游社地图接口获取点位树失败");
        }

        if (resp == null || resp.Retcode != 0 || resp.Data == null)
        {
            resp = TryLoadLabelTreeFromLocalExample();
        }

        if (resp == null || resp.Retcode != 0 || resp.Data == null)
        {
            return Array.Empty<MaskMapPointLabel>();
        }

        var categories = resp.Data.Tree
            .OrderBy(x => x.Sort)
            .ThenBy(x => x.DisplayPriority)
            .Select(cat =>
            {
                var itemsSource = cat.Children != null && cat.Children.Count > 0
                    ? cat.Children
                    : new List<LabelNode> { cat };
                var children = itemsSource
                    .OrderBy(x => x.Sort)
                    .ThenBy(x => x.DisplayPriority)
                    .Select(x => new MaskMapPointLabel
                    {
                        LabelId = x.Id.ToString(CultureInfo.InvariantCulture),
                        ParentId = cat.Id.ToString(CultureInfo.InvariantCulture),
                        Name = x.Name,
                        IconUrl = x.Icon,
                        PointCount = x.PointCount
                    })
                    .ToList();

                return new MaskMapPointLabel
                {
                    LabelId = cat.Id.ToString(CultureInfo.InvariantCulture),
                    Name = cat.Name,
                    IconUrl = cat.Icon,
                    Children = children
                };
            })
            .ToList();

        return categories;
    }

    private async Task<MaskMapPointsResult> GetMihoyoPointsAsync(IReadOnlyList<MaskMapPointLabel> selectedItems, CancellationToken ct)
    {
        if (selectedItems.Count == 0)
        {
            return new MaskMapPointsResult();
        }

        var selectedSecondLevelIds = selectedItems.Select(x => x.LabelId).ToHashSet(StringComparer.Ordinal);
        var parentLabelIds = selectedItems
            .Select(x => x.ParentId)
            .Where(x => int.TryParse(x, out _))
            .Select(x => int.Parse(x, CultureInfo.InvariantCulture))
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        var labels = selectedItems
            .GroupBy(x => x.LabelId, StringComparer.Ordinal)
            .Select(g => g.First())
            .Select(x => new MaskMapPointLabel
            {
                LabelId = x.LabelId,
                Name = x.Name,
                IconUrl = x.IconUrl
            })
            .ToList();

        if (parentLabelIds.Count == 0)
        {
            return new MaskMapPointsResult
            {
                Labels = labels,
                Points = Array.Empty<MaskMapPoint>()
            };
        }

        ApiResponse<PointListData> resp;
        try
        {
            resp = await _mihoyoMapApi.GetPointListCacheAsync(new PointListRequest
            {
                LabelIds = parentLabelIds
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "调用米游社地图接口获取点位列表失败");
            return new MaskMapPointsResult { Labels = labels, Points = Array.Empty<MaskMapPoint>() };
        }

        if (resp.Retcode != 0 || resp.Data == null)
        {
            _logger.LogWarning("获取地图点位列表失败: {Retcode} {Message}", resp.Retcode, resp.Message);
            return new MaskMapPointsResult { Labels = labels, Points = Array.Empty<MaskMapPoint>() };
        }

        var map = MapManager.GetMap(MapTypes.Teyvat, TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod);
        var points = resp.Data.PointList
            .Where(x => selectedSecondLevelIds.Contains(x.LabelId.ToString(CultureInfo.InvariantCulture)))
            .Select(x =>
            {
                var m = new MaskMapPoint
                {
                    Id = x.Id.ToString(CultureInfo.InvariantCulture),
                    X = x.XPos,
                    Y = x.YPos,
                    LabelId = x.LabelId.ToString(CultureInfo.InvariantCulture)
                };

                (m.GameX, m.GameY) = GameWebMapCoordinateConverter.MysWebToGame(m.X, m.Y);
                var imageCoordinates = map.ConvertGenshinMapCoordinatesToImageCoordinates(new Point2f((float)m.GameX, (float)m.GameY));
                (m.ImageX, m.ImageY) = (imageCoordinates.X, imageCoordinates.Y);
                return m;
            })
            .ToList();

        return new MaskMapPointsResult
        {
            Labels = labels,
            Points = points
        };
    }

    private async Task<MaskMapPointInfo> GetMihoyoPointInfoAsync(MaskMapPoint point, CancellationToken ct)
    {
        if (!int.TryParse(point.Id, out var pointId))
        {
            return new MaskMapPointInfo { Text = $"点位 ID 非法: {point.Id}" };
        }

        try
        {
            var resp = await _mihoyoMapApi.GetPointInfoAsync(new PointInfoRequest { PointId = pointId }, ct);
            if (resp.Retcode != 0 || resp.Data == null)
            {
                return new MaskMapPointInfo { Text = $"查询失败: {resp.Retcode} {resp.Message}" };
            }

            var content = (resp.Data.Info.Content ?? string.Empty).Trim();
            var imageUrl = string.Empty;
            if (resp.Data.Info.UrlList is { Count: > 0 })
            {
                imageUrl = resp.Data.Info.UrlList[0] ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                imageUrl = resp.Data.Info.Img ?? string.Empty;
            }

            return new MaskMapPointInfo
            {
                Text = string.IsNullOrEmpty(content) ? "暂无描述" : content,
                ImageUrl = imageUrl
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "查询米游社地图点位详情失败");
            return new MaskMapPointInfo { Text = "查询失败" };
        }
    }

    private async Task<IReadOnlyList<MaskMapPointLabel>> GetKongyingLabelCategoriesAsync(CancellationToken ct)
    {
        var items = await GetKongyingItemTypeListCachedAsync(ct);
        var iconUrlById = await GetKongyingIconUrlByIdCachedAsync(ct);

        var children = items
            .Where(x => x.Id != null)
            .Select(x => new MaskMapPointLabel
            {
                LabelId = x.Id!.Value.ToString(CultureInfo.InvariantCulture),
                ParentId = "KongyingTavern",
                Name = x.Name ?? string.Empty,
                IconUrl = (x.IconId != null && iconUrlById.TryGetValue(x.IconId.Value, out var url)) ? url : string.Empty,
                PointCount = x.Count ?? 0
            })
            .OrderBy(x => x.Name)
            .ToList();

        return new List<MaskMapPointLabel>
        {
            new()
            {
                LabelId = "KongyingTavern",
                Name = "空荧酒馆",
                IconUrl = string.Empty,
                Children = children
            }
        };
    }

    private async Task<MaskMapPointsResult> GetKongyingPointsAsync(IReadOnlyList<MaskMapPointLabel> selectedItems, CancellationToken ct)
    {
        if (selectedItems.Count == 0)
        {
            return new MaskMapPointsResult();
        }

        var selectedItemIds = selectedItems
            .Select(x => long.TryParse(x.LabelId, out var v) ? v : (long?)null)
            .Where(x => x != null)
            .Select(x => x!.Value)
            .ToHashSet();

        var labels = selectedItems
            .GroupBy(x => x.LabelId, StringComparer.Ordinal)
            .Select(g => g.First())
            .Select(x => new MaskMapPointLabel
            {
                LabelId = x.LabelId,
                Name = x.Name,
                IconUrl = x.IconUrl
            })
            .ToList();

        if (selectedItemIds.Count == 0)
        {
            return new MaskMapPointsResult { Labels = labels, Points = Array.Empty<MaskMapPoint>() };
        }

        var markers = await GetKongyingMarkerListCachedAsync(ct);
        var map = MapManager.GetMap(MapTypes.Teyvat, TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod);

        var points = new List<MaskMapPoint>(capacity: Math.Min(markers.Count, 4096));
        foreach (var marker in markers)
        {
            ct.ThrowIfCancellationRequested();

            if (marker.Id == null || string.IsNullOrWhiteSpace(marker.Position) || marker.ItemList == null || marker.ItemList.Count == 0)
            {
                continue;
            }

            var match = marker.ItemList.FirstOrDefault(x => x.ItemId != null && selectedItemIds.Contains(x.ItemId.Value));
            if (match?.ItemId == null)
            {
                continue;
            }

            if (!TryParseKongyingPosition(marker.Position, out var x, out var y))
            {
                continue;
            }

            var m = new MaskMapPoint
            {
                Id = marker.Id.Value.ToString(CultureInfo.InvariantCulture),
                X = x,
                Y = y,
                GameX = x,
                GameY = y,
                LabelId = match.ItemId.Value.ToString(CultureInfo.InvariantCulture)
            };

            var imageCoordinates = map.ConvertGenshinMapCoordinatesToImageCoordinates(new Point2f((float)m.GameX, (float)m.GameY));
            (m.ImageX, m.ImageY) = (imageCoordinates.X, imageCoordinates.Y);

            points.Add(m);
        }

        return new MaskMapPointsResult
        {
            Labels = labels,
            Points = points
        };
    }

    private async Task<MaskMapPointInfo> GetKongyingPointInfoAsync(MaskMapPoint point, CancellationToken ct)
    {
        if (!long.TryParse(point.Id, out var markerId))
        {
            return new MaskMapPointInfo { Text = $"点位 ID 非法: {point.Id}" };
        }

        var markers = await GetKongyingMarkerListCachedAsync(ct);
        var marker = markers.FirstOrDefault(x => x.Id == markerId);
        if (marker == null)
        {
            return new MaskMapPointInfo { Text = "暂无描述" };
        }

        var text = (marker.Content ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            text = (marker.MarkerTitle ?? string.Empty).Trim();
        }

        return new MaskMapPointInfo
        {
            Text = string.IsNullOrWhiteSpace(text) ? "暂无描述" : text,
            ImageUrl = marker.Picture ?? string.Empty
        };
    }

    private Task<IReadOnlyList<ItemTypeVo>> GetKongyingItemTypeListCachedAsync(CancellationToken ct)
    {
        return _cache.GetOrAddAsync(
                "kongying-tavern:item-types",
                async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6);
                    var list = await _kongyingTavernApi.GetItemTypeListAsync(CancellationToken.None);
                    return list;
                })
            .WaitAsync(ct);
    }

    private Task<IReadOnlyList<MarkerVo>> GetKongyingMarkerListCachedAsync(CancellationToken ct)
    {
        return _cache.GetOrAddAsync(
                "kongying-tavern:markers",
                async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
                    var list = await _kongyingTavernApi.GetMarkerListAsync(CancellationToken.None);
                    return list;
                })
            .WaitAsync(ct);
    }

    private Task<Dictionary<long, string>> GetKongyingIconUrlByIdCachedAsync(CancellationToken ct)
    {
        return _cache.GetOrAddAsync(
                "kongying-tavern:icons-by-id",
                async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6);
                    var icons = await _kongyingTavernApi.GetIconListAsync(CancellationToken.None);
                    return icons
                        .Where(x => x.Id != null && !string.IsNullOrWhiteSpace(x.Url))
                        .GroupBy(x => x.Id!.Value)
                        .ToDictionary(g => g.Key, g => g.First().Url!);
                })
            .WaitAsync(ct);
    }

    private static bool TryParseKongyingPosition(string position, out double x, out double y)
    {
        x = 0;
        y = 0;

        if (string.IsNullOrWhiteSpace(position))
        {
            return false;
        }

        var s = position.Trim();

        if (s.StartsWith("[", StringComparison.Ordinal) || s.StartsWith("{", StringComparison.Ordinal))
        {
            try
            {
                var token = JToken.Parse(s);
                if (token is JArray arr && arr.Count >= 2)
                {
                    x = arr[0]!.ToObject<double>();
                    y = arr[1]!.ToObject<double>();
                    return true;
                }

                if (token is JObject obj)
                {
                    var xt = obj["x"] ?? obj["X"] ?? obj["lng"] ?? obj["lon"];
                    var yt = obj["y"] ?? obj["Y"] ?? obj["lat"];
                    if (xt != null && yt != null)
                    {
                        x = xt.ToObject<double>();
                        y = yt.ToObject<double>();
                        return true;
                    }
                }
            }
            catch
            {
            }
        }

        var parts = s
            .Split(new[] { ',', ' ', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x))
        {
            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.CurrentCulture, out x))
            {
                return false;
            }
        }

        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y))
        {
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.CurrentCulture, out y))
            {
                return false;
            }
        }

        return true;
    }

    private static ApiResponse<LabelTreeData>? TryLoadLabelTreeFromLocalExample()
    {
        try
        {
            var root = AppContext.BaseDirectory;
            var path = Path.Combine(root, ".trae", "documents", "tree.json");
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<ApiResponse<LabelTreeData>>(json);
        }
        catch
        {
            return null;
        }
    }
}
