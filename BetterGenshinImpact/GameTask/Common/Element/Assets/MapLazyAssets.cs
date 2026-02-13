using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoTrackPath.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.Model;
using Newtonsoft.Json;

namespace BetterGenshinImpact.GameTask.Common.Element.Assets;

public class MapLazyAssets : Singleton<MapLazyAssets>
{
    // 不同场景的传送点信息
    public readonly Dictionary<string, GiWorldScene> ScenesDic;

    // 每个地区点击后处于的中心位置
    public readonly Dictionary<string, double[]> CountryPositions = new()
    {
        { "蒙德", [-876, 2278] },
        { "璃月", [270, -666] },
        { "稻妻", [-4400, -3050] },
        { "须弥", [2877, -374] },
        { "枫丹", [4515, 3631] },
        { "纳塔", [8973.5, -1879.1] },
        { "挪德卡莱", [9542.25, 1661.84] },
    };

    public readonly Dictionary<string, GiTpPosition> DomainPositionMap = new();
    public readonly Dictionary<string, GiTpPosition> GoddessPositions = new();

    public readonly List<String> DomainNameList = [];
    public readonly Dictionary<string, List<GiTpPosition>> CountryToDomains = new();

    public MapLazyAssets()
    {
        var json = File.ReadAllText(Global.Absolute(@"GameTask\AutoTrackPath\Assets\tp.json"));
        var worldScenes = Newtonsoft.Json.Linq.JObject.Parse(json)["data"]?.ToObject<List<GiWorldScene>>() ?? throw new Exception("tp.json deserialization failed");
        ScenesDic = worldScenes.ToDictionary(x => x.MapName, x => x);


        // 取出秘境 description=Domain
        var teyvatTpPositions = ScenesDic[nameof(MapTypes.Teyvat)].Points;
        foreach (var tp in teyvatTpPositions.Where(tp => tp.Type == "BlessDomain" || tp.Type == "ForgeryDomain" || tp.Type == "MasteryDomain"))
        {
            DomainPositionMap[tp.Name!] = tp;
            DomainNameList.Add(tp.Name!);

            if (!string.IsNullOrEmpty(tp.Country))
            {
                if (!CountryToDomains.ContainsKey(tp.Country))
                {
                    CountryToDomains[tp.Country] = [];
                }

                CountryToDomains[tp.Country].Add(tp);
            }
        }

        foreach (var tp in teyvatTpPositions.Where(tp => (tp.Type == "Goddess")))
        {
            GoddessPositions[tp.Id] = tp;
        }
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