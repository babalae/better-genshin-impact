using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using BetterGenshinImpact.Service.Tavern;
using BetterGenshinImpact.Service.Tavern.Model;
using LazyCache;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OpenCvSharp;

namespace BetterGenshinImpact.Service;

public sealed class MaskMapPointService : IMaskMapPointService
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromHours(5);

    // 酒馆（空荧酒馆）点位标签树的第一层 Label 类别定义（固定、手工维护），用于构建第一层节点以及限定第二层归类范围。
    private static readonly IReadOnlyList<(long Id, long IconId, string Name)> KongyingFirstLayerLabelDefinitions = new (long Id, long IconId, string Name)[]
    {
        (10, 290, "宝箱-品质"),
        (11, 290, "宝箱-获取"),
        (2, 291, "见闻"),
        (3, 292, "特产"),
        (4, 293, "矿物"),
        (5, 294, "怪物"),
        (6, 295, "食材"),
        (7, 296, "素材"),
        (8, 297, "家园"),
        (1, 298, "活动")
    };

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
            resp = await GetMihoyoPointListCacheAsync(parentLabelIds, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "调用米游社地图接口获取点位列表失败");
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

    private Task<ApiResponse<PointListData>> GetMihoyoPointListCacheAsync(IReadOnlyList<int> parentLabelIds, CancellationToken ct)
    {
        var labelIds = parentLabelIds?.Distinct().OrderBy(x => x).ToArray() ?? Array.Empty<int>();
        var key = $"mihoyo-map:point-list:2:ys_obc:zh-cn:{string.Join(",", labelIds)}";
        var request = new PointListRequest
        {
            LabelIds = labelIds.ToList()
        };

        return _cache.GetOrAddAsync(
                key,
                async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = CacheDuration;
                    return await _mihoyoMapApi.GetPointListAsync(request, CancellationToken.None);
                })
            .WaitAsync(ct);
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
            var imageUrl = resp.Data.Info.Img ?? string.Empty;
            var urlList = resp.Data.Info.UrlList
                .Where(x => !string.IsNullOrWhiteSpace(x?.Url))
                .Select(x => new MaskMapLink
                {
                    Text = x.Text ?? string.Empty,
                    Url = x.Url ?? string.Empty
                })
                .ToList();

            return new MaskMapPointInfo
            {
                Text = string.IsNullOrEmpty(content) ? "暂无描述" : content,
                ImageUrl = imageUrl,
                UrlList = urlList
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
        var iconUrlById = await GetKongyingIconUrlByIdCachedAsync(ct);
        var childrenByCategoryId = await GetKongyingChildrenByCategoryIdCachedAsync(KongyingFirstLayerLabelDefinitions, ct);

        var categories = new List<MaskMapPointLabel>(KongyingFirstLayerLabelDefinitions.Count);
        foreach (var def in KongyingFirstLayerLabelDefinitions)
        {
            var catIconUrl = iconUrlById.TryGetValue(def.IconId, out var iconUrl) ? iconUrl : string.Empty;
            var children = childrenByCategoryId.TryGetValue(def.Id, out var list) ? list : Array.Empty<MaskMapPointLabel>();

            categories.Add(new MaskMapPointLabel
            {
                LabelId = def.Id.ToString(CultureInfo.InvariantCulture),
                ParentId = "KongyingTavern",
                Name = def.Name,
                IconUrl = catIconUrl,
                PointCount = children.Sum(x => x.PointCount),
                Children = children
            });
        }

        return categories;
    }

    private Task<IReadOnlyDictionary<long, IReadOnlyList<MaskMapPointLabel>>> GetKongyingChildrenByCategoryIdCachedAsync(
        IReadOnlyList<(long Id, long IconId, string Name)> firstLayerLabelDefinitions,
        CancellationToken ct)
    {
        var categoryIds = firstLayerLabelDefinitions
            .Select(x => x.Id)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();
        var key = $"kongying-tavern:children-by-category-id:{string.Join(",", categoryIds)}";

        return _cache.GetOrAddAsync<IReadOnlyDictionary<long, IReadOnlyList<MaskMapPointLabel>>>(
                key,
                async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = CacheDuration;
                    var items = await GetKongyingItemTypeListCachedAsync(CancellationToken.None);
                    var iconUrlById = await GetKongyingIconUrlByIdCachedAsync(CancellationToken.None);

                    var dict = categoryIds.ToDictionary(x => x, _ => new Dictionary<string, (long MinId, List<long> LabelIds, string IconUrl, int PointCount)>(StringComparer.Ordinal));

                    foreach (var item in items)
                    {
                        if (item.Id == null || item.TypeIdList == null || item.TypeIdList.Count == 0)
                        {
                            continue;
                        }

                        var itemId = item.Id.Value;
                        var itemCount = item.Count ?? 0;
                        var itemIconUrl = (item.IconId != null && iconUrlById.TryGetValue(item.IconId.Value, out var url)) ? url : string.Empty;
                        var nameKey = (item.Name ?? string.Empty).Trim();
                        foreach (var typeId in item.TypeIdList)
                        {
                            if (!dict.TryGetValue(typeId, out var byName))
                            {
                                continue;
                            }

                            if (!byName.TryGetValue(nameKey, out var acc))
                            {
                                acc = (itemId, new List<long>(), itemIconUrl, 0);
                            }

                            acc.LabelIds.Add(itemId);
                            acc.PointCount += itemCount;
                            if (itemId < acc.MinId)
                            {
                                acc.MinId = itemId;
                            }

                            byName[nameKey] = acc;
                        }
                    }

                    var result = dict.ToDictionary(
                        x => x.Key,
                        x =>
                        {
                            var parentId = x.Key.ToString(CultureInfo.InvariantCulture);
                            var list = x.Value
                                .Select(kv =>
                                {
                                    var acc = kv.Value;
                                    var ids = acc.LabelIds
                                        .Distinct()
                                        .OrderBy(id => id)
                                        .Select(id => id.ToString(CultureInfo.InvariantCulture))
                                        .ToArray();

                                    return new MaskMapPointLabel
                                    {
                                        LabelId = acc.MinId.ToString(CultureInfo.InvariantCulture),
                                        LabelIds = ids,
                                        ParentId = parentId,
                                        Name = kv.Key,
                                        IconUrl = acc.IconUrl,
                                        PointCount = acc.PointCount
                                    };
                                })
                                .OrderBy(i => i.Name, StringComparer.Ordinal)
                                .ToList();

                            return (IReadOnlyList<MaskMapPointLabel>)list;
                        });
                    return result;
                })
            .WaitAsync(ct);
    }

    private async Task<MaskMapPointsResult> GetKongyingPointsAsync(IReadOnlyList<MaskMapPointLabel> selectedItems, CancellationToken ct)
    {
        if (selectedItems.Count == 0)
        {
            return new MaskMapPointsResult();
        }

        IEnumerable<string> GetEffectiveIds(MaskMapPointLabel item) =>
            item.LabelIds is { Count: > 0 } ? item.LabelIds : new[] { item.LabelId };

        var selectedItemIdsInOrder = new List<long>(capacity: selectedItems.Count);
        var selectedItemIds = new HashSet<long>();
        foreach (var item in selectedItems)
        {
            foreach (var idStr in GetEffectiveIds(item))
            {
                if (!long.TryParse(idStr, out var id))
                {
                    continue;
                }

                if (selectedItemIds.Add(id))
                {
                    selectedItemIdsInOrder.Add(id);
                }
            }
        }

        var labelsById = new Dictionary<string, MaskMapPointLabel>(StringComparer.Ordinal);
        foreach (var item in selectedItems)
        {
            foreach (var idStr in GetEffectiveIds(item))
            {
                if (string.IsNullOrWhiteSpace(idStr))
                {
                    continue;
                }

                if (!labelsById.ContainsKey(idStr))
                {
                    labelsById.Add(idStr, new MaskMapPointLabel
                    {
                        LabelId = idStr,
                        Name = item.Name,
                        IconUrl = item.IconUrl
                    });
                }
            }
        }

        var labels = labelsById.Values.ToList();

        if (selectedItemIds.Count == 0)
        {
            return new MaskMapPointsResult { Labels = labels, Points = Array.Empty<MaskMapPoint>() };
        }

        var markersByItemId = await GetKongyingMarkersByItemIdCachedAsync(ct);
        var map = MapManager.GetMap(MapTypes.Teyvat, TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod);

        var markerById = new Dictionary<long, (MarkerVo Marker, long LabelItemId)>();
        foreach (var selectedItemId in selectedItemIdsInOrder)
        {
            if (!markersByItemId.TryGetValue(selectedItemId, out var markers))
            {
                continue;
            }

            foreach (var marker in markers)
            {
                if (marker.Id == null)
                {
                    continue;
                }

                var markerId = marker.Id.Value;
                if (markerById.ContainsKey(markerId))
                {
                    continue;
                }

                markerById.Add(markerId, (marker, selectedItemId));
            }
        }

        var points = new List<MaskMapPoint>(capacity: Math.Min(markerById.Count, 4096));
        foreach (var kv in markerById)
        {
            ct.ThrowIfCancellationRequested();

            var markerId = kv.Key;
            var marker = kv.Value.Marker;
            var labelItemId = kv.Value.LabelItemId;

            if (string.IsNullOrWhiteSpace(marker.Position))
            {
                continue;
            }

            if (!TryParseKongyingPosition(marker.Position, out var x, out var y))
            {
                continue;
            }

            var m = new MaskMapPoint
            {
                Id = markerId.ToString(CultureInfo.InvariantCulture),
                X = x,
                Y = y,
                LabelId = labelItemId.ToString(CultureInfo.InvariantCulture),
                VideoUrls = string.IsNullOrWhiteSpace(marker.VideoPath)
                    ? new List<MaskMapLink>()
                    : new List<MaskMapLink>
                    {
                        new()
                        {
                            Text = string.Empty,
                            Url = marker.VideoPath!.Trim()
                        }
                    }
            };
            
            (m.GameX, m.GameY) = GameWebMapCoordinateConverter.KongyingTavernToGame(m.X, m.Y);
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

        var markerById = await GetKongyingMarkerByIdCachedAsync(ct);
        if (!markerById.TryGetValue(markerId, out var marker))
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
            ImageUrl = marker.Picture ?? string.Empty,
            UrlList = string.IsNullOrWhiteSpace(marker.VideoPath)
                ? Array.Empty<MaskMapLink>()
                : new[]
                {
                    new MaskMapLink
                    {
                        Text = string.Empty,
                        Url = marker.VideoPath!.Trim()
                    }
                }
        };
    }

    private Task<IReadOnlyList<ItemTypeVo>> GetKongyingItemTypeListCachedAsync(CancellationToken ct)
    {
        const string cacheKey = "kongying-tavern:item-types:area-filter-v1";
        return _cache.GetOrAddAsync<IReadOnlyList<ItemTypeVo>>(
                cacheKey,
                async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = CacheDuration;
                    var list = await _kongyingTavernApi.GetItemTypeListAsync(CancellationToken.None);
                    return list
                        .Where(x => x.AreaId == null || !KongyingTavernApiService.MaskMapItemTypeExcludedAreaIds.Contains(x.AreaId.Value))
                        .ToList();
                })
            .WaitAsync(ct);
    }

    private Task<IReadOnlyList<MarkerVo>> GetKongyingMarkerListCachedAsync(CancellationToken ct)
    {
        return _cache.GetOrAddAsync(
                "kongying-tavern:markers",
                async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = CacheDuration;
                    var list = await _kongyingTavernApi.GetMarkerListAsync(CancellationToken.None);
                    return list;
                })
            .WaitAsync(ct);
    }

    private Task<IReadOnlyDictionary<long, MarkerVo>> GetKongyingMarkerByIdCachedAsync(CancellationToken ct)
    {
        return _cache.GetOrAddAsync<IReadOnlyDictionary<long, MarkerVo>>(
                "kongying-tavern:markers-by-id",
                async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = CacheDuration;
                    var markers = await GetKongyingMarkerListCachedAsync(CancellationToken.None);

                    return markers
                        .Where(x => x.Id != null)
                        .GroupBy(x => x.Id!.Value)
                        .ToDictionary(g => g.Key, g => g.First());
                })
            .WaitAsync(ct);
    }

    private Task<IReadOnlyDictionary<long, IReadOnlyList<MarkerVo>>> GetKongyingMarkersByItemIdCachedAsync(CancellationToken ct)
    {
        return _cache.GetOrAddAsync<IReadOnlyDictionary<long, IReadOnlyList<MarkerVo>>>(
                "kongying-tavern:markers-by-item-id",
                async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = CacheDuration;
                    var apiStart = Stopwatch.GetTimestamp();
                    var markers = await GetKongyingMarkerListCachedAsync(CancellationToken.None);
                    var apiElapsed = Stopwatch.GetElapsedTime(apiStart);

                    var responseStart = Stopwatch.GetTimestamp();
                    var markerListByItemId = new Dictionary<long, List<MarkerVo>>(capacity: 4096);

                    foreach (var marker in markers)
                    {
                        if (marker.Id == null || string.IsNullOrWhiteSpace(marker.Position) || marker.ItemList == null || marker.ItemList.Count == 0)
                        {
                            continue;
                        }

                        var seen = new HashSet<long>();
                        foreach (var markerItem in marker.ItemList)
                        {
                            if (markerItem.ItemId == null)
                            {
                                continue;
                            }

                            if (!seen.Add(markerItem.ItemId.Value))
                            {
                                continue;
                            }

                            if (!markerListByItemId.TryGetValue(markerItem.ItemId.Value, out var list))
                            {
                                list = new List<MarkerVo>();
                                markerListByItemId[markerItem.ItemId.Value] = list;
                            }

                            list.Add(marker);
                        }
                    }

                    var result = markerListByItemId.ToDictionary(
                        x => x.Key,
                        x => (IReadOnlyList<MarkerVo>)x.Value);
                    var responseElapsed = Stopwatch.GetElapsedTime(responseStart);

                    // _logger.LogInformation(
                    //     "空荧酒馆 markers-by-item-id: API {ApiMs}ms, 响应处理 {ResponseMs}ms, markers {MarkerCount}, itemIds {ItemIdCount}",
                    //     apiElapsed.TotalMilliseconds,
                    //     responseElapsed.TotalMilliseconds,
                    //     markers.Count,
                    //     result.Count);

                    return result;
                })
            .WaitAsync(ct);
    }

    private Task<Dictionary<long, string>> GetKongyingIconUrlByIdCachedAsync(CancellationToken ct)
    {
        return _cache.GetOrAddAsync(
                "kongying-tavern:icons-by-id",
                async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = CacheDuration;
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

        var parts = position
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        return double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y);
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
