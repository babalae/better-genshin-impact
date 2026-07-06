using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Script.Dependence;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.AutoTrackPath.Model;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Exceptions;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.QuickTeleport.Assets;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Extensions;
using Fischless.GameCapture;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

/// <summary>
/// 传送任务
/// </summary>
public class TpTask
{
    private readonly QuickTeleportAssets _assets = QuickTeleportAssets.Instance;
    private readonly Rect _captureRect = TaskContext.Instance().SystemInfo.ScaleMax1080PCaptureRect;
    private readonly double _zoomOutMax1080PRatio = TaskContext.Instance().SystemInfo.ZoomOutMax1080PRatio;
    private readonly TpConfig _tpConfig = TaskContext.Instance().Config.TpConfig;
    private readonly string _mapMatchingMethod = TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod;
    private readonly BlessingOfTheWelkinMoonTask _blessingOfTheWelkinMoonTask = new();

    private readonly CancellationToken ct;
    private readonly CultureInfo cultureInfo;
    private readonly IStringLocalizer stringLocalizer;

    private const double DisplayTpPointZoomLevel = 4.4; // 传送点显示的时候的地图比例
    private const double TeleportFinalZoomDistanceFactor = 20d;
    private const double MinTeleportZoomLevel = 1.0;
    private const int MaxMapZoomWheelAttempts = 3;
    private const int MaxMapZoomWheelBatchNotches = 16;
    private const int MapZoomWheelSignForZoomLevelIncrease = -1;
    private const int MapZoomWheelBurstIntervalMs = 15;
    private const double DefaultMapZoomLevelPerWheelNotch = 0.085;
    private const double MapDragPixelsPerStep = 24d;
    private const double MapDragFastStepRatio = 0.42d;
    private const double MapDragFastDistanceRatio = 0.85d;
    private const double MapClickSafeMargin = 35d;
    private const int MapLayerSwitchRetryCount = 5;
    private const int MapLayerSwitchRetryIntervalMs = 120;
    private const int MapGroundLayerSettlingDelayMs = 400;

    private double _mapZoomLevelPerWheelNotch = DefaultMapZoomLevelPerWheelNotch;
    private Stopwatch? _teleportMToFStopwatch;
    private string? _teleportMToFTarget;

    private sealed class MapChooseCandidate
    {
        public int Index { get; set; }
        public required string IconFileName { get; init; }
        public required string IconType { get; init; }
        public required string Text { get; init; }
        public required Rect IconRect { get; init; }
        public required Rect TextRect { get; init; }
        public required Rect ClickRect { get; init; }
        public int SelectedIndicatorScore { get; set; }
    }

    private sealed class NearbyMapIcon
    {
        public int GeometricIndex { get; set; }
        public required string IconFileName { get; init; }
        public required string IconType { get; init; }
        public required Rect Rect { get; init; }
        public required double CenterX { get; init; }
        public required double CenterY { get; init; }
        public required double DistanceToTarget { get; init; }
        public bool TypeMatchesTarget { get; set; }
        public bool IsTargetIcon { get; set; }
    }

    private sealed class TeleportTargetContext
    {
        public required string MapName { get; init; }
        public bool Force { get; init; }
        public double RequestX { get; init; }
        public double RequestY { get; init; }
        public required GiTpPosition TargetTp { get; init; }
        public required GiTpPosition NearTp { get; init; }
        public double X { get; init; }
        public double Y { get; init; }
        public string? Country { get; init; }
        public double DistanceToNear { get; init; }
        public double FinalClickZoomLevel { get; init; }
        public double TravelZoomLevel { get; init; }
    }

    private enum TeleportPanelResult
    {
        Waiting,
        Confirmed,
        RetryPoint
    }

    private readonly record struct MapMoveState(
        Point2f CenterPoint,
        double XOffset,
        double YOffset,
        double MouseDeltaX,
        double MouseDeltaY,
        double MouseDistance)
    {
        public MapMoveState ScaleMouseDelta(double scale)
        {
            return this with
            {
                MouseDeltaX = MouseDeltaX * scale,
                MouseDeltaY = MouseDeltaY * scale,
                MouseDistance = MouseDistance * scale
            };
        }
    }

    public TpTask(CancellationToken ct)
    {
        this.ct = ct;
        TpTaskParam param = new TpTaskParam();
        this.cultureInfo = param.GameCultureInfo;
        this.stringLocalizer = param.StringLocalizer;
    }

    /// <summary>
    /// 传送到七天神像
    /// </summary>
    public async Task TpToStatueOfTheSeven()
    {
        await CheckInBigMapUi();

        // 提前调整至恰当的缩放以更快的传送
        if (_tpConfig.MapZoomEnabled)
        {
            double currentZoomLevel = GetCurrentBigMapZoomLevel();
            if (currentZoomLevel > DisplayTpPointZoomLevel)
            {
                await AdjustMapZoomLevel(currentZoomLevel, DisplayTpPointZoomLevel);
            }
            else if (currentZoomLevel < 3)
            {
                await AdjustMapZoomLevel(currentZoomLevel, 3);
            }
        }

        string? country = _tpConfig.ReviveStatueOfTheSevenCountry;
        string? area = _tpConfig.ReviveStatueOfTheSevenArea;
        double x = _tpConfig.ReviveStatueOfTheSevenPointX;
        double y = _tpConfig.ReviveStatueOfTheSevenPointY;
        GiTpPosition revivePoint = _tpConfig.ReviveStatueOfTheSeven ?? GetNearestGoddess(x, y);
        if (_tpConfig.IsReviveInNearestStatueOfTheSeven)
        {
            var center = GetBigMapCenterPoint(MapTypes.Teyvat.ToString());
            var giTpPoint = GetNearestGoddess(center.X, center.Y);
            country = giTpPoint.Country;
            area = giTpPoint.Level1Area;
            x = giTpPoint.X;
            y = giTpPoint.Y;
            revivePoint = giTpPoint;
        }

        Logger.LogInformation("将传送至 {country} {area} 七天神像", country, area);
        await Tp(x, y, MapTypes.Teyvat.ToString(), false);
        if (_tpConfig.ShouldMove || _tpConfig.IsReviveInNearestStatueOfTheSeven)
        {
            (x, y) = GetClosestPoint(revivePoint.TranX, revivePoint.TranY, x, y, 5);
            var waypoint = new Waypoint
            {
                X = x,
                Y = y,
                Type = WaypointType.Path.Code,
                MoveMode = MoveModeEnum.Walk.Code
            };
            var waypointForTrack = new WaypointForTrack(waypoint, nameof(MapTypes.Teyvat), _mapMatchingMethod);
            await new PathExecutor(ct).MoveTo(waypointForTrack);
            Simulation.SendInput.SimulateAction(GIActions.Drop);
        }

        await Delay((int)(_tpConfig.HpRestoreDuration * 1000), ct);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="tranX"> 传送后实际到达的点X坐标 </param>
    /// <param name="tranY"> 传送后实际到达的点Y坐标 </param>
    /// <param name="x"> 传送点 X 坐标 </param>
    /// <param name="y"> 传送点 Y 坐标 </param>
    /// <param name="d"> 期望最终离传送点的距离 </param>
    /// <returns>  </returns>
    private static (double X, double Y) GetClosestPoint(double tranX, double tranY, double x, double y, double d)
    {
        double dx = x - tranX;
        double dy = y - tranY;
        double distanceSquared = dx * dx + dy * dy;
        double distance = Math.Sqrt(distanceSquared);
        d = d > 0 ? d : 0;
        if (distance < d)
        {
            return (tranX, tranY);
        }

        double ratio = d / distance;
        double px = (x - dx * ratio);
        double py = (y - dy * ratio);
        return (px, py);
    }

    /// <summary>
    /// 获取离 x,y 最近的七天神像
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <returns></returns>
    private GiTpPosition GetNearestGoddess(double x, double y)
    {
        GiTpPosition? nearestGiTpPosition = null;
        double minDistance = double.MaxValue;
        foreach (var (_, goddessPosition) in MapLazyAssets.Instance.GoddessPositions)
        {
            var distance = Math.Sqrt(Math.Pow(goddessPosition.X - x, 2) + Math.Pow(goddessPosition.Y - y, 2));
            if (distance < minDistance)
            {
                minDistance = distance;
                nearestGiTpPosition = goddessPosition;
            }
        }

        // 获取最近的神像位置
        return nearestGiTpPosition ?? throw new InvalidOperationException("没找到最近的七天神像");
    }

    /// <summary>
    ///释放所有按键，并打开大地图界面
    /// </summary>
    /// <param name="retryCount">重试次数</param>
    public async Task OpenBigMapUi(int retryCount = 3)
    {
        for (var i = 0; i < retryCount; i++)
        {
            try
            {
                // 打开地图前释放所有按键
                Simulation.ReleaseAllKey();
                await Delay(20, ct);
                await CheckInBigMapUi();
                return;
            }
            catch (Exception e) when (IsTaskStopException(e))
            {
                throw;
            }
            catch (Exception)
            {
                if (retryCount > 1)
                {
                    await _blessingOfTheWelkinMoonTask.Start(ct);
                }

                if (i + 1 >= retryCount)
                {
                    throw;
                }
            }
        }
    }

    /// <summary>
    /// 通过大地图传送到指定坐标最近的传送点，然后移动到指定坐标
    /// </summary>
    /// <param name="tpX"></param>
    /// <param name="tpY"></param>
    /// <param name="mapName">独立地图名称</param>
    /// <param name="force">强制以当前的tpX,tpY坐标进行自动传送</param>
    private async Task<(double, double)> TpOnce(double tpX, double tpY, string mapName = "Teyvat", bool force = false)
    {
        // 1. 确认在地图界面，并在传送入口统一切回地表图层
        await OpenBigMapUi(1);
        await SwitchToGroundMapLayerIfNeeded();

        var target = ResolveTeleportTarget(tpX, tpY, mapName, force);
        SetTeleportMToFTimingTarget(target);
        LogTeleportTarget(target);

        await SwitchToTeleportTargetMap(target);

        var zoomLevel = await PrepareTeleportZoom(target);
        var bigMapInAllMapRect = await EnsureTeleportTargetReadyForClick(target, zoomLevel);
        ClickTeleportTargetMapPoint(target, bigMapInAllMapRect);

        await ClickTpPointAfterMapPointSelected(
            target.TargetTp,
            target.MapName,
            bigMapInAllMapRect,
            target.X,
            target.Y);

        await WaitForTeleportCompletion(50, 1200);
        return (target.X, target.Y);
    }

    private TeleportTargetContext ResolveTeleportTarget(double tpX, double tpY, string mapName, bool force)
    {
        var nTpPoints = GetNearestNTpPoints(tpX, tpY, mapName, 2);
        var targetTp = nTpPoints[0];
        var nearTp = nTpPoints[1];
        var (x, y, country) = force ? (tpX, tpY, null) : (targetTp.X, targetTp.Y, targetTp.Country);
        var distanceToNear = Math.Sqrt(Math.Pow(targetTp.X - nearTp.X, 2) + Math.Pow(targetTp.Y - nearTp.Y, 2));

        return new TeleportTargetContext
        {
            MapName = mapName,
            Force = force,
            RequestX = tpX,
            RequestY = tpY,
            TargetTp = targetTp,
            NearTp = nearTp,
            X = x,
            Y = y,
            Country = country,
            DistanceToNear = distanceToNear,
            FinalClickZoomLevel = GetTeleportFinalClickZoomLevel(distanceToNear),
            TravelZoomLevel = GetTeleportTravelZoomLevel()
        };
    }

    private static void LogTeleportTarget(TeleportTargetContext target)
    {
        Logger.LogInformation(
            "开始传送：{Target}",
            GetTeleportTargetLogText(target));
    }

    private static string GetTeleportTargetLogText(TeleportTargetContext target)
    {
        var tp = target.TargetTp;
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(tp.Country))
        {
            parts.Add(tp.Country);
        }

        parts.AddRange(tp.Areas.Where(area => !string.IsNullOrWhiteSpace(area)));
        var location = parts.Count > 0
            ? string.Join(" / ", parts.Distinct())
            : GetMapDisplayName(target.MapName);
        var pointName = GetTeleportPointDisplayName(tp);
        var text = $"{location} - {pointName}";
        return text;
    }

    private static string GetTeleportPointDisplayName(GiTpPosition tp)
    {
        var typeName = GetTeleportTypeDisplayName(tp.Type);
        var name = tp.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, typeName, StringComparison.Ordinal))
        {
            return typeName;
        }

        return $"{typeName}「{name}」";
    }

    private static string GetTeleportTypeDisplayName(string? type)
    {
        return type switch
        {
            "TeleportWaypoint" => "传送锚点",
            "Goddess" => "七天神像",
            "OneTimeDomain" => "一次性秘境",
            "BlessDomain" => "祝圣秘境",
            "ForgeryDomain" => "炼武秘境",
            "MasteryDomain" => "精通秘境",
            "TrounceDomain" => "征讨领域",
            "NatlanObsidianTotemPole" => "曜石图腾柱",
            "NodKraiMeetingPoint" => "挪德卡莱集会所",
            "Other" => "特殊点位",
            null or "" => "传送点",
            _ => type
        };
    }

    private static string GetMapDisplayName(string mapName)
    {
        return MapTypesExtensions.ParseFromName(mapName).GetDescription();
    }

    private async Task SwitchToTeleportTargetMap(TeleportTargetContext target)
    {
        if (target.MapName == MapTypes.Teyvat.ToString())
        {
            await SwitchRecentlyCountryMap(target.X, target.Y, target.Country);
        }
        else
        {
            await SwitchArea(MapTypesExtensions.ParseFromName(target.MapName).GetDescription());
        }
    }

    private async Task<double> PrepareTeleportZoom(TeleportTargetContext target)
    {
        var zoomLevel = GetCurrentBigMapZoomLevel();
        if (_tpConfig.MapZoomEnabled)
        {
            if (zoomLevel > target.TravelZoomLevel + _tpConfig.PrecisionThreshold)
            {
                await AdjustMapZoomLevel(zoomLevel, target.TravelZoomLevel);
                zoomLevel = GetCurrentBigMapZoomLevel();
            }
        }

        return zoomLevel;
    }

    private async Task<Rect> EnsureTeleportTargetReadyForClick(TeleportTargetContext target, double currentZoomLevel)
    {
        if (_tpConfig.MapZoomEnabled && currentZoomLevel > target.FinalClickZoomLevel + _tpConfig.PrecisionThreshold)
        {
            await MoveMapTo(target.X, target.Y, target.MapName, target.FinalClickZoomLevel);
        }

        var bigMapInAllMapRect = GetBigMapRect(target.MapName);
        var retryCount = 0;
        do
        {
            if (IsPointInBigMapWindow(target.MapName, bigMapInAllMapRect, target.X, target.Y))
            {
                break;
            }

            if (retryCount++ >= 5) // 防止死循环
            {
                Logger.LogWarning("多次尝试未移动到目标传送点，传送失败");
                throw new Exception("多次尝试未移动到目标传送点，传送失败");
            }

            await MoveMapTo(target.X, target.Y, target.MapName, target.FinalClickZoomLevel);
            bigMapInAllMapRect = GetBigMapRect(target.MapName);
        } while (true);

        return bigMapInAllMapRect;
    }

    private void ClickTeleportTargetMapPoint(TeleportTargetContext target, Rect bigMapInAllMapRect)
    {
        var (clickX, clickY) = ConvertToGameRegionPosition(target.MapName, bigMapInAllMapRect, target.X, target.Y);
        using (var clickCapture = CaptureToRectArea())
        {
            var nearbyMapIcons = GetNearbyMapIcons(clickCapture, clickX, clickY, target.TargetTp);
            ClickBestNearbyMapIcon(clickCapture, nearbyMapIcons, clickX, clickY);
        }
    }

    private double GetTeleportFinalClickZoomLevel(double nearestTpDistance)
    {
        var targetZoomLevel = Math.Max(nearestTpDistance / TeleportFinalZoomDistanceFactor, MinTeleportZoomLevel);
        return ClampTeleportZoomLevel(targetZoomLevel);
    }

    private double GetTeleportTravelZoomLevel()
    {
        return GetTeleportMaxVisibleZoomLevel();
    }

    private double GetTeleportMaxVisibleZoomLevel()
    {
        return Math.Max(MinTeleportZoomLevel, Math.Min(DisplayTpPointZoomLevel, _tpConfig.MaxZoomLevel));
    }

    private double ClampTeleportZoomLevel(double zoomLevel)
    {
        return Math.Clamp(zoomLevel, MinTeleportZoomLevel, GetTeleportMaxVisibleZoomLevel());
    }

    private double GetCurrentBigMapZoomLevel()
    {
        using var capture = CaptureToRectArea();
        return GetBigMapZoomLevel(capture);
    }

    private async Task ClickTpPointAfterMapPointSelected(
        GiTpPosition? targetTp,
        string mapName,
        Rect bigMapInAllMapRect,
        double targetX,
        double targetY)
    {
        for (var pointAttempt = 0; pointAttempt < 3; pointAttempt++)
        {
            if (pointAttempt > 0)
            {
                ClickTargetMapPoint(targetTp, mapName, bigMapInAllMapRect, targetX, targetY);
                await Delay(300, ct);
            }

            if (await WaitForTeleportPanelAndConfirm(targetTp))
            {
                return;
            }
        }

        throw new TpPointNotActivate("等待传送点交互面板超时");
    }

    private async Task<bool> WaitForTeleportPanelAndConfirm(GiTpPosition? targetTp)
    {
        var retryInterval = GetTeleportPanelRetryInterval();
        for (var i = 0; i < 18; i++)
        {
            if (i > 0)
            {
                await Delay(retryInterval, ct);
            }

            using var teleportCapture = CaptureToRectArea();
            var result = await HandleTeleportPanel(teleportCapture, targetTp);
            switch (result)
            {
                case TeleportPanelResult.Confirmed:
                    return true;
                case TeleportPanelResult.RetryPoint:
                    return false;
            }
        }

        return false;
    }

    private void ClickTargetMapPoint(GiTpPosition? targetTp, string mapName, Rect bigMapInAllMapRect, double targetX, double targetY)
    {
        var (clickX, clickY) = ConvertToGameRegionPosition(mapName, bigMapInAllMapRect, targetX, targetY);
        using var capture = CaptureToRectArea();
        var nearbyMapIcons = GetNearbyMapIcons(capture, clickX, clickY, targetTp);
        ClickBestNearbyMapIcon(capture, nearbyMapIcons, clickX, clickY);
    }

    private static int GetTeleportPanelRetryInterval()
    {
        var delay = TaskContext.Instance().Config.QuickTeleportConfig.WaitTeleportPanelDelay;
        return Math.Clamp(delay <= 0 ? 50 : delay, 50, 300);
    }

    /// <summary>
    ///     检查传送是否完成，未完成则等待
    /// </summary>
    /// <param name="maxAttempts">最大检查延时的次数</param>
    /// <param name="delayMs">如果未完成加载，检查加载页面的延时。</param>
    private async Task WaitForTeleportCompletion(int maxAttempts, int delayMs)
    {
        await Delay(delayMs, ct);
        for (var i = 0; i < maxAttempts; i++)
        {
            using var capture = CaptureToRectArea();
            if (Bv.IsInMainUi(capture))
            {
                return;
            }

            await Delay(delayMs, ct);
            // 打开大地图期间推送的月卡会在传送之后直接显示，导致检测不到传送完成。
            await _blessingOfTheWelkinMoonTask.Start(ct);
        }

        Logger.LogWarning("传送等待超时，换台电脑吧");
    }

    /// <summary>
    /// 传送点是否在大地图窗口内
    /// </summary>
    /// <param name="mapName"></param>
    /// <param name="bigMapInAllMapRect">大地图在整个游戏地图中的矩形位置（原神坐标系）</param>
    /// <param name="x">传送点x坐标（原神坐标系）</param>
    /// <param name="y">传送点y坐标（原神坐标系）</param>
    /// <returns></returns>
    private bool IsPointInBigMapWindow(string mapName, Rect bigMapInAllMapRect, double x, double y)
    {
        return IsPointInBigMapClickableArea(mapName, bigMapInAllMapRect, x, y);
    }

    private bool IsPointInBigMapClickableArea(string mapName, Rect bigMapInAllMapRect, double x, double y)
    {
        // 坐标不包含直接返回
        if (!bigMapInAllMapRect.Contains(x, y))
        {
            return false;
        }

        var (clickX, clickY) = ConvertToGameRegionPosition(mapName, bigMapInAllMapRect, x, y);
        var safeMargin = MapClickSafeMargin * _zoomOutMax1080PRatio;

        // 屏蔽左上角360x400区域
        if (clickX < 360 * _zoomOutMax1080PRatio && clickY < 400 * _zoomOutMax1080PRatio)
        {
            return false;
        }

        if (clickX < safeMargin
            || clickY < safeMargin
            || clickX > _captureRect.Width - safeMargin
            || clickY > _captureRect.Height - safeMargin)
        {
            return false;
        }

        return true;
    }

    private bool TryGetClickableTargetPosition(string mapName, double x, double y, out Rect bigMapInAllMapRect, out double clickX, out double clickY)
    {
        bigMapInAllMapRect = default;
        clickX = 0;
        clickY = 0;

        try
        {
            bigMapInAllMapRect = GetBigMapRect(mapName);
            if (!IsPointInBigMapClickableArea(mapName, bigMapInAllMapRect, x, y))
            {
                return false;
            }

            (clickX, clickY) = ConvertToGameRegionPosition(mapName, bigMapInAllMapRect, x, y);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 转换传送点坐标到窗体内需要点击的坐标
    /// </summary>
    /// <param name="mapName"></param>
    /// <param name="bigMapInAllMapRect">大地图在整个游戏地图中的矩形位置（原神坐标系）</param>
    /// <param name="x">传送点x坐标（原神坐标系）</param>
    /// <param name="y">传送点y坐标（原神坐标系）</param>
    /// <returns></returns>
    private (double clickX, double clickY) ConvertToGameRegionPosition(string mapName, Rect bigMapInAllMapRect, double x, double y)
    {
        var map = MapManager.GetMap(mapName, _mapMatchingMethod);
        var (picX, picY) = map.ConvertGenshinMapCoordinatesToImageCoordinates(new Point2f((float)x, (float)y));
        var picRect = map.ConvertGenshinMapCoordinatesToImageCoordinates(bigMapInAllMapRect);
        var clickX = (picX - picRect.X) / picRect.Width * _captureRect.Width;
        var clickY = (picY - picRect.Y) / picRect.Height * _captureRect.Height;
        return (clickX, clickY);
    }

    public async Task CheckInBigMapUi()
    {
        // 尝试打开地图失败后，先回到主界面后再次尝试打开地图
        if (!await TryToOpenBigMapUi())
        {
            await new ReturnMainUiTask().Start(ct);
            await Delay(500, ct);
            if (!await TryToOpenBigMapUi())
            {
                throw new RetryException("打开大地图失败，请检查按键绑定中「打开地图」按键设置是否和原神游戏中一致！");
            }
        }
    }

    /// <summary>
    /// 尝试打开地图界面
    /// </summary>
    private async Task<bool> TryToOpenBigMapUi()
    {
        if (IsInBigMapUi())
        {
            return true;
        }

        RestartTeleportMToFTiming();
        Simulation.SendInput.SimulateAction(GIActions.OpenMap);
        return await WaitForBigMapUiAppear(12, 200);
    }

    private void RestartTeleportMToFTiming()
    {
        _teleportMToFStopwatch = Stopwatch.StartNew();
        _teleportMToFTarget = null;
    }

    private void SetTeleportMToFTimingTarget(TeleportTargetContext target)
    {
        if (_teleportMToFStopwatch == null)
        {
            return;
        }

        _teleportMToFTarget = GetTeleportTargetLogText(target);
    }

    private bool IsInBigMapUi()
    {
        using var capture = CaptureToRectArea();
        return Bv.IsInBigMapUi(capture);
    }

    private async Task<bool> WaitForBigMapUiAppear(int checkTimes, int delayMs)
    {
        for (var i = 0; i < checkTimes; i++)
        {
            if (IsInBigMapUi())
            {
                return true;
            }

            await Delay(delayMs, ct);
        }

        return false;
    }


    public async Task<(double, double)> Tp(double tpX, double tpY, string mapName = "Teyvat", bool force = false)
    {
        for (var i = 0; i < 3; i++)
        {
            try
            {
                return await TpOnce(tpX, tpY, mapName, force);
            }
            catch (TpPointNotActivate e)
            {
                // throw; // 不抛出异常，继续重试
                Logger.LogWarning(e.Message + "  重试");
                await Delay(300, ct);
            }
            catch (Exception e) when (IsTaskStopException(e))
            {
                throw;
            }
            catch (Exception)
            {
            }
        }

        throw new InvalidOperationException("传送失败");
    }

    /// <summary>
    /// 移动地图到指定传送点位置
    /// 可能会移动不对，所以可以重试此方法
    /// </summary>
    /// <param name="x">目标x坐标</param>
    /// <param name="y">目标y坐标</param>
    /// <param name="mapName">地图名称</param>
    /// <param name="finalZoomLevel">到达目标点的最小缩放等级，只在 MapZoomEnabled 为 True 生效</param>
    public async Task MoveMapTo(double x, double y, string mapName, double finalZoomLevel = 2)
    {
        // 参数初始化
        double minZoomLevel = ClampTeleportZoomLevel(finalZoomLevel);
        double maxZoomLevel = GetTeleportTravelZoomLevel();
        double currentZoomLevel = GetCurrentBigMapZoomLevel();
        int exceptionTimes = 0;
        Point2f mapCenterPoint;
        try
        {
            mapCenterPoint = GetPositionFromBigMap(mapName); // 初始中心
        }
        catch (MapPositionNotRecognizedException)
        {
            // 判断当前缩放是否离最佳识别缩放(4.4)较远，如果是，则先调整到最佳视角尝试
            if (_tpConfig.MapZoomEnabled && Math.Abs(currentZoomLevel - maxZoomLevel) > 0.3)
            {
                await AdjustMapZoomLevel(currentZoomLevel, maxZoomLevel);
                currentZoomLevel = maxZoomLevel;
                await Delay(300, ct);

                try
                {
                    mapCenterPoint = GetPositionFromBigMap(mapName);
                }
                catch (MapPositionNotRecognizedException)
                {
                    await ForceJumpToTargetArea(x, y, mapName);
                    await Delay(300, ct);

                    try
                    {
                        mapCenterPoint = GetPositionFromBigMap(mapName);
                    }
                    catch (MapPositionNotRecognizedException ex)
                    {
                        throw new Exception("所有脱困策略均失效，无法获取初始点", ex);
                    }
                }
            }
            else
            {
                await ForceJumpToTargetArea(x, y, mapName);
                await Delay(300, ct);

                try
                {
                    mapCenterPoint = GetPositionFromBigMap(mapName);
                }
                catch (MapPositionNotRecognizedException ex)
                {
                    throw new Exception("初始识别失败且切换区域后依然无效", ex);
                }
            }
        }

        var moveState = GetMoveMapState(mapCenterPoint, x, y, currentZoomLevel);
        // 缩小地图到恰当的缩放
        if (_tpConfig.MapZoomEnabled)
        {
            if (moveState.MouseDistance > _tpConfig.MapZoomOutDistance)
            {
                double targetZoomLevel = currentZoomLevel * moveState.MouseDistance / _tpConfig.MapZoomOutDistance;
                targetZoomLevel = Math.Min(targetZoomLevel, maxZoomLevel);
                await AdjustMapZoomLevel(currentZoomLevel, targetZoomLevel);
                double nextZoomLevel = GetCurrentBigMapZoomLevel();
                moveState = moveState.ScaleMouseDelta(currentZoomLevel / nextZoomLevel);
                currentZoomLevel = nextZoomLevel;
            }
        }

        // 开始移动并放大地图
        for (var iteration = 0; iteration < _tpConfig.MaxIterations; iteration++)
        {
            var targetClickable = TryGetClickableTargetPosition(mapName, x, y, out _, out _, out _);
            if (targetClickable)
            {
                if (_tpConfig.MapZoomEnabled && currentZoomLevel > minZoomLevel + _tpConfig.PrecisionThreshold)
                {
                    await AdjustMapZoomLevel(currentZoomLevel, minZoomLevel);
                    currentZoomLevel = GetCurrentBigMapZoomLevel();
                    if (TryGetRecognizedMoveMapState(mapName, x, y, currentZoomLevel, out var recognizedState))
                    {
                        moveState = recognizedState;
                        exceptionTimes = 0;
                    }

                    if (!TryGetClickableTargetPosition(mapName, x, y, out _, out _, out _))
                    {
                        continue;
                    }
                }

                break;
            }

            if (_tpConfig.MapZoomEnabled)
            {
                if (moveState.MouseDistance < _tpConfig.MapZoomInDistance)
                {
                    double targetZoomLevel = currentZoomLevel * moveState.MouseDistance / _tpConfig.MapZoomInDistance;
                    targetZoomLevel = Math.Max(targetZoomLevel, minZoomLevel);
                    if (currentZoomLevel > minZoomLevel + _tpConfig.PrecisionThreshold)
                    {
                        await AdjustMapZoomLevel(currentZoomLevel, targetZoomLevel);
                        double nextZoomLevel = GetCurrentBigMapZoomLevel();
                        moveState = moveState.ScaleMouseDelta(currentZoomLevel / nextZoomLevel);
                        currentZoomLevel = nextZoomLevel;
                    }
                }
            }

            // 非常接近目标点，不再进一步调整
            if (moveState.MouseDistance < _tpConfig.Tolerance)
            {
                break;
            }

            int moveMouseX = (int)Math.Round(moveState.MouseDeltaX) * Math.Sign(moveState.XOffset);
            int moveMouseY = (int)Math.Round(moveState.MouseDeltaY) * Math.Sign(moveState.YOffset);
            // DpiScale 是 Windows 显示缩放倍率，实际拖动和预测必须使用同一口径。
            int effectiveMoveMouseX = GetDisplayScaleAdjustedMouseDelta(moveMouseX);
            int effectiveMoveMouseY = GetDisplayScaleAdjustedMouseDelta(moveMouseY);

            var mouseMoveDebug = await MouseMoveMap(effectiveMoveMouseX, effectiveMoveMouseY);

            // 推算理论上的移动后坐标 (惯性预测)
            Point2f predictedPoint = moveState.CenterPoint + new Point2f(
                (float)(mouseMoveDebug.SentDeltaX * currentZoomLevel / _tpConfig.MapScaleFactor),
                (float)(mouseMoveDebug.SentDeltaY * currentZoomLevel / _tpConfig.MapScaleFactor));

            try
            {
                var newCenterPoint = GetPositionFromBigMap(mapName); // 随循环更新的地图中心

                // 计算识别坐标与预测坐标的偏差
                double jumpDistance = Math.Sqrt(Math.Pow(newCenterPoint.X - predictedPoint.X, 2) + Math.Pow(newCenterPoint.Y - predictedPoint.Y, 2));
                double expectedMoveLen = Math.Sqrt(mouseMoveDebug.SentDeltaX * mouseMoveDebug.SentDeltaX + mouseMoveDebug.SentDeltaY * mouseMoveDebug.SentDeltaY) * currentZoomLevel / _tpConfig.MapScaleFactor;
                double predictedDeltaX = predictedPoint.X - moveState.CenterPoint.X;
                double predictedDeltaY = predictedPoint.Y - moveState.CenterPoint.Y;
                double actualDeltaX = newCenterPoint.X - moveState.CenterPoint.X;
                double actualDeltaY = newCenterPoint.Y - moveState.CenterPoint.Y;
                double actualMoveLen = Math.Sqrt(actualDeltaX * actualDeltaX + actualDeltaY * actualDeltaY);
                double moveRatio = expectedMoveLen > 0 ? actualMoveLen / expectedMoveLen : 0;
                double moveDirectionCos = GetMoveDirectionCos(predictedDeltaX, predictedDeltaY, actualDeltaX, actualDeltaY);
                // 如果识别结果和本次拖动的距离/方向明显不一致，则判定为低特征区域误识别，进入盲走推算。
                if (IsMapMoveRecognitionAnomaly(expectedMoveLen, actualMoveLen, moveRatio, moveDirectionCos, jumpDistance))
                {
                    throw new MapPositionNotRecognizedException("中心点识别坐标异常跳跃");
                }

                moveState = GetMoveMapState(newCenterPoint, x, y, currentZoomLevel);
                exceptionTimes = 0;
            }
            catch (MapPositionNotRecognizedException)
            {
                exceptionTimes++;
                if (exceptionTimes > 5)
                {
                    throw new Exception("多次中心点识别失败或异常，惯性推算失效，重新传送");
                }

                moveState = GetMoveMapState(predictedPoint, x, y, currentZoomLevel);
            }
        }

    }

    private MapMoveState GetMoveMapState(
        Point2f mapCenterPoint,
        double x,
        double y,
        double currentZoomLevel)
    {
        var xOffset = x - mapCenterPoint.X;
        var yOffset = y - mapCenterPoint.Y;
        double totalMoveMouseX = _tpConfig.MapScaleFactor * Math.Abs(xOffset) / currentZoomLevel;
        double totalMoveMouseY = _tpConfig.MapScaleFactor * Math.Abs(yOffset) / currentZoomLevel;
        double mouseDistance = Math.Sqrt(totalMoveMouseX * totalMoveMouseX + totalMoveMouseY * totalMoveMouseY);
        return new MapMoveState(mapCenterPoint, xOffset, yOffset, totalMoveMouseX, totalMoveMouseY, mouseDistance);
    }

    private bool TryGetRecognizedMoveMapState(
        string mapName,
        double x,
        double y,
        double currentZoomLevel,
        out MapMoveState moveState)
    {
        try
        {
            var mapCenterPoint = GetPositionFromBigMap(mapName);
            moveState = GetMoveMapState(mapCenterPoint, x, y, currentZoomLevel);
            return true;
        }
        catch (MapPositionNotRecognizedException)
        {
            moveState = default;
            return false;
        }
    }

    /// <summary>
    /// 点击并移动鼠标
    /// </summary>
    /// <param name="x1">鼠标初始位置x</param>
    /// <param name="y1">鼠标初始位置y</param>
    /// <param name="x2">鼠标移动后位置x</param> 
    /// <param name="y2">鼠标移动后位置y</param>
    public async Task MouseClickAndMove(int x1, int y1, int x2, int y2)
    {
        // GlobalMethod.MoveMouseTo(x1, y1);
        GameCaptureRegion.GameRegionMove((rect, scale) => (x1 * scale, y1 * scale));
        await Delay(50, ct);
        GlobalMethod.LeftButtonDown();
        await Delay(50, ct);
        // GlobalMethod.MoveMouseTo(x2, y2);
        GameCaptureRegion.GameRegionMove((rect, scale) => (x2 * scale, y2 * scale));
        await Delay(50, ct);
        GlobalMethod.LeftButtonUp();
        await Delay(50, ct);
        GameCaptureRegion.GameRegionMove((rect, scale) => (rect.Width / 2d, rect.Height / 2d));
    }

    /// <summary>
    /// 调整地图的缩放等级（整数缩放级别）。
    /// </summary>
    /// <param name="zoomLevel">目标等级：1-6。整数。随着数字变大地图越小，细节越少。</param>
    [Obsolete]
    public async Task AdjustMapZoomLevel(int zoomLevel)
    {
        await AdjustMapZoomLevel(GetCurrentBigMapZoomLevel(), zoomLevel);
    }

    /// <summary>
    /// 将大地图缩放等级设置为指定值
    /// </summary>
    /// <remarks>
    /// 缩放等级说明：
    /// - 数值范围：1.0(最大地图) 到 6.0(最小地图)
    /// - 缩放效果：数值越大，地图显示范围越广，细节越少
    /// - 缩放位置：1.0 对应缩放条最上方，6.0 对应缩放条最下方
    /// - 推荐范围：建议在 2.0 到 5.0 之间调整，过大或过小可能影响操作
    /// </remarks>
    /// <param name="zoomLevel">当前缩放等级：1.0-6.0，浮点数。</param>
    /// <param name="targetZoomLevel">目标缩放等级：1.0-6.0，浮点数。</param>
    public async Task AdjustMapZoomLevel(double zoomLevel, double targetZoomLevel)
    {
        targetZoomLevel = ClampBigMapZoomLevel(targetZoomLevel);
        zoomLevel = IsFinite(zoomLevel) ? ClampBigMapZoomLevel(zoomLevel) : GetCurrentBigMapZoomLevel();
        var currentZoomLevel = zoomLevel;
        if (IsZoomCloseEnough(currentZoomLevel, targetZoomLevel))
        {
            return;
        }

        var noProgressTimes = 0;
        for (var attempt = 0; attempt < MaxMapZoomWheelAttempts && !IsZoomCloseEnough(currentZoomLevel, targetZoomLevel); attempt++)
        {
            var diff = targetZoomLevel - currentZoomLevel;
            var desiredZoomDirection = Math.Sign(diff);
            if (desiredZoomDirection == 0)
            {
                break;
            }

            var perNotch = Math.Max(_mapZoomLevelPerWheelNotch, _tpConfig.PrecisionThreshold);
            var notchCount = Math.Clamp((int)Math.Round(Math.Abs(diff) / perNotch), 1, MaxMapZoomWheelBatchNotches);
            var wheelNotches = MapZoomWheelSignForZoomLevelIncrease * desiredZoomDirection * notchCount;
            var beforeZoomLevel = currentZoomLevel;
            currentZoomLevel = await ScrollMapZoomAndMeasure(wheelNotches);
            var actualDelta = currentZoomLevel - beforeZoomLevel;
            UpdateMapZoomWheelCalibration(wheelNotches, actualDelta);

            if (Math.Abs(actualDelta) < _tpConfig.PrecisionThreshold / 2d ||
                Math.Abs(targetZoomLevel - currentZoomLevel) >= Math.Abs(targetZoomLevel - beforeZoomLevel) - _tpConfig.PrecisionThreshold / 2d)
            {
                noProgressTimes++;
                if (noProgressTimes >= 2)
                {
                    break;
                }
            }
            else
            {
                noProgressTimes = 0;
            }
        }
    }

    private async Task<double> ScrollMapZoomAndMeasure(int wheelNotches)
    {
        GameCaptureRegion.GameRegionMove((rect, scale) => (rect.Width / 2d, rect.Height / 2d));
        await Delay(20, ct);
        var singleWheelNotch = Math.Sign(wheelNotches);
        for (var i = 0; i < Math.Abs(wheelNotches); i++)
        {
            Simulation.SendInput.Mouse.VerticalScroll(singleWheelNotch);
            if (i + 1 < Math.Abs(wheelNotches))
            {
                await Delay(MapZoomWheelBurstIntervalMs, ct);
            }
        }

        await Delay(GetMapZoomWheelDelay(wheelNotches), ct);
        var afterZoomLevel = GetCurrentBigMapZoomLevel();
        return afterZoomLevel;
    }

    private static int GetMapZoomWheelDelay(int wheelNotches)
    {
        return 90 + Math.Min(120, Math.Abs(wheelNotches) * 8);
    }

    private void UpdateMapZoomWheelCalibration(int wheelNotches, double zoomDelta)
    {
        if (wheelNotches == 0 || Math.Abs(zoomDelta) < _tpConfig.PrecisionThreshold / 2d)
        {
            return;
        }

        var measuredPerNotch = Math.Abs(zoomDelta) / Math.Abs(wheelNotches);
        _mapZoomLevelPerWheelNotch = _mapZoomLevelPerWheelNotch * 0.7d + measuredPerNotch * 0.3d;
    }

    private bool IsZoomCloseEnough(double zoomLevel, double targetZoomLevel)
    {
        var wheelTolerance = Math.Max(_tpConfig.PrecisionThreshold, _mapZoomLevelPerWheelNotch * 0.75d);
        return Math.Abs(zoomLevel - targetZoomLevel) <= wheelTolerance;
    }

    private static double ClampBigMapZoomLevel(double zoomLevel)
    {
        return Math.Clamp(zoomLevel, 1d, 6d);
    }

    private static int GetDisplayScaleAdjustedMouseDelta(int pixelDelta)
    {
        double displayScale = TaskContext.Instance().DpiScale;
        return (int)(pixelDelta / displayScale);
    }

    private static (int DesktopX, int DesktopY, double CaptureX, double CaptureY) GetCursorDebugPosition()
    {
        User32.GetCursorPos(out var cursor);
        var captureRect = TaskContext.Instance().SystemInfo.CaptureAreaRect;
        return (cursor.X, cursor.Y, cursor.X - captureRect.X, cursor.Y - captureRect.Y);
    }

    private static bool TryPickSafeMapDragStart(
        double preferredX,
        double preferredY,
        double startMinX,
        double startMaxX,
        double startMinY,
        double startMaxY,
        double deltaX,
        double deltaY,
        double safeMinX,
        double safeMaxX,
        double safeMinY,
        double safeMaxY,
        double forbiddenWidth,
        double forbiddenHeight,
        double avoidPadding,
        out double startX,
        out double startY)
    {
        startX = 0;
        startY = 0;
        double bestX = 0;
        double bestY = 0;
        double bestDistance = double.MaxValue;

        bool IsSafePoint(double x, double y)
        {
            return x >= safeMinX && x <= safeMaxX &&
                   y >= safeMinY && y <= safeMaxY &&
                   !(x < forbiddenWidth && y < forbiddenHeight);
        }

        void TryCandidate(double x, double y)
        {
            x = Math.Clamp(x, startMinX, startMaxX);
            y = Math.Clamp(y, startMinY, startMaxY);
            if (!IsSafePoint(x, y) || !IsSafePoint(x + deltaX, y + deltaY))
            {
                return;
            }

            double distance = Math.Pow(x - preferredX, 2) + Math.Pow(y - preferredY, 2);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestX = x;
                bestY = y;
            }
        }

        double safeForbiddenX = forbiddenWidth + avoidPadding;
        double safeForbiddenY = forbiddenHeight + avoidPadding;
        double[] candidateXs =
        [
            preferredX,
            startMinX,
            startMaxX,
            safeForbiddenX,
            safeForbiddenX - deltaX
        ];
        double[] candidateYs =
        [
            preferredY,
            startMinY,
            startMaxY,
            safeForbiddenY,
            safeForbiddenY - deltaY
        ];

        foreach (double x in candidateXs)
        {
            foreach (double y in candidateYs)
            {
                TryCandidate(x, y);
            }
        }

        if (bestDistance >= double.MaxValue)
        {
            return false;
        }

        startX = bestX;
        startY = bestY;
        return true;
    }

    private async Task<(int SentDeltaX, int SentDeltaY, int Steps, double StartX, double StartY, double EndX, double EndY, double ActualDeltaX, double ActualDeltaY)> MouseMoveMap(int pixelDeltaX, int pixelDeltaY)
    {
        // 起点向预期拖动方向的反方向偏移，并保留随机性；位移按可拖动地图区域裁剪。
        double startX = 0;
        double startY = 0;
        double endX = 0;
        double endY = 0;
        int sentDeltaX = 0;
        int sentDeltaY = 0;
        GameCaptureRegion.GameRegionMove((rect, scale) =>
        {
            double expectedDeltaX = pixelDeltaX * scale;
            double expectedDeltaY = pixelDeltaY * scale;
            double edgePadding = Math.Max(8d * scale, 115d * scale);
            double minX = edgePadding;
            double maxX = rect.Width - edgePadding;
            double minY = edgePadding;
            double maxY = rect.Height - edgePadding;
            double availableX = Math.Max(0, maxX - minX);
            double availableY = Math.Max(0, maxY - minY);
            double dragRatioByX = expectedDeltaX == 0 ? 1 : availableX / Math.Abs(expectedDeltaX);
            double dragRatioByY = expectedDeltaY == 0 ? 1 : availableY / Math.Abs(expectedDeltaY);
            double dragRatio = Math.Min(1, Math.Min(dragRatioByX, dragRatioByY));
            double forbiddenWidth = 360d * scale;
            double forbiddenHeight = 400d * scale;
            double avoidPadding = Math.Max(4d, 6d * scale);

            for (var attempt = 0; attempt < 12; attempt++)
            {
                sentDeltaX = (int)(pixelDeltaX * dragRatio);
                sentDeltaY = (int)(pixelDeltaY * dragRatio);
                double sentActualDeltaX = sentDeltaX * scale;
                double sentActualDeltaY = sentDeltaY * scale;
                double startMinX = Math.Max(minX, minX - sentActualDeltaX);
                double startMaxX = Math.Min(maxX, maxX - sentActualDeltaX);
                double startMinY = Math.Max(minY, minY - sentActualDeltaY);
                double startMaxY = Math.Min(maxY, maxY - sentActualDeltaY);
                if (startMinX > startMaxX || startMinY > startMaxY)
                {
                    dragRatio *= 0.85d;
                    continue;
                }

                double jitterX = (Random.Shared.NextDouble() - 0.5d) * rect.Width / 12d;
                double jitterY = (Random.Shared.NextDouble() - 0.5d) * rect.Height / 12d;
                double centeredStartX = rect.Width / 2d - sentActualDeltaX / 2d + jitterX;
                double centeredStartY = rect.Height / 2d - sentActualDeltaY / 2d + jitterY;
                if (TryPickSafeMapDragStart(
                        centeredStartX,
                        centeredStartY,
                        startMinX,
                        startMaxX,
                        startMinY,
                        startMaxY,
                        sentActualDeltaX,
                        sentActualDeltaY,
                        minX,
                        maxX,
                        minY,
                        maxY,
                        forbiddenWidth,
                        forbiddenHeight,
                        avoidPadding,
                        out startX,
                        out startY))
                {
                    endX = startX + sentActualDeltaX;
                    endY = startY + sentActualDeltaY;
                    return (startX, startY);
                }

                dragRatio *= 0.85d;
            }

            sentDeltaX = 0;
            sentDeltaY = 0;
            startX = Math.Clamp(rect.Width / 2d, minX, maxX);
            startY = Math.Clamp(rect.Height / 2d, minY, maxY);
            endX = startX;
            endY = startY;
            return (startX, startY);
        });

        double moveMouseLength = Math.Sqrt(sentDeltaX * sentDeltaX + sentDeltaY * sentDeltaY);
        int steps = GetMapDragStepCount(moveMouseLength);
        int[] stepX = GenerateSteps(sentDeltaX, steps);
        int[] stepY = GenerateSteps(sentDeltaY, steps);
        var startCursor = GetCursorDebugPosition();
        Simulation.SendInput.Mouse.LeftButtonDown();
        int movedX = 0;
        int movedY = 0;
        for (var i = 0; i < steps; i++)
        {
            var i1 = i;
            await Delay(_tpConfig.StepIntervalMilliseconds, ct);
            movedX += stepX[i1];
            movedY += stepY[i1];
            if (_tpConfig.MapDragUseRelativeMove)
            {
                GameCaptureRegion.GameRegionMoveBy((_, scale) => (stepX[i1] * scale, stepY[i1] * scale));
            }
            else
            {
                GameCaptureRegion.GameRegionMove((_, scale) => (startX + movedX * scale, startY + movedY * scale));
            }
        }

        Simulation.SendInput.Mouse.LeftButtonUp();
        var endCursor = GetCursorDebugPosition();
        return (sentDeltaX, sentDeltaY, steps, startX, startY, endX, endY, endCursor.CaptureX - startCursor.CaptureX, endCursor.CaptureY - startCursor.CaptureY);
    }

    private static int GetMapDragStepCount(double moveMouseLength)
    {
        if (moveMouseLength <= 0)
        {
            return 3;
        }

        return Math.Clamp((int)Math.Ceiling(moveMouseLength / MapDragPixelsPerStep), 5, 60);
    }

    private static int[] GenerateSteps(int delta, int steps)
    {
        if (steps <= 1)
        {
            return [delta];
        }

        var fastSteps = Math.Clamp((int)Math.Ceiling(steps * MapDragFastStepRatio), 1, steps);
        var slowSteps = steps - fastSteps;
        if (slowSteps == 0)
        {
            return DistributeSteps(delta, steps, _ => 1d);
        }

        var fastDelta = (int)Math.Round(delta * MapDragFastDistanceRatio);
        var slowDelta = delta - fastDelta;
        var fastPart = DistributeSteps(fastDelta, fastSteps, _ => 1d);
        var slowPart = DistributeSteps(
            slowDelta,
            slowSteps,
            i =>
            {
                var t = slowSteps <= 1 ? 1d : i / (double)(slowSteps - 1);
                return 0.2d + 0.8d * Math.Cos(t * Math.PI / 2d);
            });

        var result = new int[steps];
        Array.Copy(fastPart, 0, result, 0, fastPart.Length);
        Array.Copy(slowPart, 0, result, fastPart.Length, slowPart.Length);
        return result;
    }

    private static int[] DistributeSteps(int delta, int steps, Func<int, double> weightFactory)
    {
        var result = new int[steps];
        if (steps == 0)
        {
            return result;
        }

        var weights = new double[steps];
        var sum = 0d;
        for (var i = 0; i < steps; i++)
        {
            weights[i] = Math.Max(0.001d, weightFactory(i));
            sum += weights[i];
        }

        var remaining = delta;
        for (var i = 0; i < steps; i++)
        {
            result[i] = (int)(delta * weights[i] / sum);
            remaining -= result[i];
        }

        for (var i = 0; i < Math.Abs(remaining); i++)
        {
            result[i % steps] += remaining > 0 ? 1 : -1;
        }

        return result;
    }

    private static double GetMoveDirectionCos(
        double expectedDeltaX,
        double expectedDeltaY,
        double actualDeltaX,
        double actualDeltaY)
    {
        var expectedLen = Math.Sqrt(expectedDeltaX * expectedDeltaX + expectedDeltaY * expectedDeltaY);
        var actualLen = Math.Sqrt(actualDeltaX * actualDeltaX + actualDeltaY * actualDeltaY);
        if (expectedLen <= 1 || actualLen <= 1)
        {
            return 1;
        }

        return (expectedDeltaX * actualDeltaX + expectedDeltaY * actualDeltaY) / (expectedLen * actualLen);
    }

    private static bool IsMapMoveRecognitionAnomaly(
        double expectedMoveLen,
        double actualMoveLen,
        double moveRatio,
        double moveDirectionCos,
        double jumpDistance)
    {
        if (jumpDistance > Math.Max(200, expectedMoveLen * 2))
        {
            return true;
        }

        if (expectedMoveLen > 1200 && (moveRatio < 0.55 || moveRatio > 1.85))
        {
            return true;
        }

        return expectedMoveLen > 1200 && actualMoveLen > 120 && moveDirectionCos < 0.65;
    }

    public Point2f GetPositionFromBigMap(string mapName)
    {
        return GetBigMapCenterPoint(mapName);
    }

    public Point2f? GetPositionFromBigMapNullable(string mapName)
    {
        try
        {
            return GetBigMapCenterPoint(mapName);
        }
        catch
        {
            return null;
        }
    }

    public Rect GetBigMapRect(string mapName)
    {
        var rect = new Rect();
        NewRetry.Do(() =>
        {
            // 判断是否在地图界面
            using var ra = CaptureToRectArea();
            using var mapScaleButtonRa = ra.Find(QuickTeleportAssets.Instance.MapScaleButtonRo);
            if (mapScaleButtonRa.IsExist())
            {
                try
                {
                    rect = MapManager.GetMap(mapName, _mapMatchingMethod).GetBigMapRect(ra.CacheGreyMat);
                }
                catch (Exception)
                {
                    rect = default; // 发生异常视为识别失败
                }

                if (rect == default)
                {
                    // 滚轮调整后再次识别
                    Simulation.SendInput.Mouse.VerticalScroll(2);
                    Sleep(500);
                    throw new RetryException("识别大地图位置失败");
                }
            }
            else
            {
                throw new RetryException("当前不在地图界面");
            }
        }, TimeSpan.FromMilliseconds(500), 5);

        if (rect == default)
        {
            throw new InvalidOperationException("多次重试后，识别大地图位置失败");
        }

        // 提瓦特大陆由于用的256的图，需要做特殊逻辑
        if (mapName == MapTypes.Teyvat.ToString())
        {
            const int s = TeyvatMap.BigMap256ScaleTo2048; // 相对2048做8倍缩放
            rect = new Rect(rect.X * s, rect.Y * s, rect.Width * s, rect.Height * s);
        }

        return MapManager.GetMap(mapName, _mapMatchingMethod).ConvertImageCoordinatesToGenshinMapCoordinates(rect)!.Value;
    }

    public Point2f GetBigMapCenterPoint(string mapName)
    {
        // 判断是否在地图界面
        using var ra = CaptureToRectArea();
        using var mapScaleButtonRa = ra.Find(QuickTeleportAssets.Instance.MapScaleButtonRo);
        if (mapScaleButtonRa.IsExist())
        {
            Point2f p;
            try
            {
                p = MapManager.GetMap(mapName, _mapMatchingMethod).GetBigMapPosition(ra.CacheGreyMat);
            }
            catch (Exception ex)
            {
                throw new MapPositionNotRecognizedException("大地图特征点匹配引发异常：" + ex.Message, ex);
            }

            if (p.IsEmpty())
            {
                throw new MapPositionNotRecognizedException("大地图特征点匹配识别位置失败");
            }

            // 提瓦特大陆由于用的256的图，需要做特殊逻辑
            var (x, y) = (p.X, p.Y);
            if (mapName == MapTypes.Teyvat.ToString())
            {
                (x, y) = (p.X * TeyvatMap.BigMap256ScaleTo2048, p.Y * TeyvatMap.BigMap256ScaleTo2048);
            }

            return MapManager.GetMap(mapName, _mapMatchingMethod).ConvertImageCoordinatesToGenshinMapCoordinates(new Point2f(x, y))!.Value;
        }
        else
        {
            throw new InvalidOperationException("当前不在地图界面");
        }
    }

    /// <summary>
    /// 当无法获取当前位置时，直接根据目标坐标强制计算并跃迁到对应区域的地图
    /// </summary>
    private async Task ForceJumpToTargetArea(double x, double y, string mapName)
    {
        if (mapName == MapTypes.Teyvat.ToString())
        {
            string targetCountry = "当前位置";
            double minDistance = double.MaxValue;
            foreach (var (country, position) in MapLazyAssets.Instance.CountryPositions)
            {
                var distance = Math.Sqrt(Math.Pow(position[0] - x, 2) + Math.Pow(position[1] - y, 2));
                if (distance < minDistance)
                {
                    minDistance = distance;
                    targetCountry = country;
                }
            }

            if (targetCountry != "当前位置")
            {
                await SwitchArea(targetCountry);
            }
        }
        else
        {
            await SwitchArea(MapTypesExtensions.ParseFromName(mapName).GetDescription());
        }
    }

    /// <summary>
    /// 获取最接近的N个传送点坐标和所处区域
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="n">获取最近的 n 个传送点</param>
    /// <returns></returns>
    public List<GiTpPosition> GetNearestNTpPoints(double x, double y, string mapName, int n = 1)
    {
        // 检查 n 的合法性
        if (n < 1)
        {
            throw new ArgumentException("The value of n must be greater than or equal to 1.", nameof(n));
        }

        // 按距离排序并选择前 n 个点
        return MapLazyAssets.Instance.ScenesDic[mapName].Points
            .OrderBy(tp => Math.Pow(tp.X - x, 2) + Math.Pow(tp.Y - y, 2))
            .Take(n)
            .ToList();
    }

    public async Task<bool> SwitchRecentlyCountryMap(double x, double y, string? forceCountry = null)
    {
        // 识别当前位置
        var minDistance = double.MaxValue;
        var bigMapCenterPointNullable = GetPositionFromBigMapNullable(MapTypes.Teyvat.ToString());

        if (bigMapCenterPointNullable != null)
        {
            var bigMapCenterPoint = bigMapCenterPointNullable.Value;
            minDistance = Math.Sqrt(Math.Pow(bigMapCenterPoint.X - x, 2) + Math.Pow(bigMapCenterPoint.Y - y, 2));
            if (minDistance < 50)
            {
                // 点位很近的情况下不切换
                return false;
            }
        }

        string minCountry = "当前位置";
        foreach (var (country, position) in MapLazyAssets.Instance.CountryPositions)
        {
            var distance = Math.Sqrt(Math.Pow(position[0] - x, 2) + Math.Pow(position[1] - y, 2));
            if (distance < minDistance)
            {
                minDistance = distance;
                minCountry = country;
            }
        }

        if (minCountry != "当前位置")
        {
            if (forceCountry != null)
            {
                minCountry = forceCountry;
            }

            await SwitchArea(minCountry);
            return true;
        }

        return false;
    }

    internal async Task SwitchArea(string areaName)
    {
        GameCaptureRegion.GameRegionClick((rect, scale) => (rect.Width - 160 * scale, rect.Height - 60 * scale));
        await Delay(300, ct);
        using var ra = CaptureToRectArea();
        var list = ra.FindMulti(new RecognitionObject
        {
            RecognitionType = RecognitionTypes.Ocr,
            RegionOfInterest = new Rect(ra.Width * 2 / 3, 0, ra.Width / 3, ra.Height),
            ReplaceDictionary = new Dictionary<string, string[]>
            {
                ["渊下宫"] = ["渊下宮"],
            },
        });
        string minCountryLocalized = this.stringLocalizer.WithCultureGet(this.cultureInfo, areaName);
        Region? matchRect = list.OrderByDescending(r => r.Y).FirstOrDefault(r => r.Text.Contains(minCountryLocalized));
        if (matchRect == null)
        {
            Logger.LogWarning("切换区域失败：{Country}", areaName);
            if (areaName == MapTypes.TheChasm.GetDescription() || areaName == MapTypes.Enkanomiya.GetDescription() || areaName == MapTypes.SeaOfBygoneEras.GetDescription() || areaName == MapTypes.AncientSacredMountain.GetDescription() || areaName == MapTypes.TempleOfSpace.GetDescription())
            {
                throw new Exception($"切换独立地图区域[{areaName}]失败");
            }
        }
        else
        {
            matchRect.Click();
            Logger.LogInformation("切换到区域：{Country}", areaName);
        }

        await Delay(500, ct);
    }

    private async Task SwitchToGroundMapLayerIfNeeded()
    {
        var layerSwitchClicked = false;
        for (var i = 0; i < MapLayerSwitchRetryCount; i++)
        {
            using var capture = CaptureToRectArea();
            using var groundButton = capture.Find(_assets.MapUndergroundToGroundButtonRo);
            if (groundButton.IsExist())
            {
                groundButton.Click();
                await Delay(MapGroundLayerSettlingDelayMs, ct);
                Logger.LogInformation("已切换到地表地图");
                return;
            }

            if (!layerSwitchClicked && Bv.BigMapIsUnderground(capture))
            {
                using var layerSwitchButton = capture.Find(_assets.MapUndergroundSwitchButtonRo);
                if (layerSwitchButton.IsExist())
                {
                    layerSwitchButton.Click();
                    layerSwitchClicked = true;
                }
            }

            await Delay(MapLayerSwitchRetryIntervalMs, ct);
        }
    }

    public async Task Tp(string name)
    {
        // 通过大地图传送到指定传送点
        await Delay(500, ct);
    }

    public async Task TpByF1(string name)
    {
        // 传送到指定传送点
        await Delay(500, ct);
    }

    public Task ClickTpPoint(
        ImageRegion imageRegion,
        GiTpPosition? targetTp = null,
        string mapName = "Teyvat",
        Rect bigMapInAllMapRect = default,
        double targetX = double.NaN,
        double targetY = double.NaN)
    {
        return ClickTpPoint(imageRegion, targetTp);
    }

    private async Task ClickTpPoint(
        ImageRegion imageRegion,
        GiTpPosition? targetTp)
    {
        var result = await HandleTeleportPanel(imageRegion, targetTp);
        if (result != TeleportPanelResult.Confirmed)
        {
            throw result == TeleportPanelResult.RetryPoint
                ? new TpPointNotActivate("传送点未激活或不存在")
                : new TpPointNotActivate("选项列表不存在传送点");
        }
    }

    private async Task<TeleportPanelResult> HandleTeleportPanel(
        ImageRegion imageRegion,
        GiTpPosition? targetTp)
    {
        if (!Bv.IsInBigMapUi(imageRegion))
        {
            return TeleportPanelResult.Confirmed;
        }

        using var teleportButton = imageRegion.Find(_assets.TeleportButtonRo);
        if (!teleportButton.IsEmpty())
        {
            PressTeleportConfirmKey();
            return TeleportPanelResult.Confirmed;
        }

        using var mapCloseRa1 = imageRegion.Find(_assets.MapCloseButtonRo);
        if (!mapCloseRa1.IsEmpty())
        {
            return TeleportPanelResult.RetryPoint;
        }

        var candidate = CheckMapChooseIcon(imageRegion, targetTp);
        if (candidate == null)
        {
            return TeleportPanelResult.Waiting;
        }

        return await WaitAndPressTeleportConfirm(candidate)
            ? TeleportPanelResult.Confirmed
            : TeleportPanelResult.RetryPoint;
    }

    private async Task<bool> WaitAndPressTeleportConfirm(MapChooseCandidate candidate)
    {
        for (var i = 0; i < 20; i++)
        {
            if (i > 0)
            {
                await Delay(200, ct);
            }

            using var screen = CaptureToRectArea();
            if (!Bv.IsInBigMapUi(screen))
            {
                return true;
            }

            using var teleportButton = screen.Find(_assets.TeleportButtonRo);
            if (!teleportButton.IsEmpty())
            {
                PressTeleportConfirmKey();
                return true;
            }
        }

        return false;
    }

    private void PressTeleportConfirmKey()
    {
        var stopwatch = _teleportMToFStopwatch;
        stopwatch?.Stop();
        var elapsed = stopwatch?.Elapsed;
        var target = _teleportMToFTarget;
        _teleportMToFStopwatch = null;
        _teleportMToFTarget = null;

        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_F);

        if (elapsed != null)
        {
            Logger.LogInformation(
                "传送完成，用时 {ElapsedSeconds:0.00} 秒",
                elapsed.Value.TotalSeconds);
        }
    }

    private List<NearbyMapIcon> GetNearbyMapIcons(ImageRegion imageRegion, double targetX, double targetY, GiTpPosition? targetTp)
    {
        if (!IsFinite(targetX) || !IsFinite(targetY))
        {
            return [];
        }

        var radius = GetNearbyMapIconSearchRadius();
        var searchRect = new Rect(
            (int)Math.Round(targetX - radius),
            (int)Math.Round(targetY - radius),
            (int)Math.Round(radius * 2),
            (int)Math.Round(radius * 2)).ClampTo(imageRegion.SrcMat);
        if (searchRect.Width <= 0 || searchRect.Height <= 0)
        {
            return [];
        }

        var result = new List<NearbyMapIcon>();
        var targetType = targetTp?.Type ?? string.Empty;
        var shouldFilterByTargetType = HasMapIconForTargetType(targetType);
        var isHdrCapture = TaskContext.Instance().Config.CaptureMode == nameof(CaptureModes.WindowsGraphicsCaptureHdr);
        for (var i = 0; i < _assets.MapChooseIconGreyMatList.Count; i++)
        {
            var template = _assets.MapChooseIconGreyMatList[i];
            if (template.Empty())
            {
                continue;
            }

            using var searchGrey = new Mat(imageRegion.CacheGreyMat, searchRect).Clone();
            var iconFileName = GetMapChooseIconFileName(_assets.MapChooseIconRoList[i]);
            var iconType = GetMapChooseIconType(iconFileName);
            var typeMatchesTarget = IsMapChooseIconTypeMatch(iconType, targetType);
            if (shouldFilterByTargetType && !typeMatchesTarget)
            {
                continue;
            }

            var threshold = Math.Min(_assets.MapChooseIconRoList[i].Threshold, isHdrCapture ? 0.68 : 0.72);
            var iconRects = MatchTemplateHelper.MatchOnePicForOnePic(searchGrey, template, null, threshold);
            foreach (var relativeIconRect in iconRects)
            {
                var rect = new Rect(
                    searchRect.X + relativeIconRect.X,
                    searchRect.Y + relativeIconRect.Y,
                    relativeIconRect.Width,
                    relativeIconRect.Height);
                var centerX = rect.X + rect.Width / 2d;
                var centerY = rect.Y + rect.Height / 2d;
                var distance = GetDistance(centerX, centerY, targetX, targetY);
                if (distance > radius)
                {
                    continue;
                }

                AddNearbyMapIcon(result, new NearbyMapIcon
                {
                    IconFileName = iconFileName,
                    IconType = iconType,
                    Rect = rect,
                    CenterX = centerX,
                    CenterY = centerY,
                    DistanceToTarget = distance,
                    TypeMatchesTarget = typeMatchesTarget,
                });
            }
        }

        var ordered = result
            .OrderBy(x => x.CenterY)
            .ThenBy(x => x.CenterX)
            .ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].GeometricIndex = i + 1;
        }

        return ordered;
    }

    private double GetNearbyMapIconSearchRadius()
    {
        return Math.Max(120, 170 * _zoomOutMax1080PRatio);
    }

    private bool HasMapIconForTargetType(string targetType)
    {
        return !string.IsNullOrEmpty(targetType) &&
               _assets.MapChooseIconRoList
                   .Select(ro => GetMapChooseIconType(GetMapChooseIconFileName(ro)))
                   .Any(iconType => IsMapChooseIconTypeMatch(iconType, targetType));
    }

    private static void AddNearbyMapIcon(List<NearbyMapIcon> icons, NearbyMapIcon newIcon)
    {
        const double sameIconMaxDistance = 18;
        var sameIcon = icons.FirstOrDefault(icon => GetDistance(icon.CenterX, icon.CenterY, newIcon.CenterX, newIcon.CenterY) <= sameIconMaxDistance);
        if (sameIcon == null)
        {
            icons.Add(newIcon);
            return;
        }

        if ((!sameIcon.TypeMatchesTarget && newIcon.TypeMatchesTarget) ||
            (sameIcon.TypeMatchesTarget == newIcon.TypeMatchesTarget && newIcon.DistanceToTarget < sameIcon.DistanceToTarget))
        {
            icons.Remove(sameIcon);
            icons.Add(newIcon);
        }
    }

    private NearbyMapIcon? ClickBestNearbyMapIcon(ImageRegion imageRegion, List<NearbyMapIcon> nearbyMapIcons, double fallbackX, double fallbackY)
    {
        var icon = ChooseTargetNearbyMapIcon(nearbyMapIcons);
        if (icon == null)
        {
            imageRegion.ClickTo(fallbackX, fallbackY);
            return null;
        }

        foreach (var nearbyIcon in nearbyMapIcons)
        {
            nearbyIcon.IsTargetIcon = ReferenceEquals(nearbyIcon, icon);
        }

        imageRegion.ClickTo(icon.CenterX, icon.CenterY);
        return icon;
    }

    private static NearbyMapIcon? ChooseTargetNearbyMapIcon(List<NearbyMapIcon> nearbyMapIcons)
    {
        if (nearbyMapIcons.Count == 0)
        {
            return null;
        }

        var nearest = nearbyMapIcons.OrderBy(x => x.DistanceToTarget).First();
        var nearestMatchedType = nearbyMapIcons
            .Where(x => x.TypeMatchesTarget)
            .OrderBy(x => x.DistanceToTarget)
            .FirstOrDefault();
        if (nearestMatchedType == null || nearest.TypeMatchesTarget)
        {
            return nearest;
        }

        return nearestMatchedType.DistanceToTarget <= nearest.DistanceToTarget + 35
            ? nearestMatchedType
            : nearest;
    }

    /// <summary>
    /// 识别候选列表，并尽量选择与目标坐标对应的实际传送点。
    /// </summary>
    private MapChooseCandidate? CheckMapChooseIcon(
        ImageRegion imageRegion,
        GiTpPosition? targetTp)
    {
        var candidates = GetMapChooseCandidates(imageRegion);
        if (candidates.Count == 0)
        {
            return null;
        }

        var chosenCandidate = ChooseMapCandidate(candidates, targetTp);
        if (chosenCandidate == null)
        {
            return null;
        }

        ClickMapChooseCandidate(imageRegion, chosenCandidate);
        return chosenCandidate;
    }

    private List<MapChooseCandidate> GetMapChooseCandidates(ImageRegion imageRegion)
    {
        var candidates = new List<MapChooseCandidate>();
        var isHdrCapture = TaskContext.Instance().Config.CaptureMode == nameof(CaptureModes.WindowsGraphicsCaptureHdr);
        var threshold = isHdrCapture ? 0.7 : 0.8;

        for (var i = 0; i < _assets.MapChooseIconGreyMatList.Count; i++)
        {
            using var mapChooseIconRoi = imageRegion.CacheGreyMat[_assets.MapChooseIconRoi].Clone();
            var iconFileName = GetMapChooseIconFileName(_assets.MapChooseIconRoList[i]);
            var iconType = GetMapChooseIconType(iconFileName);
            var iconRects = MatchTemplateHelper.MatchOnePicForOnePic(mapChooseIconRoi, _assets.MapChooseIconGreyMatList[i], null, threshold);

            foreach (var relativeIconRect in iconRects.OrderBy(x => x.Y))
            {
                var iconRect = new Rect(
                    _assets.MapChooseIconRoi.X + relativeIconRect.X,
                    _assets.MapChooseIconRoi.Y + relativeIconRect.Y,
                    relativeIconRect.Width,
                    relativeIconRect.Height);
                if (HasSameMapChooseCandidate(candidates, iconRect))
                {
                    continue;
                }

                var textRect = new Rect(iconRect.X + iconRect.Width, iconRect.Y - 8, 320, iconRect.Height + 16).ClampTo(imageRegion.SrcMat);
                if (textRect.Width <= 0 || textRect.Height <= 0)
                {
                    continue;
                }

                using var textRa = imageRegion.DeriveCrop(textRect);
                using var textRegion = textRa.Find(new RecognitionObject
                {
                    RecognitionType = isHdrCapture ? RecognitionTypes.Ocr : RecognitionTypes.ColorRangeAndOcr,
                    LowerColor = new Scalar(249, 249, 249), // 只取白色文字
                    UpperColor = new Scalar(255, 255, 255),
                });
                var text = CleanCandidateText(textRegion.Text);
                if (string.IsNullOrEmpty(text) || text.Length == 1)
                {
                    continue;
                }

                var clickRect = new Rect(textRect.X, textRect.Y, Math.Min(textRect.Width, 220), textRect.Height).ClampTo(imageRegion.SrcMat);
                candidates.Add(new MapChooseCandidate
                {
                    IconFileName = iconFileName,
                    IconType = iconType,
                    Text = text,
                    IconRect = iconRect,
                    TextRect = textRect,
                    ClickRect = clickRect,
                });
            }
        }

        var orderedCandidates = candidates.OrderBy(x => x.IconRect.Y).ToList();
        for (var i = 0; i < orderedCandidates.Count; i++)
        {
            orderedCandidates[i].Index = i + 1;
            orderedCandidates[i].SelectedIndicatorScore = GetMapChooseSelectedIndicatorScore(imageRegion, orderedCandidates[i]);
        }

        return orderedCandidates;
    }

    private MapChooseCandidate? ChooseMapCandidate(
        List<MapChooseCandidate> candidates,
        GiTpPosition? targetTp)
    {
        var exactNameCandidate = ChooseMapCandidateByExactName(candidates, targetTp);
        if (exactNameCandidate != null)
        {
            return exactNameCandidate;
        }

        var highlighted = ChooseMapCandidateByHighlight(candidates);
        var compatibleCandidates = targetTp == null
            ? new List<MapChooseCandidate>()
            : candidates.Where(x => IsCandidateCompatibleWithTarget(x, targetTp)).ToList();

        MapChooseCandidate? chosen = null;
        if (highlighted != null && IsCandidateCompatibleWithTarget(highlighted, targetTp))
        {
            chosen = highlighted;
        }

        if (chosen == null && compatibleCandidates.Count == 1)
        {
            chosen = compatibleCandidates[0];
        }

        if (chosen == null && highlighted != null)
        {
            chosen = highlighted;
        }

        if (chosen == null)
        {
            chosen = ChooseUniqueCompatibleMapCandidate(candidates, targetTp);
        }

        if (chosen == null)
        {
            chosen = candidates.OrderBy(x => x.IconRect.Y).FirstOrDefault();
        }

        return chosen;
    }

    private static MapChooseCandidate? ChooseMapCandidateByExactName(List<MapChooseCandidate> candidates, GiTpPosition? targetTp)
    {
        if (targetTp == null)
        {
            return null;
        }

        var targetName = NormalizeCandidateText(targetTp.Name ?? string.Empty);
        if (string.IsNullOrEmpty(targetName))
        {
            return null;
        }

        var exactNameCandidates = candidates
            .Where(x => NormalizeCandidateText(x.Text) == targetName)
            .ToList();
        return exactNameCandidates.Count == 1
            ? exactNameCandidates[0]
            : null;
    }

    private static MapChooseCandidate? ChooseMapCandidateByHighlight(List<MapChooseCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        const int minSelectedScore = 18;
        const int minScoreGap = 10;
        var ordered = candidates.OrderByDescending(x => x.SelectedIndicatorScore).ToList();
        var best = ordered[0];
        var secondScore = ordered.Count > 1 ? ordered[1].SelectedIndicatorScore : 0;
        return best.SelectedIndicatorScore >= minSelectedScore &&
               best.SelectedIndicatorScore >= secondScore + minScoreGap
            ? best
            : null;
    }

    private static MapChooseCandidate? ChooseUniqueCompatibleMapCandidate(List<MapChooseCandidate> candidates, GiTpPosition? targetTp)
    {
        if (targetTp == null)
        {
            return null;
        }

        var compatibleCandidates = candidates.Where(x => IsCandidateCompatibleWithTarget(x, targetTp)).ToList();
        if (compatibleCandidates.Count == 1)
        {
            return compatibleCandidates[0];
        }

        return null;
    }

    private static bool IsCandidateCompatibleWithTarget(MapChooseCandidate candidate, GiTpPosition? targetTp)
    {
        if (targetTp == null)
        {
            return true;
        }

        var targetType = targetTp.Type ?? string.Empty;
        if (!string.IsNullOrEmpty(targetType) && IsMapChooseIconTypeMatch(candidate.IconType, targetType))
        {
            return true;
        }

        var optionText = NormalizeCandidateText(candidate.Text);
        var targetName = NormalizeCandidateText(targetTp.Name ?? string.Empty);
        return !string.IsNullOrEmpty(optionText) &&
               !string.IsNullOrEmpty(targetName) &&
               (optionText == targetName || optionText.Contains(targetName) || targetName.Contains(optionText));
    }

    private static bool IsMapChooseIconTypeMatch(string iconType, string targetType)
    {
        if (string.IsNullOrEmpty(iconType) || string.IsNullOrEmpty(targetType))
        {
            return false;
        }

        return iconType switch
        {
            "Domain" => targetType is "OneTimeDomain" or "BlessDomain" or "ForgeryDomain" or "MasteryDomain" or "TrounceDomain",
            _ => string.Equals(iconType, targetType, StringComparison.Ordinal)
        };
    }

    private static string GetMapChooseIconFileName(RecognitionObject recognitionObject)
    {
        return (recognitionObject.Name ?? string.Empty).Replace("MapChooseIcon", string.Empty);
    }

    private static string GetMapChooseIconType(string iconFileName)
    {
        return iconFileName switch
        {
            "TeleportWaypoint.png" => "TeleportWaypoint",
            "StatueOfTheSeven.png" => "Goddess",
            "Domain.png" or "Domain2.png" => "Domain",
            "ObsidianTotemPole.png" => "NatlanObsidianTotemPole",
            "NodKraiMeetingPoint.png" => "NodKraiMeetingPoint",
            _ => iconFileName.Replace(".png", string.Empty)
        };
    }

    private static bool HasSameMapChooseCandidate(List<MapChooseCandidate> candidates, Rect iconRect)
    {
        return candidates.Any(candidate =>
            Math.Abs(candidate.IconRect.X - iconRect.X) <= 6 &&
            Math.Abs(candidate.IconRect.Y - iconRect.Y) <= 6);
    }

    private static int GetMapChooseSelectedIndicatorScore(ImageRegion imageRegion, MapChooseCandidate candidate)
    {
        var centerY = candidate.IconRect.Y + candidate.IconRect.Height / 2d;
        var indicatorRect = new Rect(
            candidate.IconRect.X - 70,
            (int)Math.Round(centerY - 24),
            60,
            48).ClampTo(imageRegion.SrcMat);
        if (indicatorRect.Width <= 0 || indicatorRect.Height <= 0)
        {
            return 0;
        }

        using var indicatorMat = new Mat(imageRegion.SrcMat, indicatorRect);
        using var gray = new Mat();
        switch (indicatorMat.Channels())
        {
            case 4:
                Cv2.CvtColor(indicatorMat, gray, ColorConversionCodes.BGRA2GRAY);
                break;
            case 3:
                Cv2.CvtColor(indicatorMat, gray, ColorConversionCodes.BGR2GRAY);
                break;
            default:
                indicatorMat.CopyTo(gray);
                break;
        }

        using var brightMask = new Mat();
        Cv2.Threshold(gray, brightMask, 210, 255, ThresholdTypes.Binary);
        return Cv2.CountNonZero(brightMask);
    }

    private static void ClickMapChooseCandidate(ImageRegion imageRegion, MapChooseCandidate candidate)
    {
        imageRegion.ClickTo(candidate.ClickRect.X, candidate.ClickRect.Y, candidate.ClickRect.Width, candidate.ClickRect.Height);
    }

    private static double GetDistance(double x1, double y1, double x2, double y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static string CleanCandidateText(string text)
    {
        return text.Replace(">", string.Empty).Replace("＞", string.Empty).Trim();
    }

    private static string NormalizeCandidateText(string text)
    {
        var cleaned = CleanCandidateText(text);
        return new string(cleaned.Where(ch => !char.IsWhiteSpace(ch) && ch != '-' && ch != '－' && ch != '·').ToArray());
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static bool IsTaskStopException(Exception exception)
    {
        return exception is NormalEndException or OperationCanceledException;
    }

    /// <summary>
    /// 给定的映射关系可以表示成 (x, y) 对的形式，其中 x 是输入值，y 是输出值
    ///    1 - 1
    ///  0.8 - 2
    ///  0.6 - 3
    ///  0.4 - 4
    ///  0.2 - 5
    ///    0 - 6
    /// y=−5x+6
    /// </summary>
    /// <param name="region"></param>
    /// <returns></returns>
    public double GetBigMapZoomLevel(ImageRegion region)
    {
        var s = Bv.GetBigMapScale(region);
        // 1~6 的缩放等级
        return (-5 * s) + 6;
    }
}

public class MapPositionNotRecognizedException : Exception
{
    public MapPositionNotRecognizedException(string message) : base(message)
    {
    }

    public MapPositionNotRecognizedException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
