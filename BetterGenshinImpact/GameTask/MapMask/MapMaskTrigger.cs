using System;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Layer;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.View;
using BetterGenshinImpact.ViewModel;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Rect = System.Windows.Rect;

namespace BetterGenshinImpact.GameTask.MapMask;

/// <summary>
/// 地图遮罩触发器
/// </summary>
public class MapMaskTrigger : ITaskTrigger
{
    private readonly ILogger<MapMaskTrigger> _logger = App.GetLogger<MapMaskTrigger>();

    public string Name => "地图遮罩";
    public bool IsEnabled { get; set; }
    public int Priority => 1; // 低优先级
    public bool IsExclusive => false;

    public GameUiCategory SupportedGameUiCategory => GameUiCategory.Unknown;

    private readonly MapMaskConfig _config = TaskContext.Instance().Config.MapMaskConfig;
    private readonly string _mapMatchingMethod = TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod;

    private readonly TemplateMatchStabilityDetector _detector = new();

    private DateTime _prevExecute = DateTime.MinValue;

    // 图像连续稳定次数
    private int _stableCount = 0;

    private ISceneMap _teyvatMap => MapManager.GetMap(MapTypes.Teyvat, _mapMatchingMethod);
    private OpenCvSharp.Rect _prevRect = default;

    private const int RectDebounceThreshold = 3;

    private readonly NavigationInstance _navigationInstance = new();

    public void Init()
    {
        IsEnabled = _config.Enabled;

        // 关闭时隐藏UI
        if (!IsEnabled)
        {
            UIDispatcherHelper.Invoke(() =>
            {
                if (MaskWindow.InstanceNullable() != null)
                {
                    if (MaskWindow.Instance().DataContext is MaskWindowViewModel vm)
                    {
                        vm.IsInBigMapUi = false;
                    }
                }
            });
        }
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
            var inBigMapUi = content.CurrentGameUiCategory == GameUiCategory.BigMap || Bv.IsInBigMapUi(region);
            UIDispatcherHelper.Invoke(() =>
            {
                if (MaskWindow.Instance().DataContext is MaskWindowViewModel vm)
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
                    var rect256 = BigMapTeyvat256Layer.GetInstance((SceneBaseMap)_teyvatMap).GetBigMapRect(region.CacheGreyMat, _prevRect);
                    if (rect256 != default)
                    {
                        // 过大或过小的区域不处理
                        if (rect256 is { Width: < 50, Height: < 40 } || rect256 is { Width: > 3000, Height: > 1800 })
                        {
                            _prevRect = default;
                            return;
                        }


                        // if (_prevRect != default)
                        // {
                        //     var dx = Math.Abs(rect256.X - _prevRect.X);
                        //     var dy = Math.Abs(rect256.Y - _prevRect.Y);
                        //     if (dx <= RectDebounceThreshold && dy <= RectDebounceThreshold)
                        //     {
                        //         return;
                        //     }
                        // }

                        _prevRect = rect256;
                    }

                    const int s = TeyvatMap.BigMap256ScaleTo2048; // 相对2048做8倍缩放
                    var rect2048 = new Rect(rect256.X * s, rect256.Y * s, rect256.Width * s, rect256.Height * s);
                    UIDispatcherHelper.Invoke(() => { MaskWindow.Instance().PointsCanvasControl.UpdateViewport(rect2048.X, rect2048.Y, rect2048.Width, rect2048.Height); });
                }
            }
            else
            {
                if ((_config.MiniMapMaskEnabled || _config.PathAutoRecordEnabled) && Bv.IsInMainUi(region))
                {
                    // 主界面上展示小地图
                    if (_config.MiniMapMaskEnabled)
                    {
                        var miniPoint = _navigationInstance.GetPositionStable(region, nameof(MapTypes.Teyvat), TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod);
                        if (miniPoint != default)
                        {
                            // 展示窗口是 212 
                            double viewportSize = MapAssets.MimiMapRect1080P.Width / 3.0 * 10;
                            UIDispatcherHelper.Invoke(() =>
                            {
                                MaskWindow.Instance().MiniMapPointsCanvasControl.UpdateViewport(
                                    miniPoint.X - viewportSize / 2.0,
                                    miniPoint.Y - viewportSize / 2.0,
                                    viewportSize,
                                    viewportSize);
                            });
                        }
                        else
                        {
                            UIDispatcherHelper.Invoke(() => { MaskWindow.Instance().MiniMapPointsCanvasControl.UpdateViewport(0, 0, 0, 0); });
                        }

                        // 自动记录路径
                        if (_config.PathAutoRecordEnabled)
                        {
                            // ...
                        }
                    }
                }

                _prevRect = default;
            }
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "实时地图定位时发生异常");
        }
    }
}