using System;
using System.Windows;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.View;
using BetterGenshinImpact.ViewModel;
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

    private readonly TemplateMatchStabilityDetector _detector = new();

    private DateTime _prevExecute = DateTime.MinValue;

    // 图像连续稳定次数
    private int _stableCount = 0;

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
            var inBigMapUi = Bv.IsInBigMapUi(region);
            UIDispatcherHelper.Invoke(() =>
            {
                var vm = MaskWindow.Instance().DataContext as MaskWindowViewModel;
                if (vm != null)
                {
                    vm.IsInBigMapUi = inBigMapUi;
                }
            });

            if (inBigMapUi)
            {
                if (_detector.IsStable(region.CacheGreyMat))
                {
                    _stableCount++;
                    if (_stableCount >= 20)
                    {
                        _stableCount = 0;
                    }
                }
                else
                {
                    _stableCount = 0;
                }

                if (_stableCount == 0)
                {
                    var rect256 = MapManager.GetMap(MapTypes.Teyvat, _mapMatchingMethod).GetBigMapRect(region.CacheGreyMat);
                    const int s = TeyvatMap.BigMap256ScaleTo2048; // 相对2048做8倍缩放
                    var rect2048 = new Rect(rect256.X * s, rect256.Y * s, rect256.Width * s, rect256.Height * s);
                    UIDispatcherHelper.Invoke(() => { MaskWindow.Instance().PointsCanvasControl.UpdateViewport(rect2048.X, rect2048.Y, rect2048.Width, rect2048.Height); });
                }
            }
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "实时地图定位时发生异常");
        }
    }
}
