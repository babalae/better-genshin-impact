using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.Model.Area;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoPathing;

public class NavigationInstance
{
    private float _prevX = -1;
    private float _prevY = -1;
    public void Reset()
    {
        (_prevX, _prevY) = (-1, -1);
    }
    
    public void SetPrevPosition(float x, float y)
    {
        (_prevX, _prevY) = (x, y);
    }

    public Point2f GetPosition(ImageRegion imageRegion, string mapName)
    {
        var colorMat = new Mat(imageRegion.SrcMat, MapAssets.Instance.MimiMapRect);
        var p = MapManager.GetMap(mapName).GetMiniMapPosition(colorMat, _prevX, _prevY);
        if (p != default)
        {
            (_prevX, _prevY) = (p.X, p.Y);
        }
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(typeof(Navigation),
            "SendCurrentPosition", new object(), p));
        return p;
    }

    /// <summary>
    /// 稳定获取当前位置坐标，优先使用全地图匹配，适用于不需要高效率但需要高稳定性的场景
    /// </summary>
    /// <param name="imageRegion">图像区域</param>
    /// <param name="mapName"></param>
    /// <returns>当前位置坐标</returns>
    public Point2f GetPositionStable(ImageRegion imageRegion, string mapName)
    {
        var colorMat = new Mat(imageRegion.SrcMat, MapAssets.Instance.MimiMapRect);

        // 先尝试使用局部匹配
        var p =  MapManager.GetMap(mapName).GetMiniMapPosition(colorMat, _prevX, _prevY);

        // 如果局部匹配失败，再尝试全地图匹配失败
        if (p == new Point2f())
        {
            Reset();
            p = MapManager.GetMap(mapName).GetMiniMapPosition(colorMat, _prevX, _prevY);
        }
        if (p != default)
        {
            (_prevX, _prevY) = (p.X, p.Y);
        }

        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(typeof(Navigation),
            "SendCurrentPosition", new object(), p));
        return p;
    }
}