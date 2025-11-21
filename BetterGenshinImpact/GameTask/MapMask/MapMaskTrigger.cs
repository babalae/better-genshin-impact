using System;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.View;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.MapMask;

/// <summary>
/// 自动吃药触发器
/// 检测红血状态时自动使用Recovery.png，检测到Resurrection.png时按z复活
/// </summary>
public class MapMaskTrigger : ITaskTrigger
{
    private readonly ILogger<MapMaskTrigger> _logger = App.GetLogger<MapMaskTrigger>();

    public string Name => "地图遮罩";
    public bool IsEnabled { get; set; }
    public int Priority => 25; // 中等优先级
    public bool IsExclusive => false;

    private readonly MapMaskConfig _config = TaskContext.Instance().Config.MapMaskConfig;
    private readonly string _mapMatchingMethod = TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod;

    private DateTime _prevExecute = DateTime.MinValue;

    public void Init()
    {
        IsEnabled = true;
    }

    public void OnCapture(CaptureContent content)
    {
        if ((DateTime.Now - _prevExecute).TotalMilliseconds <= 50)
        {
            return;
        }

        _prevExecute = DateTime.Now;

        try
        {
            var region = content.CaptureRectArea;
            if (Bv.IsInBigMapUi(region))
            {
                var rect = MapManager.GetMap(MapTypes.Teyvat, _mapMatchingMethod).GetBigMapRect(region.CacheGreyMat);
                UIDispatcherHelper.Invoke(() => { MaskWindow.Instance().PointsCanvasControl.UpdateViewport(rect.X, rect.Y, rect.Width, rect.Height); });
            }
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "实时地图定位时发生异常");
        }
    }
}