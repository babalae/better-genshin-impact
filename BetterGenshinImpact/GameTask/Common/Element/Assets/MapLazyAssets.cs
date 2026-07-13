using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoTrackPath.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using Newtonsoft.Json;

namespace BetterGenshinImpact.GameTask.Common.Element.Assets;

public sealed class MapLazyAssets
{
    private static readonly Lazy<MapLazyAssets> Cache = new(() => new MapLazyAssets(), true);
    private readonly Dictionary<string, GiTpPosition> _domainPositionMap = new();
    private readonly Dictionary<string, GiTpPosition> _goddessPositions = new();
    private readonly List<string> _domainNameList = [];
    private readonly Dictionary<string, List<GiTpPosition>> _countryToDomains = new();

    // 不同场景的传送点信息
    public IReadOnlyDictionary<string, GiWorldScene> ScenesDic { get; }

    // 每个地区点击后处于的中心位置
    public IReadOnlyDictionary<string, double[]> CountryPositions { get; } = new Dictionary<string, double[]>
    {
        { "蒙德", [-876, 2278] },
        { "璃月", [270, -666] },
        { "稻妻", [-4400, -3050] },
        { "须弥", [2877, -374] },
        { "枫丹", [4515, 3631] },
        { "纳塔", [8973.5, -1879.1] },
        { "挪德卡莱", [9542.25, 1661.84] },
    };

    public IReadOnlyDictionary<string, GiTpPosition> DomainPositionMap => _domainPositionMap;
    public IReadOnlyDictionary<string, GiTpPosition> GoddessPositions => _goddessPositions;

    public IReadOnlyList<string> DomainNameList => _domainNameList;
    public IReadOnlyDictionary<string, List<GiTpPosition>> CountryToDomains => _countryToDomains;

    private MapLazyAssets()
    {
        var json = File.ReadAllText(Global.Absolute(@"GameTask\AutoTrackPath\Assets\tp.json"));
        var worldScenes = Newtonsoft.Json.Linq.JObject.Parse(json)["data"]?.ToObject<List<GiWorldScene>>() ?? throw new Exception("tp.json deserialization failed");
        ScenesDic = worldScenes.ToDictionary(x => x.MapName, x => x);


        // 取出秘境 description=Domain
        var teyvatTpPositions = ScenesDic[nameof(MapTypes.Teyvat)].Points;
        foreach (var tp in teyvatTpPositions.Where(tp => tp.Type == "BlessDomain" || tp.Type == "ForgeryDomain" || tp.Type == "MasteryDomain"))
        {
            _domainPositionMap[tp.Name!] = tp;
            _domainNameList.Add(tp.Name!);

            if (!string.IsNullOrEmpty(tp.Country))
            {
                if (!_countryToDomains.ContainsKey(tp.Country))
                {
                    _countryToDomains[tp.Country] = [];
                }

                _countryToDomains[tp.Country].Add(tp);
            }
        }

        foreach (var tp in teyvatTpPositions.Where(tp => (tp.Type == "Goddess")))
        {
            _goddessPositions[tp.Id] = tp;
        }
    }

    public static MapLazyAssets Get()
    {
        return Cache.Value;
    }

    public string? GetCountryByDomain(string domain)
    {
        if (string.IsNullOrEmpty(domain)) return null;
        if (DomainPositionMap.TryGetValue(domain, out var tp))
        {
            return tp.Country;
        }

        return null;
    }
}
