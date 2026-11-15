using System;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

namespace BetterGenshinImpact.Model.MaskMap;

/// <summary>将各地图服务的网页坐标转换为游戏场景坐标。</summary>
public static class GameWebMapCoordinateConverter
{
    /// <summary>官方提瓦特底图的游戏坐标原点 X。</summary>
    private const double MysWebOffsetOriginX = -749.75;
    /// <summary>官方提瓦特底图的游戏坐标原点 Y。</summary>
    private const double MysWebOffsetOriginY = 2322.0;

    /// <summary>空荧酒馆提瓦特底图的游戏坐标原点 X。</summary>
    private const double KongyingTavernOffsetOriginX = 396.125;
    /// <summary>空荧酒馆提瓦特底图的游戏坐标原点 Y。</summary>
    private const double KongyingTavernOffsetOriginY = -619.9375;

    /// <summary>官方点位请求使用的独立地图 ID，不能沿用提瓦特的 map_id=2。</summary>
    public static int GetMysMapId(MapTypes mapType) => mapType switch
    {
        MapTypes.Teyvat => 2,
        MapTypes.Enkanomiya => 7,
        MapTypes.TheChasm => 9,
        MapTypes.SeaOfBygoneEras => 34,
        MapTypes.AncientSacredMountain => 36,
        MapTypes.TempleOfSpace => 37,
        MapTypes.MoonCanon => 40,
        _ => throw new ArgumentOutOfRangeException(nameof(mapType))
    };

    /// <summary>网页场景坐标转换为游戏坐标；霜月与空之神殿的网页底图还经过缩放。</summary>
    public static (double x, double y) MysWebToGame(double webX, double webY, MapTypes mapType)
    {
        // 原点由官方底图的 origin、padding 与场景原点对齐；非等尺寸底图通过离线传送点校准。
        // 校准回归保留网页锚点和游戏坐标，允许官方手工标点及传送落点的少量误差。
        var (scale, originX, originY) = mapType switch
        {
            MapTypes.Teyvat => (1d, MysWebOffsetOriginX, MysWebOffsetOriginY),
            MapTypes.Enkanomiya => (1d, 199d, 269d),
            MapTypes.TheChasm => (1d, 544d, 604d),
            MapTypes.SeaOfBygoneEras => (1d, 4000d, 1444d),
            MapTypes.AncientSacredMountain => (1d, -152d, 225d),
            MapTypes.TempleOfSpace => (1.0530497881788916, 416.0115890349508, 166.1548633661736),
            MapTypes.MoonCanon => (1.741741507663424, 6920.808622380749, -614.6393319645038),
            _ => throw new ArgumentOutOfRangeException(nameof(mapType))
        };
        return (originX - webX * scale, originY - webY * scale);
    }

    /// <summary>酒馆独立地图使用各自的缩放和原点，不能使用大陆的 3/4 比例。</summary>
    public static (double x, double y) KongyingTavernToGame(double webX, double webY, MapTypes mapType)
    {
        // 与每张独立地图的离线传送点对齐；测试保存未参与运行计算的真实锚点对。
        var (scale, originX, originY) = mapType switch
        {
            MapTypes.Teyvat => (0.75, KongyingTavernOffsetOriginX, KongyingTavernOffsetOriginY),
            MapTypes.Enkanomiya => (0.2501125512676512, 1157.0233057177866, 478.0039412719874),
            MapTypes.TheChasm => (0.24951717820242997, 1154.7738346642138, 478.99747275155454),
            MapTypes.SeaOfBygoneEras => (0.5005734234687407, 3340.453045121157, -654.8003010328387),
            MapTypes.AncientSacredMountain => (0.499865659233012, 264.3317658936171, -1091.5541608319825),
            MapTypes.TempleOfSpace => (0.5066583715667548, 64.69364463479906, -1530.54049042313),
            MapTypes.MoonCanon => (1.0000515743683527, 7696.919683398945, -2187.8056275949953),
            _ => throw new ArgumentOutOfRangeException(nameof(mapType))
        };
        return (originX - webX * scale, originY - webY * scale);
    }
}
