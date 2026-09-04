using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.View;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.Common;

namespace BetterGenshinImpact.GameTask.TravelLog;

/// <summary>
/// 移动轨迹记录触发器
/// 在主界面下周期性识别小地图位置，记录移动轨迹并统计里程，
/// 同时把轨迹推送到遮罩窗口的小地图画布上展示
/// </summary>
public class TravelLogTrigger : ITaskTrigger
{
    public string Name => "TravelLog";

    public bool IsEnabled
    {
        get => TaskContext.Instance().Config.TravelLogConfig.Enabled;
        set => TaskContext.Instance().Config.TravelLogConfig.Enabled = value;
    }

    public int Priority => 5;

    public bool IsExclusive => false;

    /// <summary>
    /// 主界面在触发器体系中归类为 Unknown
    /// </summary>
    public GameUiCategory SupportedGameUiCategory => GameUiCategory.Unknown;

    /// <summary>
    /// 位置识别间隔
    /// </summary>
    private static readonly TimeSpan MatchInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 推送到遮罩画布的轨迹点上限（约 100 分钟路程，大地图概览使用）
    /// </summary>
    private const int MaskTrajectoryMaxPoints = 6000;

    private readonly ILogger _logger = TaskControl.Logger;

    private DateTime _lastMatchTime = DateTime.MinValue;

    /// <summary>
    /// 后台匹配任务忙标志（0 空闲 / 1 忙）
    /// </summary>
    private int _matching;

    // 上一次匹配到的位置（2048 级图像坐标），用于局部加速匹配
    private float _prevX = -1;
    private float _prevY = -1;

    /// <summary>
    /// 最近一次推送到遮罩的轨迹版本号
    /// </summary>
    private int _lastPushedVersion = -1;

    /// <summary>
    /// 遮罩画布上的轨迹当前是否由本触发器驱动显示
    /// </summary>
    private bool _maskTrajectoryActive;

    public void Init()
    {
        _lastMatchTime = DateTime.MinValue;
        _prevX = -1;
        _prevY = -1;
        ClearMaskTrajectory();
    }

    public void OnCapture(CaptureContent content)
    {
        if (!IsEnabled)
        {
            ClearMaskTrajectory();
            return;
        }

        if (!Bv.IsInMainUi(content.CaptureRectArea))
        {
            SuspendMinimapOverlay();
            return;
        }

        var now = DateTime.Now;
        if (now - _lastMatchTime < MatchInterval)
        {
            return;
        }

        // 上一次匹配还没结束则跳过
        if (Interlocked.CompareExchange(ref _matching, 1, 0) != 0)
        {
            return;
        }

        _lastMatchTime = now;

        // 截图内容在 OnCapture 返回后会被释放，克隆一份给后台任务用
        var region = new ImageRegion(
            content.CaptureRectArea.SrcMat.Clone(),
            content.CaptureRectArea.X,
            content.CaptureRectArea.Y);

        Task.Run(() =>
        {
            try
            {
                MatchPosition(region);
            }
            catch (Exception e)
            {
                _logger.LogDebug(e, "[TravelLog] 位置识别失败");
            }
            finally
            {
                region.Dispose();
                Interlocked.Exchange(ref _matching, 0);
            }
        });
    }

    /// <summary>
    /// 识别当前位置并记录轨迹点
    /// </summary>
    private void MatchPosition(ImageRegion region)
    {
        var matchingMethod = TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod;
        var sceneMap = MapManager.GetMap(MapTypes.Teyvat, matchingMethod);

        using var miniMapMat = new Mat(region.SrcMat, MapAssets.Get(region).MimiMapRect);

        // 先基于上一位置局部匹配，失败或跳变过大时重置并全图匹配
        var p = sceneMap.GetMiniMapPosition(miniMapMat, _prevX, _prevY);
        if (p == default || (_prevX > 0 && _prevY > 0 && p.DistanceTo(new Point2f(_prevX, _prevY)) > 150))
        {
            _prevX = -1;
            _prevY = -1;
            p = sceneMap.GetMiniMapPosition(miniMapMat, _prevX, _prevY);
        }

        if (p == default)
        {
            return;
        }

        _prevX = p.X;
        _prevY = p.Y;

        // 图像坐标（2048 级）→ 游戏坐标（1024 级，1 单位 ≈ 1 米）
        var gamePoint = sceneMap.ConvertImageCoordinatesToGenshinMapCoordinates(p);
        if (gamePoint is Point2f gp)
        {
            TravelLogService.Instance.AddPoint(gp);
        }

        PushTrajectoryToMask(sceneMap, p);
    }

    /// <summary>
    /// 把今日轨迹（转为 2048 级图像坐标）推送到遮罩的小地图与大地图画布
    /// </summary>
    private void PushTrajectoryToMask(ISceneMap sceneMap, Point2f imagePos)
    {
        if (MaskWindow.InstanceNullable() == null)
        {
            return;
        }

        var version = TravelLogService.Instance.TodayVersion;

        List<List<System.Windows.Point>>? segments = null;
        if (version != _lastPushedVersion)
        {
            var (points, segmentStarts) = TravelLogService.Instance.GetRecentTrajectory(MaskTrajectoryMaxPoints);

            segments = [];
            for (var i = 0; i < segmentStarts.Count; i++)
            {
                var start = segmentStarts[i];
                var end = i + 1 < segmentStarts.Count ? segmentStarts[i + 1] : points.Count;
                if (end - start < 1)
                {
                    continue;
                }

                var segment = new List<System.Windows.Point>(end - start);
                for (var j = start; j < end; j++)
                {
                    var imgPoint = sceneMap.ConvertGenshinMapCoordinatesToImageCoordinates(new Point2f(points[j].X, points[j].Y));
                    segment.Add(new System.Windows.Point(imgPoint.X, imgPoint.Y));
                }
                segments.Add(segment);
            }

            _lastPushedVersion = version;
        }

        // 地图遮罩的小地图功能开启时，小地图视口由 MapMaskTrigger 驱动，这里不重复设置；
        // 大地图视口始终由 MapMaskTrigger 驱动（需要开启地图遮罩才能显示大地图轨迹）
        var miniMapMaskEnabled = TaskContext.Instance().Config.MapMaskConfig.MiniMapMaskEnabled;
        System.Windows.Rect? viewport = null;
        if (!miniMapMaskEnabled)
        {
            double viewportSize = MapAssets.MimiMapRect1080P.Width / 3.0 * 10;
            viewport = new System.Windows.Rect(
                imagePos.X - viewportSize / 2.0,
                imagePos.Y - viewportSize / 2.0,
                viewportSize,
                viewportSize);
        }

        if (segments == null && viewport == null)
        {
            return;
        }

        _maskTrajectoryActive = true;
        UIDispatcherHelper.BeginInvoke(() =>
        {
            var window = MaskWindow.InstanceNullable();
            if (window == null)
            {
                return;
            }

            if (segments != null)
            {
                window.MiniMapPointsCanvasControl.UpdateTrajectory(segments);
                window.PointsCanvasControl.UpdateTrajectory(segments);
            }

            if (viewport is { } v)
            {
                window.MiniMapPointsCanvasControl.UpdateViewport(v.X, v.Y, v.Width, v.Height);
            }
        });
    }

    /// <summary>
    /// 离开主界面（打开大地图/菜单等）时，仅隐藏小地图圆形覆盖层。
    /// 大地图画布上的轨迹保留，供大地图遮罩继续展示。
    /// </summary>
    private void SuspendMinimapOverlay()
    {
        if (!_maskTrajectoryActive || MaskWindow.InstanceNullable() == null)
        {
            return;
        }

        _maskTrajectoryActive = false;

        var miniMapMaskEnabled = TaskContext.Instance().Config.MapMaskConfig.MiniMapMaskEnabled;
        if (miniMapMaskEnabled)
        {
            // 小地图视口由 MapMaskTrigger 管理，它会自行隐藏，无需干预
            return;
        }

        UIDispatcherHelper.BeginInvoke(() =>
        {
            var window = MaskWindow.InstanceNullable();
            if (window == null)
            {
                return;
            }

            window.MiniMapPointsCanvasControl.UpdateViewport(0, 0, 0, 0);
        });
    }

    /// <summary>
    /// 功能关闭时，清空两个遮罩画布上的轨迹与本触发器驱动的小地图视口
    /// </summary>
    private void ClearMaskTrajectory()
    {
        if (_lastPushedVersion < 0 && !_maskTrajectoryActive)
        {
            return; // 从未推送过，无需清理
        }

        _maskTrajectoryActive = false;
        _lastPushedVersion = -1;

        if (MaskWindow.InstanceNullable() == null)
        {
            return;
        }

        var miniMapMaskEnabled = TaskContext.Instance().Config.MapMaskConfig.MiniMapMaskEnabled;
        UIDispatcherHelper.BeginInvoke(() =>
        {
            var window = MaskWindow.InstanceNullable();
            if (window == null)
            {
                return;
            }

            window.MiniMapPointsCanvasControl.UpdateTrajectory(null);
            window.PointsCanvasControl.UpdateTrajectory(null);
            if (!miniMapMaskEnabled)
            {
                window.MiniMapPointsCanvasControl.UpdateViewport(0, 0, 0, 0);
            }
        });
    }
}
