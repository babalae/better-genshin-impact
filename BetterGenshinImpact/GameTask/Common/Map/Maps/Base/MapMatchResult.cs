using System;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

/// <summary>大小地图共用的定位结果；只依据地表背景定位时楼层为 null，避免把地下误报为地表。</summary>
/// <param name="Viewport2048">场景原生图像坐标下的完整视口；小地图仅定位时为默认值。</param>
/// <param name="GenshinPosition">视口中心的游戏坐标。</param>
/// <param name="Floor">素材中的楼层编号；未知时为 null。</param>
/// <param name="LayerName">命中的分层名称。</param>
/// <param name="Confidence">几何内点比例或模板归一化得分；不提供得分的匹配路径为 0。</param>
/// <param name="Via">命中路径。</param>
public sealed record MapMatchResult(Rect Viewport2048, Point2f GenshinPosition, int? Floor,
    string? LayerName, double Confidence, string Via)
{
    /// <summary>匹配中心的场景原生图像坐标，保留亚像素精度。</summary>
    public Point2f ImagePosition { get; init; } = new(Viewport2048.X + Viewport2048.Width / 2f,
        Viewport2048.Y + Viewport2048.Height / 2f);

    /// <summary>每游戏单位对应的层图像素数；不使用层图标定的路径为 null。</summary>
    public float? LayerScale { get; init; }

    /// <summary>校验坐标、单轴尺寸和地图交集，允许视口在地图边缘露出界外空白。</summary>
    internal static bool IsViewportValid(SceneBaseMap map, Rect2d viewport, double scale)
    {
        return double.IsFinite(viewport.X) && double.IsFinite(viewport.Y) &&
               double.IsFinite(viewport.Width) && double.IsFinite(viewport.Height) &&
               viewport.Width * scale >= 80 && viewport.Height * scale >= 60 &&
               viewport.Width * scale <= map.MapSize.Width * (map.Type == MapTypes.Teyvat ? 1 : 3) &&
               viewport.Height * scale <= map.MapSize.Height * (map.Type == MapTypes.Teyvat ? 1 : 3) &&
               viewport.Right * scale > 0 && viewport.Bottom * scale > 0 &&
               viewport.X * scale < map.MapSize.Width && viewport.Y * scale < map.MapSize.Height;
    }

    /// <summary>最终一步取整，避免多个尺度之间累积像素误差。</summary>
    internal static Rect RoundViewport(Rect2d rect) => new(
        (int)Math.Round(rect.X), (int)Math.Round(rect.Y),
        (int)Math.Round(rect.Width), (int)Math.Round(rect.Height));

    /// <summary>将匹配视口和经过验证的置信度转换为显示结果。</summary>
    internal static MapMatchResult? FromViewport(SceneBaseMap map, Rect2d viewport, int? floor,
        string? name, double confidence, string via, float? layerScale = null)
    {
        if (!IsViewportValid(map, viewport, 1)) return null;
        var center = new Point2f((float)(viewport.X + viewport.Width / 2), (float)(viewport.Y + viewport.Height / 2));
        var position = map.ConvertImageCoordinatesToGenshinMapCoordinates(center);
        return position is { } p
            ? new MapMatchResult(RoundViewport(viewport), p, floor, name, confidence, via)
            {
                ImagePosition = center,
                LayerScale = layerScale
            }
            : null;
    }

    /// <summary>将只有中心位置的小地图匹配转换为公共结果，不虚构视口或匹配得分。</summary>
    internal static MapMatchResult? FromPosition(SceneBaseMap map, Point2f imagePosition, int? floor, string via)
    {
        if (imagePosition == default) return null;
        var position = map.ConvertImageCoordinatesToGenshinMapCoordinates(imagePosition);
        return position is { } p
            ? new MapMatchResult(default, p, floor, null, 0, via) { ImagePosition = imagePosition }
            : null;
    }
}
