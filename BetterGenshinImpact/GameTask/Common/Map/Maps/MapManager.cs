using System.Collections.Generic;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps;

public static class MapManager
{
    private static readonly Dictionary<string, ISceneMap> _maps = new();
    private static readonly object LockObject = new();

    public static ISceneMap GetMap(string mapName, string matchingMethod)
    {
        return GetMap(MapTypesExtensions.ParseFromName(mapName), matchingMethod);
    }


    /// <summary>
    /// 获取指定类型的地图实例
    /// </summary>
    /// <param name="mapType">地图类型</param>
    /// <param name="matchingMethod">地图匹配方式</param>
    /// <returns>地图实例</returns>
    public static ISceneMap GetMap(MapTypes mapType, string matchingMethod)
    {
        if (string.IsNullOrEmpty(matchingMethod))
        {
            matchingMethod = TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod;
        }
        string key = $"{mapType}_{matchingMethod}";

        if (_maps.TryGetValue(key, out var map))
        {
            return map;
        }

        lock (LockObject)
        {
            // 双重检查锁定
            if (_maps.TryGetValue(key, out map))
            {
                return map;
            }

            map = CreateMap(mapType, matchingMethod);
            _maps[key] = map;
            return map;
        }
    }

    private static ISceneMap CreateMap(MapTypes mapType, string matchingMethod)
    {
        if (matchingMethod == "SIFT")
        {
            return mapType switch
            {
                MapTypes.Teyvat => new TeyvatMap(),
                MapTypes.TheChasm => new TheChasmMap(),
                MapTypes.Enkanomiya => new EnkanomiyaMap(),
                MapTypes.AncientSacredMountain => new AncientSacredMountainMap(),
                MapTypes.SeaOfBygoneEras => new SeaOfBygoneErasMap(),
                _ => throw new System.ArgumentException($"未知的地图类型: {mapType}", nameof(mapType))
            };
        }
        else
        {
            return mapType switch
            {
                MapTypes.Teyvat => new TeyvatMapTest(),
                MapTypes.TheChasm => new TheChasmMapTest(),
                MapTypes.Enkanomiya => new EnkanomiyaMapTest(),
                MapTypes.AncientSacredMountain => new AncientSacredMountainMap(),
                MapTypes.SeaOfBygoneEras => new SeaOfBygoneErasMap(),
                _ => throw new System.ArgumentException($"未知的地图类型: {mapType}", nameof(mapType))
            };
        }
    }
}