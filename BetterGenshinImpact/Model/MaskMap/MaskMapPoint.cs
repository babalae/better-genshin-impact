using System.Collections.Generic;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

namespace BetterGenshinImpact.Model.MaskMap;

public class MaskMapPoint
{
    public string Id { get; set; } = string.Empty;

    /// <summary>点位所属场景，详情请求与图像投影必须使用同一地图。</summary>
    public MapTypes MapType { get; set; } = MapTypes.Teyvat;
    public double X { get; set; }
    public double Y { get; set; }
    
    /// <summary>
    /// 游戏中的坐标 X
    /// </summary>
    public double GameX { get; set; }
    
    /// <summary>
    /// 游戏中的坐标 Y
    /// </summary>
    public double GameY { get; set; }
    
    /// <summary>
    /// 2048级别游戏图像地图的坐标 X
    /// </summary>
    public double ImageX { get; set; }
    
    /// <summary>
    /// 2048级别游戏图像地图的坐标 Y
    /// </summary>
    public double ImageY { get; set; }
    
    public string LabelId { get; set; } = string.Empty;

    public List<MaskMapLink> VideoUrls { get; set; } = new();

    public bool IsHidden { get; set; }

    public bool Contains(double px, double py)
    {
        return px >= X - MaskMapPointStatic.Width / 2 && px <= X + MaskMapPointStatic.Width / 2 &&
               py >= Y - MaskMapPointStatic.Height / 2 && py <= Y + MaskMapPointStatic.Height / 2;
    }
}

public class MaskMapPointStatic
{
    public static readonly int Width = 32;

    public static readonly int Height = 32;
}
