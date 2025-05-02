using System.Collections.Generic;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps;

public static class MapManager
{
    private static readonly Dictionary<MapTypes, IndependentBaseMap> _maps = new();
    private static readonly object LockObject = new();

    public static IndependentBaseMap GetMap(string mapName)
    {
        return GetMap(MapTypesExtensions.ParseFromName(mapName));
    }


    /// <summary>
    /// 获取指定类型的地图实例
    /// </summary>
    /// <param name="mapType">地图类型</param>
    /// <returns>地图实例</returns>
    public static IndependentBaseMap GetMap(MapTypes mapType)
    {
        if (_maps.TryGetValue(mapType, out var map))
        {
            return map;
        }

        lock (LockObject)
        {
            // 双重检查锁定
            if (_maps.TryGetValue(mapType, out map))
            {
                return map;
            }

            map = CreateMap(mapType);
            _maps[mapType] = map;
            return map;
        }
    }

    private static IndependentBaseMap CreateMap(MapTypes mapType)
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

    /// <summary>
    /// 获取所有已初始化的地图实例
    /// </summary>
    /// <returns>所有地图实例的集合</returns>
    public static IReadOnlyDictionary<MapTypes, IndependentBaseMap> GetAllMaps()
    {
        return _maps;
    }
}