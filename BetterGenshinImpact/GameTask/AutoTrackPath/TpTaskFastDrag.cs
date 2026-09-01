using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Recognition.OpenCv.FeatureMatch;
using BetterGenshinImpact.Core.Recognition.OpenCv.Model;
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
using Fischless.GameCapture;

using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using System;
using System.Windows.Forms;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

/// <summary>
/// 传送任务
/// </summary>
public class TpTaskFastDrag
{
    private readonly TpTaskFastDragAssets _assets;
    private readonly SwitchAreaRegionAssets _switchAreaRegionAssets;
    private readonly Rect _captureRect = TaskContext.Instance().SystemInfo.ScaleMax1080PCaptureRect;
    private readonly double _zoomOutMax1080PRatio = TaskContext.Instance().SystemInfo.ZoomOutMax1080PRatio;
    private readonly TpConfig _tpConfig = TaskContext.Instance().Config.TpConfig;
    private readonly TpTaskFastDragConfig _fastDragConfig;
    private readonly string _mapMatchingMethod = TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod;
    private readonly BlessingOfTheWelkinMoonTask _blessingOfTheWelkinMoonTask = new();

    private readonly CancellationToken ct;
    private readonly CultureInfo cultureInfo;
    private readonly IStringLocalizer stringLocalizer;
    private readonly double _screenHeight;

    /// <summary>
    /// MapZoomDistanceForce > 0 时的额外延时系数。
    /// 值越大延时越多，用于低配电脑在传送过程中给渲染留更多时间。
    /// 公式：1 + MapZoomDistanceForce * 0.2，例如 force=1 → 1.2×, force=5 → 2.0×
    /// </summary>
    private readonly double _extraDelayFactor = 1.0;

    // 分层先验区块限定匹配（teleport-bigmap-position-region-constrained-match spec）：
    // 单次传送生命周期内有效。仅 TpOnce 路径设置；其它调用方（七天神像/地脉花）不设 → 保持 null → 走全图旧路径。
    private Point2f? _miniMapPriorGenshin = null;   // 第一层先验（原神坐标），TpOnce 打开大地图前采集
    private Point2f? _targetPriorGenshin = null;    // 第二层先验（原神坐标），= nTpPoints[0]
    // 上一次传送的目标坐标（原神坐标），用于第二层先验。
    // 必须 static：PathExecutor 每次传送都 new TpTask，实例字段无法跨传送保留。
    private static Point2f? _lastTpTargetGenshin = null;

    // 上次传送成功落地地图名（对标公版 TpTaskOfficial.s_lastSuccessfulTeleportMapName，纯内存不持久化）。
    // 任务结束 finally 清空（teleport-fastdrag-skip-last-successful-map spec，OQ-1/BC-4）。
    private static string? _lastSuccessfulTeleportMapName;

    private bool _priorIsRegionCenter = false;   // 标记当前第一层先验是否为"区域中心点"（切换区域后），是则用 RegionCenterRangeGenshin(200) 而非 Layer1RangeGenshin(100)

    // 拖动滑动窗口先验（teleport 拖动循环专用）：中心=predictedPoint，半径=预测移动距离*2，跟随拖动前移。
    // 非 null 时 GetBigMapCenterPoint 优先走此动态先验；匹配失败降级全图。与三层先验字段互斥使用。
    private Point2f? _dragPriorCenterGenshin = null;
    private double _dragPriorRadiusGenshin = 0;

    /// <summary>拖动结束时保存的最终中心点，供步骤 6 点击坐标反推时做拖动先验。</summary>
    private Point2f? _lastDragCenterGenshin = null;

    /// <summary>
    /// 直接通过缩放比例按钮计算放大按钮的Y坐标
    /// </summary>
    private readonly int _zoomInButtonY = TaskContext.Instance().Config.TpConfig.ZoomStartY - 24; //  y-coordinate for zoom-in button  = _zoomStartY - 24

    /// <summary>
    /// 直接通过缩放比例按钮计算缩小按钮的Y坐标
    /// </summary>
    private readonly int _zoomOutButtonY = TaskContext.Instance().Config.TpConfig.ZoomEndY + 24; //  y-coordinate for zoom-out button = _zoomEndY + 24

    private const double DisplayTpPointZoomLevel = 4.4; // 传送点显示的时候的地图比例（默认地图）
    private const double MoonCanonDisplayTpPointZoomLevel = 3.0; // 霜月传送点仅在 ≤3.0 渲染
    private const int MoonCanonExtraBigMapRenderMs = 4600; // 霜月大地图渲染更慢，额外等待（对齐公版打开超时 7000ms）

    /// <summary>
    /// 传送点显示/可点击的缩放上限：霜月地图为 3.0（传送点仅在 ≤3.0 渲染），其余地图为默认 4.4。
    /// 对所有非霜月地图返回值与旧常量逐字节相同，保证既有地图零回归。
    /// </summary>
    private static double GetDisplayTpPointZoomLevel(string? mapName)
        => string.Equals(mapName, MapTypes.MoonCanon.ToString(), StringComparison.Ordinal)
            ? MoonCanonDisplayTpPointZoomLevel
            : DisplayTpPointZoomLevel;

    /// <summary>
    /// 大地图打开后等待渲染的额外容忍时长：霜月地图 +4600ms（大图渲染慢，对齐公版 7000ms 打开超时），其余地图 0。
    /// 仅影响霜月，其余地图零回归。
    /// </summary>
    private static int GetExtraBigMapRenderMs(string? mapName)
        => string.Equals(mapName, MapTypes.MoonCanon.ToString(), StringComparison.Ordinal)
            ? MoonCanonExtraBigMapRenderMs
            : 0;

    /// <summary>
    /// 动态跑道模式自校准：MoveMouseBy 相对移动的"实际物理位移 ÷ 意图 pixelDelta"比值。
    /// 该比值随系统 DPI / 鼠标设置漂移（实测 dpi2→1.0，dpi2.5→1.225），无法用公式可靠预测，
    /// 故由 MouseMoveMap 每次拖动用 GetCursorPos 前后差实测校准（EMA 平滑），MoveMapTo 算跑道时消费。
    /// 0 表示尚未校准（用 max(1, dpi/2) 作初值）。单线程传送场景，普通 static double 足够。
    /// 详见 .kiro/specs/teleport-drag-edge-aware-runway-clamp/。
    /// </summary>
    private static double _dragMoveAmplifyRatio = 0;

    public TpTaskFastDrag(CancellationToken ct)
    {
        this.ct = ct;
        _fastDragConfig = TaskContext.Instance().Config.TpTaskFastDragConfig;
        _assets = TpTaskFastDragAssets.Get(_captureRect.Width, _captureRect.Height);
        _switchAreaRegionAssets = SwitchAreaRegionAssets.Get(_captureRect.Width, _captureRect.Height);
        TpTaskParam param = new TpTaskParam();
        this.cultureInfo = param.GameCultureInfo;
        this.stringLocalizer = param.StringLocalizer;
        // 初始化全局参数
        var gameHandle = TaskContext.Instance().GameHandle;
        var gameScreen = Screen.FromHandle(gameHandle);
        var gameScreenBounds = gameScreen.Bounds;
        Simulation.SendInput.Mouse.LeftButtonUp();
        // _screenHeight 始终自动适配，MapZoomDistanceForce 不再控制固定倍率，只控制额外延时系数
        _screenHeight = gameScreenBounds.Height > SystemControl.GetGameScreenRect(TaskContext.Instance().GameHandle).Height 
            ? (SystemControl.GetGameScreenRect(TaskContext.Instance().GameHandle).Height <= 1080 ? 3 : 2) 
            : 2.3;
        _extraDelayFactor = _fastDragConfig.MapZoomDistanceForce > 0 ? 1.0 + _fastDragConfig.MapZoomDistanceForce * 0.2 : 1.0;
        
        // 快速拖动 + MapZoomDistanceForce==0 → 动态跑道模式（边缘感知截断）；>0 → 动态跑道 + 额外延时；关 → 经典。
        TaskControl.Logger.LogDebug("屏幕宽高：{gameScreenBounds} 游戏分辨率：{GetGameScreenRect} 传送参数：{screenHeight} 拖动模式={Mode} 额外延时系数={Factor}",
            gameScreenBounds.Size,
            SystemControl.GetGameScreenRect(TaskContext.Instance().GameHandle).Size,
            _screenHeight,
            (_fastDragConfig.MapZoomDistanceForce == 0) ? "动态跑道" : "动态跑道+额外延时",
            _extraDelayFactor);
    }

    /// <summary>
    /// 根据 _extraDelayFactor 缩放延时值。
    /// _extraDelayFactor == 1.0 时返回原值（零开销，MapZoomDistanceForce==0 时的默认行为）。
    /// </summary>
    private int ApplyExtraDelay(int baseDelayMs)
    {
        if (_extraDelayFactor <= 1.0) return baseDelayMs;
        return (int)Math.Round(baseDelayMs * _extraDelayFactor);
    }

    private static RecognitionObject GetQuickTeleportRecognitionObject(string objectName)
    {
        return RecognitionAssets.Get("QuickTeleport", objectName);
    }

    private static RecognitionObject GetQuickTeleportRecognitionObject(string objectName, Region region)
    {
        return RecognitionAssets.Get("QuickTeleport", objectName, region);
    }

    /// <summary>
    /// 传送到七天神像
    /// </summary>
    public async Task TpToStatueOfTheSeven(bool requireLoadingScreen = false)
    {
        await CheckInBigMapUi();

        // 提前调整至恰当的缩放以更快的传送
        using var ra3 = CaptureToRectArea();
        double currentZoomLevel = GetBigMapZoomLevel(ra3);
        if (currentZoomLevel > DisplayTpPointZoomLevel)
        {
            await AdjustMapZoomLevel(currentZoomLevel, DisplayTpPointZoomLevel);
        }
        else if (currentZoomLevel < 3)
        {
            await AdjustMapZoomLevel(currentZoomLevel, 3);
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

        TaskControl.Logger.LogInformation("将传送至 {country} {area} 七天神像", country, area);
        await Tp(x, y, MapTypes.Teyvat.ToString(), false, requireLoadingScreen);
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
        foreach (var (_, goddessPosition) in MapLazyAssets.Get().GoddessPositions)
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
    /// <param name="mapName">目标地图名（用于霜月等大图延长渲染等待；null 走默认时长）</param>
    public async Task OpenBigMapUi(int retryCount = 3, string? mapName = null)
    {
        for (var i = 0; i < retryCount; i++)
        {
            try
            {
                // 打开地图前释放所有按键
                Simulation.ReleaseAllKey();
                await Delay(20, ct);
                await CheckInBigMapUi(i, mapName);
                return;
            }
            catch (Exception e) when (e is NormalEndException || e is TaskCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                if (retryCount > 1)
                {
                    Logger.LogError("打开大地图失败，重试 {I} 次", i + 1);
                    Logger.LogDebug(e, "打开大地图失败，重试 {I} 次", i + 1);
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
    /// <param name="retryTimes">重试次数</param>
    private async Task<(double, double)> TpOnce(double tpX, double tpY, string mapName = "Teyvat", bool force = false, int retryTimes = 0, bool requireLoadingScreen = false, string? fastSyncId = null, bool pullZoomForEdgeRecognition = false)
    {
        // 分层先验：打开大地图【之前】采集小地图当前坐标作第一层先验（原神坐标）。
        // 详见 teleport-bigmap-position-region-constrained-match spec。
        _miniMapPriorGenshin = TryGetMiniMapPriorGenshin(mapName);
        _priorIsRegionCenter = false; // 缓存先验，用第一层标准半径100

        #region 步骤1-2：确认地图界面 + 传送前计算准备
        // 1. 确认在地图界面（传 mapName：霜月大图延长渲染等待）
        await OpenBigMapUi(1, mapName);
        // 2. 传送前的计算准备
        // 获取离目标传送点最近的两个传送点，按距离排序
        var nTpPoints = GetNearestNTpPoints(tpX, tpY, mapName, 2);
        // 获取最近的传送点与区域
        var (x, y, country) = force ? (tpX, tpY, null) : (nTpPoints[0].X, nTpPoints[0].Y, nTpPoints[0].Country);
        // 第二层先验 = 上一次传送的目标坐标（覆盖"上次落地→这次目标"的距离，半径500）
        // 首次传送时 _lastTpTargetGenshin 为 null，第二层先验也为 null，走全图兜底（合理）
        _targetPriorGenshin = _lastTpTargetGenshin;
        var disBetweenTpPoints = Math.Sqrt(Math.Pow(nTpPoints[0].X - nTpPoints[1].X, 2) +
                                           Math.Pow(nTpPoints[0].Y - nTpPoints[1].Y, 2));
        // 确保不会点错传送点的最小缩放，保证至少为 1.0
        var minZoomLevel = Math.Max(disBetweenTpPoints / 30, 1.0);
        // 旧日之海地图在缩放低于 2.0 时画面变暗，导致亮度检测失败触发死循环。
        // 钳制 minZoomLevel 下限为 2.0，确保缩放等级不低于 2.0。
        if (mapName == "SeaOfBygoneEras")
        {
            minZoomLevel = Math.Max(minZoomLevel, 2.0);
        }

        // 点击前"降档目标"缩放（clickZoomLevel）：
        //   缩放数字越大越缩小，越小越放大（相邻传送点在屏幕上越分散）。
        //   步骤4已在 minZoomLevel(旧日之海≥2.0) 把相邻点拉开定位，但边沿重试(5.6)/兜底(5.6b)/
        //   点击前重拖(步骤6) 历史上一律把缩放降回固定的 DisplayTpPointZoomLevel(4.4)——4.4 比
        //   2.0 更"缩小"，会让刚被拉开的相邻点在屏幕上重新挤近，偶发点到隔壁传送点。
        //   仅对旧日之海：降档目标取 Math.Min(4.4, minZoomLevel)，保留步骤4的相邻点间距。
        //     - minZoomLevel=2.0 → Math.Min(4.4,2.0)=2.0：更放大、点更分散，且 2.0≤4.4 图标正常渲染、≥2.0 画面不暗。
        //     - minZoomLevel≥4.4（远距离点）→ Math.Min=4.4：与旧行为一致，远点本就无需额外分散。
        //   非旧日之海：clickZoomLevel = GetDisplayTpPointZoomLevel(mapName)——普通地图仍 4.4（逐字节不变、零回归），
        //   霜月地图 = 3.0（传送点仅在 ≤3.0 渲染，4.4 下点击落在不渲染的空位会失败）。
        double clickZoomLevel = mapName == "SeaOfBygoneEras"
            ? Math.Min(DisplayTpPointZoomLevel, minZoomLevel)
            : GetDisplayTpPointZoomLevel(mapName);

        // 特殊相邻传送点命中判定基准（决策 e）：用最近真实传送点坐标，独立于 force 的 (x,y)。仅取值，无 IO。
        double adjBaseX = nTpPoints[0].X;
        double adjBaseY = nTpPoints[0].Y;

        if (mapName == MapTypes.Teyvat.ToString())
        {
            // 计算传送点位置离哪张地图切换后的中心点最近，切换到该地图
            await SwitchRecentlyCountryMap(x, y, country);
        }
        else if (FastDragAreaSwitchSkipDecisions.ShouldSkipAreaSwitch(retryTimes, _lastSuccessfulTeleportMapName, mapName))
        {
            // teleport-fastdrag-skip-last-successful-map spec（BC-2）：
            // 首次尝试 + 上次传送成功落地同一张非提瓦特图 → 玩家大概率仍在目标图，跳过切区菜单直接识别定位；
            // 识别失败走 MoveMapTo 初始识别失败 → ForceJumpToTargetArea → SwitchArea 兜底补切（既有自救链，BC-6）。
            TaskControl.Logger.LogInformation("快速传送：上次成功落地同图（{Map}），跳过切换地区，直接识别定位", mapName);
        }
        else
        {
            // 直接切换地区
            await SwitchArea(MapTypesExtensions.ParseFromName(mapName).GetDescription());
        }

        if (!await WaitForBigMapUiOrTimeoutAsync(ApplyExtraDelay(2000)))
        {
            Logger.LogWarning("等待大地图界面超时（2000ms），可能地图尚未打开，继续按原逻辑读取缩放级别");
        }
        #endregion

        #region 步骤3：调整初始缩放等级
        Rect bigMapInAllMapRect;
        // 3. 调整初始缩放等级，避免识别中心点失败
        using var ra3 = CaptureToRectArea();
        var zoomLevel = GetBigMapZoomLevel(ra3);
        /* 动态调整缩放逻辑：
            1. 如果当前缩放大于显示传送点级别 -> 缩小
            2. 如果小于配置的最小级别 -> 放大 */
        // 显示档按地图区分：普通地图 4.4（逐字节不变），霜月 3.0（传送点仅在 ≤3.0 渲染）
        var displayZoom = GetDisplayTpPointZoomLevel(mapName);
        if (zoomLevel > displayZoom + _tpConfig.PrecisionThreshold)
        {
            await AdjustMapZoomLevel(zoomLevel, displayZoom);
            zoomLevel = displayZoom;
            TaskControl.Logger.LogInformation("当前缩放等级过大，调整为 {zoomLevel:0.00}", displayZoom);
            bigMapInAllMapRect = GetBigMapRect(mapName);
        }
        else if (zoomLevel < _fastDragConfig.MinZoomLevel - _tpConfig.PrecisionThreshold)
        {
            await AdjustMapZoomLevel(zoomLevel, _fastDragConfig.MinZoomLevel);
            zoomLevel = _fastDragConfig.MinZoomLevel;
            TaskControl.Logger.LogInformation("当前缩放等级过小，调整为 {zoomLevel:0.00}", _fastDragConfig.MinZoomLevel);
            bigMapInAllMapRect = GetBigMapRect(mapName);
        }

        // 3.5 提前白名单判定：决策是否跳过步骤 4（避免重复缩放）
        bool skipStep4DueToSpecial = false;
        try
        {
            var (hitSpecialEarly, specialZoomEarly) = SpecialAdjacentTpPointDecisions.IsSpecialAdjacentPoint(
                GetSpecialAdjacentTpPointList(), adjBaseX, adjBaseY, tolerance: 50.0, defaultZoom: 1.9);
            if (hitSpecialEarly)
            {
                skipStep4DueToSpecial = true;
                TaskControl.Logger.LogInformation(
                    "命中特殊白名单传送点（基准 {X:0},{Y:0}），跳过步骤 4 通用缩放，将在步骤 5.7 统一处理",
                    adjBaseX, adjBaseY);
            }
        }
        catch (Exception ex)
        {
            TaskControl.Logger.LogWarning(ex, "白名单判定异常，按通用流程处理");
            skipStep4DueToSpecial = false;
        }
        #endregion

        #region 步骤4：相近传送点强制缩放定位
        // 4. zoomLevel不满足条件，强制进行一次 MoveMapTo，避免传送点相近导致误点
        if (!skipStep4DueToSpecial && zoomLevel > minZoomLevel)
        {
            TaskControl.Logger.LogInformation("目标传送点有相近传送点，到目标传送点附近将缩放到{zoomLevel:0.00}", minZoomLevel);
            await MoveMapTo(x, y, mapName, minZoomLevel,country);
            // 补检查：MoveMapTo 内部可能因早停（mouseDistance < 收工阈值）跳过缩放，
            // 此处确保缩放真正到位。
            using var raStep4 = CaptureToRectArea();
            double zoomAfterMove = GetBigMapZoomLevel(raStep4);
            if (zoomAfterMove > minZoomLevel + _tpConfig.PrecisionThreshold)
            {
                TaskControl.Logger.LogInformation("步骤4 补缩放：当前缩放 {CZ:0.00} > 目标 {MZ:0.00}，补执行缩放到位", zoomAfterMove, minZoomLevel);
                await AdjustMapZoomLevel(zoomAfterMove, minZoomLevel);
            }
            await WaitMapStableOrTimeoutAsync(ApplyExtraDelay(500)); // fast-drag-recognition-acceleration spec
        }
        #endregion
        
        #region 步骤5：定位传送点到可点击窗口（含点击前缩放回调）
        // 5. 判断传送点是否在当前界面，若否则移动地图
        // await WaitMapStableOrTimeoutAsync(1000,20,5); // fast-drag-recognition-acceleration spec

        // 5.0 点击一次未出现传送点(TpPointNotActivate)后的重试：进入定位循环【之前】就把缩放拉到 5.5，
        //     稳住纳塔↔须弥沙漠边沿"黑色区域"下的大地图位置识别（缩放越大→画面越缩小→单帧特征越多→
        //     GetBigMapRect / IsPointInBigMapWindow 位置匹配越稳）。
        //     必须放在这里而非 MoveMapTo 内：定位循环第一件事是 IsPointInBigMapWindow，若它直接判可点击就
        //     break、根本不调 MoveMapTo，埋在 MoveMapTo 里的拉升会被跳过。
        //     普通传送点在 5.5 下不渲染，但位置判据 IsPointInBigMapWindow 不依赖图标渲染，故定位有效；
        //     点击前(步骤 6 之前)再降回 4.4 恢复渲染。
        // edgeZoomApplied：本次是否为稳定边沿识别把缩放拉到了 5.5。两个触发路径共用：
        //   (A) pullZoomForEdgeRecognition（点击未出现传送点重试，见下方 5.0）；
        //   (B) 定位循环内连续两次"传送点不在当前大地图范围内"（见 do-while 内）。
        // 只要任一路径拉过 5.5，点击前(5.6)就必须降回 4.4 恢复普通传送点渲染，否则会点在不渲染的空位。
        bool edgeZoomApplied = false;

        if (pullZoomForEdgeRecognition)
        {
            using var raEdge = CaptureToRectArea();
            double zoomNowEdge = GetBigMapZoomLevel(raEdge);
            if (Math.Abs(zoomNowEdge - 5.5) > _tpConfig.PrecisionThreshold)
            {
                await AdjustMapZoomLevel(zoomNowEdge, 5.5);
                await Delay(ApplyExtraDelay(200), ct);
                TaskControl.Logger.LogInformation("点击未出现传送点重试：缩放拉到 5.5 稳定地图边沿位置识别");
            }
            edgeZoomApplied = true;
        }

        // 重试时（retryTimes >= 2）强制缩放到 2.0，使传送点图标放大避免被大地图标记物（地脉花等）遮挡。
        // 放在 pullZoomForEdgeRecognition 之后以覆盖其 5.5 拉升，确保点击前缩放到放大状态。
        // 第一次重试（retryTimes == 1）不走 2.0，保留 5.5→4.4 降回逻辑（步骤 5.6），
        // 避免 2.0 下地图放大过多导致 GetBigMapRect 识别精度下降。
        if (retryTimes >= 2)
        {
            using var raRetry = CaptureToRectArea();
            double zoomNowRetry = GetBigMapZoomLevel(raRetry);
            if (Math.Abs(zoomNowRetry - 2.0) > _tpConfig.PrecisionThreshold)
            {
                await AdjustMapZoomLevel(zoomNowRetry, 2.0);
                await Delay(ApplyExtraDelay(200), ct);
                TaskControl.Logger.LogInformation("重试第{Retry}次：缩放拉到 2.0 放大传送点图标避免标记物遮挡（retryTimes>=2 触发）", retryTimes);
            }
            // 重试时手动拉了缩放，需要重新计算 bigMapInAllMapRect
            // 清除 edgeZoomApplied 标志，防止步骤 5.6 又把缩放降回 4.4
            edgeZoomApplied = false;
            bigMapInAllMapRect = GetBigMapRect(mapName);
        }
        else
        {
            bigMapInAllMapRect = GetBigMapRect(mapName);
        }
        var retryCount = 0;
        do
        {
            if (IsPointInBigMapWindow(mapName, bigMapInAllMapRect, x, y, country)) break;
            if (retryCount++ >= 5) // 防止死循环
            {
                TaskControl.Logger.LogWarning("多次尝试未移动到目标传送点，传送失败");
                throw new Exception("多次尝试未移动到目标传送点，传送失败");
            }
            
            TaskControl.Logger.LogInformation("传送点不在当前大地图范围内，重新调整地图位置-1（保持当前缩放，不强制归一到 2.0）");

            // [诊断-定位循环] 打出本轮重进时的实测缩放 + 重试计数。keepCurrentZoom=true 会保留上一轮缩放，
            // 若这里缩放持续偏离 4.4（如停在很放大档），则 do-while 的 Contains 与早停几何判据尺度不一致，
            // 是 livelock 的直接诱因（验证假设3）。
            using (var raLoop = CaptureToRectArea())
            {
                TaskControl.Logger.LogDebug("[诊断-定位循环] retryCount={RC} retryTimes={RT} 目标({X:0},{Y:0}) 进入本轮实测缩放={Z:0.00}",
                    retryCount, retryTimes, x, y, GetBigMapZoomLevel(raLoop));
            }

            // 改法 B：keepCurrentZoom=true，定位循环不强行把缩放归一到 2.0，保留拖动进入时的实际缩放。
            await MoveMapTo(x, y, mapName, 2, country, retryTimes, keepCurrentZoom: true);
            // 加速：等像素稳定（远比连续两次模板匹配 GetBigMapRect 快），稳定后再单次 GetBigMapRect
            // fast-drag-recognition-acceleration spec / design.md §4.2（feedback adjustment）
            await WaitMapStableOrTimeoutAsync(ApplyExtraDelay(1000));
            bigMapInAllMapRect = GetBigMapRect(mapName);
        } while (true);

        // 5.6 若本次任一路径(A 点击未出现传送点重试 / B 连续两次不在范围内)把缩放拉到了 5.5，
        //     点击前降回可点击可见档(4.4)：普通传送点仅在 ≤4.4 渲染，5.5 下点击会点在不渲染的空位。
        //     降档后重算 bigMapInAllMapRect 以得到 4.4 下的点击坐标。
        if (edgeZoomApplied)
        {
            using var raBack = CaptureToRectArea();
            double zoomBack = GetBigMapZoomLevel(raBack);
            // 降档目标由 DisplayTpPointZoomLevel 改为 clickZoomLevel：非旧日之海仍 = 4.4（逐字节不变），
            // 旧日之海 = Math.Min(4.4, minZoomLevel)，避免把步骤4拉开的相邻点重新挤近导致点错隔壁。
            if (Math.Abs(zoomBack - clickZoomLevel) > _tpConfig.PrecisionThreshold)
            {
                await AdjustMapZoomLevel(zoomBack, clickZoomLevel);
                await Delay(ApplyExtraDelay(200), ct);
                TaskControl.Logger.LogInformation("边沿重试：点击前缩放降回 {Z:0.0} 恢复传送点渲染", clickZoomLevel);
                bigMapInAllMapRect = GetBigMapRect(mapName);
            }
        }

        // 5.6b 点击前缩放兜底（Zoom_Collapse_Guard）：无条件确保点击前缩放 ≤ 4.4。
        //     根因：异常/重试路径（TpPointNotActivate 后 5.0 拉 5.5、或经 Tp 外层 catch→ReturnMainUiTask
        //     退回主界面重进后 edgeZoomApplied 已复位为 false 而缩放仍停在 5.5）会绕过 5.6 的降档条件，
        //     使缩放停在 5.5。普通传送点在 5.5 不渲染，点击落在空位 → 传送失败。此兜底仅看实测缩放是否 > 4.4+容差。
        //     位于 5.7 之前：5.7 的 specialZoom(2.0<4.4) 不受影响，且 5.7 命中时会自行重算 rect。
        //     详见 .kiro/specs/teleport-final-click-zoom-not-collapsed-click-miss-fix/design.md 组件 B。
        using var raCollapse = CaptureToRectArea();
        double zoomNowCollapse = GetBigMapZoomLevel(raCollapse);
        // 降档目标由 DisplayTpPointZoomLevel 改为 clickZoomLevel：非旧日之海仍 = 4.4（触发阈值与降档目标逐字节不变），
        // 旧日之海 = Math.Min(4.4, minZoomLevel)（2.0≤4.4，图标正常渲染、画面不暗），保留步骤4相邻点间距。
        if (ShouldCollapseZoomBeforeClick(zoomNowCollapse, clickZoomLevel, _tpConfig.PrecisionThreshold))
        {
            await AdjustMapZoomLevel(zoomNowCollapse, clickZoomLevel);
            await Delay(ApplyExtraDelay(200), ct);
            TaskControl.Logger.LogInformation("点击前缩放兜底：{From:0.0} 高于可点击档，降回 {To:0.0} 恢复传送点渲染", zoomNowCollapse, clickZoomLevel);
            bigMapInAllMapRect = GetBigMapRect(mapName);
        }

        // 5.7 特殊相邻传送点：命中清单则点击前拉到专属放大，并整合步骤 4 的 minZoomLevel
        //     重新 MoveMapTo 定位并重算 bigMapInAllMapRect，使相邻两点在屏幕上分开、点得准、不弹菜单。
        //     命中判定为纯坐标 O(n) 比较（清单懒加载+缓存），未命中零额外开销、逐字节走原路径。
        //     详见 .kiro/specs/teleport-adjacent-point-misclick-zoom-whitelist-fix/design.md §组件 4。
        try
        {
            var (hitSpecial, specialZoom) = SpecialAdjacentTpPointDecisions.IsSpecialAdjacentPoint(
                GetSpecialAdjacentTpPointList(), adjBaseX, adjBaseY, tolerance: 50.0, defaultZoom: 1.5);
            if (hitSpecial)
            {
                // 整合步骤 4 的安全缩放：取 minZoomLevel 和 specialZoom 中更小值（更放大 = 更安全）
                double finalZoom = Math.Min(minZoomLevel, specialZoom);
                
                using var raSp = CaptureToRectArea();
                double zoomNow = GetBigMapZoomLevel(raSp);
                if (Math.Abs(zoomNow - finalZoom) > _tpConfig.PrecisionThreshold)
                {
                    await AdjustMapZoomLevel(zoomNow, finalZoom);
                    await Delay(ApplyExtraDelay(200), ct);
                }
                TaskControl.Logger.LogInformation(
                    "命中特殊相邻传送点，点击前放大到 {FinalZoom:0.00}（minZoom={MinZoom:0.00}, specialZoom={SpecZoom:0.00}）并重新定位（基准 {X:0},{Y:0}）",
                    finalZoom, minZoomLevel, specialZoom, adjBaseX, adjBaseY);
                // 放大后屏幕映射改变，必须重新 MoveMapTo + 重算 bigMapInAllMapRect（决策 d）
                await MoveMapTo(x, y, mapName, finalZoom, country, retryTimes);
                await WaitMapStableOrTimeoutAsync(ApplyExtraDelay(500));
                bigMapInAllMapRect = GetBigMapRect(mapName);
            }
        }
        catch (Exception ex)
        {
            TaskControl.Logger.LogWarning(ex, "步骤 5.7 白名单判定异常");
            // 补偿逻辑：如果步骤 4 被跳过了，这里必须补上安全缩放
            if (skipStep4DueToSpecial && zoomLevel > minZoomLevel)
            {
                TaskControl.Logger.LogInformation("补偿缩放：步骤 4 已跳过但步骤 5.7 失败，使用 minZoomLevel={MinZoom:0.00}", minZoomLevel);
                await MoveMapTo(x, y, mapName, minZoomLevel, country, retryTimes);
                await WaitMapStableOrTimeoutAsync(ApplyExtraDelay(1000));
                bigMapInAllMapRect = GetBigMapRect(mapName);
            }
        }
        #endregion

        #region 步骤6：计算并点击传送点
        // 6. 计算传送点位置并点击
        // 点击前硬校验（teleport-click-outside-game-window-misclick-fix）：
        // step5.6b 降档（更放大）可能把 step5 do-while 判为可点的边缘点推出游戏窗口。
        // 若直接点，多屏环境下越界坐标会打到下方屏幕的其他程序（QQ）→失焦→传送失败。
        // 故点击前用 IsPointInBigMapWindow（含 IsWithinScreen 屏幕内硬边界）复核落点；
        // 不可点则回定位循环重拖（用可点档 4.4，把点拖回窗口中段），有限次后仍不可点则抛
        // 可重试异常走既有 RetryException 流程。正常路径（点本就在窗口内）首轮即通过，零额外开销。
        int clickGuardRetry = 0;
        while (!IsPointInBigMapWindow(mapName, bigMapInAllMapRect, x, y, country))
        {
            if (clickGuardRetry++ >= 3)
            {
                TaskControl.Logger.LogWarning("点击前校验：传送点多次无法拖入可点窗口（可能被降档推出屏幕），放弃本次点击重试");
                throw new RetryException("传送点不在可点击窗口内，重新传送");
            }
            TaskControl.Logger.LogInformation("点击前校验：传送点不在可点窗口内（第 {N} 次），用可点档重新定位", clickGuardRetry);
            // 重拖目标缩放由 DisplayTpPointZoomLevel 改为 clickZoomLevel：非旧日之海仍 = 4.4（逐字节不变），
            // 旧日之海 = Math.Min(4.4, minZoomLevel)，与 5.6/5.6b 一致，避免重拖把相邻点重新挤近。
            await MoveMapTo(x, y, mapName, clickZoomLevel, country, retryTimes, keepCurrentZoom: false);
            await WaitMapStableOrTimeoutAsync(ApplyExtraDelay(1000));
            bigMapInAllMapRect = GetBigMapRect(mapName);
        }

        // Debug.WriteLine($"({x},{y}) 在 {bigMapInAllMapRect} 内，计算它在窗体内的位置");
        // 注意这个坐标的原点是中心区域某个点，所以要转换一下点击坐标（点击坐标是左上角为原点的坐标系），不能只是缩放
        var (clickX, clickY) = ConvertToGameRegionPosition(mapName, bigMapInAllMapRect, x, y);
        using var ra4 = CaptureToRectArea();
        // [点击诊断] 仅旧日之海（门控，避免对其它地图每次点击多跑一次 GetBigMapZoomLevel 识别）：
        //   记录"点击那一刻"的实测缩放 + 定位矩形 + 目标点(原神) + 计算落点，供偶发误点复现时归因。
        if (mapName == "SeaOfBygoneEras")
        {
            Logger.LogDebug(
                "[点击诊断] map={Map} retry={Retry} 实测缩放={Zoom:0.00} 定位矩形={Rect} 目标点(原神)=({X:0},{Y:0}) 计算落点=({CX:0},{CY:0})",
                mapName, retryTimes, GetBigMapZoomLevel(ra4), bigMapInAllMapRect, x, y, clickX, clickY);
        }

        ra4.ClickTo((int)clickX, (int)clickY-12);
        #endregion

        #region 步骤7-8：触发快速传送 + 等待完成
        // 7. 触发一次快速传送功能
        // 加速 + 容错：popup 探测立即点 + IsLoadingScreen 终判 + 失败重点最多 3 次
        // 已进入传送加载页 → 直接返回，跳过 ClickTpPoint（避免重复点击）
        // 未进入 → 走 ClickTpPoint 兜底（旧行为）
        // fast-drag-recognition-acceleration spec / final click pre-stop optimization v2
        bool entered = await FastClickTeleportButtonAsync();
        if (!entered)
        {
            using var ra1 = CaptureToRectArea();
            await ClickTpPoint(ra1);
        }

        // 8. 等待传送完成
        await WaitForTeleportCompletion(50, 1200, requireLoadingScreen, fastSyncId);
        // 保存本次传送的目标坐标，供下次传送的第二层先验使用
        _lastTpTargetGenshin = new Point2f((float)x, (float)y);
        // 记录本次传送成功落地地图（含 Teyvat 也记录；跳过判据内部排除 Teyvat）
        _lastSuccessfulTeleportMapName = mapName;
        return (x, y);
        #endregion
    }

    /// <summary>
    ///     检查传送是否完成，未完成则等待
    /// </summary>
    /// <param name="maxAttempts">最大检查延时的次数</param>
    /// <param name="delayMs">如果未完成加载，检查加载页面的延时。</param>
    /// <param name="requireLoadingScreen">
    ///     当为 true 时启用阶段 1：先在 6s 内每 200ms 观察一次传送过渡页，
    ///     避免“开大地图被打死→复苏到神像→派蒙可见→误判传送成功”。
    /// </param>
    private async Task WaitForTeleportCompletion(int maxAttempts, int delayMs, bool requireLoadingScreen = false, string? fastSyncId = null)
    {
        // === 阶段 1（仅当调用方传入 requireLoadingScreen=true）===
        if (requireLoadingScreen)
        {
            bool seen = await WaitForLoadingScreenAsync(timeoutMs: 6000, intervalMs: 200, fastSyncId: fastSyncId);
            if (!seen)
            {
                TaskControl.Logger.LogWarning("[传送] 未观察到传送过渡页，疑似传送被打断（点击传送后角色可能已倒地/被打断）");
                throw new TeleportLoadingTimeoutException("阶段 1 在 6s 内未观察到传送过渡页");
            }
            else
            {
                TaskControl.Logger.LogInformation("[传送] 观察到传送过渡页，继续等待传送完成");
            }
        }

        // === 阶段 2（保持原行为 + 增加复苏弹窗检测）===

        await Delay(delayMs, ct);
        for (var i = 0; i < maxAttempts; i++)
        {
            using var capture = CaptureToRectArea();

            // 阶段 2 复苏弹窗检测（requireLoadingScreen 路径，防御阶段 1 之后才出现弹窗的罕见场景）
            if (requireLoadingScreen && Bv.IsInRevivePrompt(capture))
            {
                TaskControl.Logger.LogWarning("[传送] 传送过程中检测到复苏弹窗（阶段 2），疑似传送失败 + 角色死亡");
                // 抛超时让上层重试。
                throw new TeleportLoadingTimeoutException("传送中检测到复苏弹窗，传送失败");
            }

            if (Bv.IsInMainUi(capture))
            {
                TaskControl.Logger.LogInformation("传送完成，返回主界面");
                return;
            }
            //增加容错，小概率情况下碰到，前面点击传送失败
            capture.Find(_assets.TeleportButtonRo, rg => rg.Click());
            await Delay(delayMs, ct);
            // 打开大地图期间推送的月卡会在传送之后直接显示，导致检测不到传送完成。
            await _blessingOfTheWelkinMoonTask.Start(ct);
        }

        TaskControl.Logger.LogWarning("传送等待超时，换台电脑吧");
    }

    /// <summary>
    /// 阶段 1：在 timeoutMs 内每 intervalMs 截图判断一次过渡页是否出现。
    /// 命中 → 返回 true；超时 → 返回 false。
    ///
    /// 暂停 / 网络断开兜底：循环顶部检测 IsSuspend || IsSuspendedByNetwork 任一为 true 时
    /// 早退 return true，让阶段 2 接管。原因：墙钟 deadline 在暂停期间继续累积，
    /// 不早退会导致解除暂停后立即超时误抛异常（网络断开检测在公版本机化为恒 false，见
    /// TpTeleportSuspendDetector 类注释）。
    ///
    /// fastSyncId：预留的调用栈透传参数（茶包联机抢报用）；公版调用方恒传 null，路径完全短路。
    /// </summary>
    private async Task<bool> WaitForLoadingScreenAsync(int timeoutMs, int intervalMs, string? fastSyncId = null)
    {
        long deadline = Environment.TickCount + timeoutMs;
        while (Environment.TickCount < deadline)
        {
            ct.ThrowIfCancellationRequested();

            // 暂停 / 网络断开早退：避免墙钟超时误判（cancel 优先级更高，已在上一行处理）
            if (TeleportLoadingPhaseSuspendGuard.ShouldSkip(
                    RunnerContext.Instance.IsSuspend,
                    TpTeleportSuspendDetector.IsSuspendedByNetwork))
            {
                TaskControl.Logger.LogInformation("[传送] 检测到暂停/网络断开，跳过传送过渡页守卫，回退原判据");
                return true;
            }

            using var capture = CaptureToRectArea();

            // 复苏弹窗优先于过渡页判定（在过渡页之前的瞬间，复苏弹窗已经显示）
            if (Bv.IsInRevivePrompt(capture))
            {
                TaskControl.Logger.LogWarning("[传送] 传送过程中检测到复苏弹窗（阶段 1），疑似传送失败 + 角色死亡");
                // 抛超时让上层重试。
                throw new TeleportLoadingTimeoutException("传送中检测到复苏弹窗，传送失败");
            }

            if (TeleportLoadingDetector.IsLoadingScreen(capture.SrcMat))
            {
                return true;
            }
            await Delay(intervalMs, ct);
        }
        return false;
    }

    /// <summary>
    /// 传送点是否在大地图窗口内
    /// </summary>
    /// <param name="mapName"></param>
    /// <param name="bigMapInAllMapRect">大地图在整个游戏地图中的矩形位置（原神坐标系）</param>
    /// <param name="x">传送点x坐标（原神坐标系）</param>
    /// <param name="y">传送点y坐标（原神坐标系）</param>
    /// <returns></returns>
    private bool IsPointInBigMapWindow(string mapName, Rect bigMapInAllMapRect, double x, double y, string? country = null)
    {
        // 坐标不包含直接返回
        if (!bigMapInAllMapRect.Contains(x, y))
        {
            // [诊断] 区分"范围判据(Contains)挂" vs "安全区判据(IsClickable)挂"：这里是 Contains 挂。
            // 打出目标点与矩形，若目标点明显在矩形外→地图确实没拖到位；若擦边→矩形/缩放尺度问题。
            double rectCX = bigMapInAllMapRect.X + bigMapInAllMapRect.Width / 2.0;
            double rectCY = bigMapInAllMapRect.Y + bigMapInAllMapRect.Height / 2.0;
            Logger.LogDebug("[诊断-范围判据] Contains=false 目标点({X:0},{Y:0}) 不在矩形内 rect=[X={RX:0} Y={RY:0} W={RW:0} H={RH:0}] 矩形中心=({CX:0},{CY:0}) 目标距矩心=({DX:0},{DY:0})",
                x, y, bigMapInAllMapRect.X, bigMapInAllMapRect.Y, bigMapInAllMapRect.Width, bigMapInAllMapRect.Height,
                rectCX, rectCY, x - rectCX, y - rectCY);
            return false;
        }

        var (clickX, clickY) = ConvertToGameRegionPosition(mapName, bigMapInAllMapRect, x, y);
        // 用五个精确 UI 危险区矩形替换旧的"左上 360×400 + 四周 115 圈"粗糙屏蔽。
        // 命中任一 UI 矩形 → 危险（继续 MoveMapTo 避让）；否则可点击（含边缘中段）。
        // 详见 .kiro/specs/teleport-drag-corner-ui-safezone-clamp/。
        bool withinScreen = TeleportClickSafeZone.IsWithinScreen(clickX, clickY, _zoomOutMax1080PRatio);
        bool inDanger = TeleportClickSafeZone.IsInDangerZone(clickX, clickY, _zoomOutMax1080PRatio, country);
        bool isClickable = withinScreen && !inDanger;
        // [诊断] Contains 已通过，走到 IsClickable。拆开 withinScreen / inDanger 两个子判据，
        // 明确到底是"越出屏幕"还是"落在 UI 危险矩形"导致不可点，便于对照早停几何判据。
        Logger.LogDebug("[IsPointInBigMapWindow] 传送点({X:0},{Y:0}) 计算点击位置=({ClickX:0},{ClickY:0}) 安全区判定={IsClickable} (屏幕内={Within} 危险区={Danger}) ratio={Ratio:0.00}",
            x, y, clickX, clickY, isClickable, withinScreen, inDanger, _zoomOutMax1080PRatio);
        return isClickable;
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
        var (picX, picY) = MapManager.GetMap(mapName, _mapMatchingMethod).ConvertGenshinMapCoordinatesToImageCoordinates(new Point2f((float)x, (float)y));
        var picRect = MapManager.GetMap(mapName, _mapMatchingMethod).ConvertGenshinMapCoordinatesToImageCoordinates(bigMapInAllMapRect);
        Debug.WriteLine($"({picX},{picY}) 在 {picRect} 内，计算它在窗体内的位置");
        var clickX = (picX - picRect.X) / picRect.Width * _captureRect.Width;
        var clickY = (picY - picRect.Y) / picRect.Height * _captureRect.Height;
        return (clickX, clickY);
    }

    public async Task CheckInBigMapUi(int retryCount = 0, string? mapName = null)
    {
        // 尝试打开地图失败后，先回到主界面后再次尝试打开地图
        if (!await TryToOpenBigMapUi(retryCount, mapName))
        {
            await new ReturnMainUiTask().Start(ct);
            await Delay(ApplyExtraDelay(500), ct);
            if (!await TryToOpenBigMapUi(retryCount, mapName))
            {
                throw new RetryException("打开大地图失败，请检查按键绑定中「打开地图」按键设置是否和原神游戏中一致！");
            }
        }
    }

    /// <summary>
    /// 尝试打开地图界面
    /// </summary>
    private async Task<bool> TryToOpenBigMapUi(int retryCount = 0, string? mapName = null)
    {
        // M 打开地图识别当前位置，中心点为当前位置
        using (var ra1 = CaptureToRectArea())
        {
            if (IsInBigMapUiViaAssets(ra1))
            {
                return true;
            }
        }

        // 重按 M 机制（teleport-open-bigmap-repress-on-swallow）：
        //   根因——传送落地后主界面判定(IsInMainUi)通过时，游戏往往还差一点渲染，此刻按 M 会被吞，
        //   旧逻辑要空烧 2800ms 等待 + 1500ms 空轮询(不重按) ≈ 4.3s 才判失败，再靠上层回主界面兜底。
        //
        //   关键：M 是开关键，若第一次已生效再按一次会把正在打开的地图【关掉】，所以重按判据必须可靠。
        //   把两件时间尺度完全不同的事拆成两个独立时间，避免"参数越大重按越慢"的错误耦合：
        //     ① 被吞探测 swallowProbeMs（固定，不随参数缩放）：按 M 后只探测"是否离开主界面"。
        //        —— 只要离开主界面（哪怕地图还没渲染出来、处于过渡态）→ 按键已生效 → 立即【提交】进入②，
        //           绝不再按 M（地图渲染多慢都行）。
        //        —— 若在 swallowProbeMs 内【一直赖在主界面】（派蒙菜单没消失）→ 判定被吞 → 重按。
        //        它只需覆盖"主界面菜单消失"的延迟（快），不需要覆盖"地图完全渲染"（慢），所以短。
        //     ② 渲染容忍 renderToleranceMs（随 MapZoomDistanceForce 缩放）：离开主界面后耐心等大地图出现，
        //        轮询命中即返回（正常路径零额外延时）。慢机器调高"传送整体识别延时"在这里起作用，不拖慢①。
        //
        //   maxPress 次内仍未成功 → return false，交回 CheckInBigMapUi 既有兜底（回主界面 + 延时 + 再试一轮）。
        const int maxPress = 3;
        const int swallowProbeMs = 400;                              // 固定：主界面消失延迟上限（≠地图渲染时间）
        int renderToleranceMs = ApplyExtraDelay(1500 + retryCount * 200) + GetExtraBigMapRenderMs(mapName); // 随参数缩放：地图渲染耐心（霜月大图额外+4600ms）
        int pressCount = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            Simulation.SendInput.SimulateAction(GIActions.OpenMap);
            pressCount++;

            // ── 阶段①：被吞探测。轮询到"离开主界面"或"大地图出现"即结束；一直在主界面到超时 = 被吞。
            bool leftMainUi = false;
            long probeDeadline = Environment.TickCount + swallowProbeMs;
            while (Environment.TickCount < probeDeadline)
            {
                ct.ThrowIfCancellationRequested();
                using var raP = CaptureToRectArea();
                if (IsInBigMapUiViaAssets(raP))
                {
                    return true; // 已经开好了
                }
                if (!Bv.IsInMainUi(raP))
                {
                    leftMainUi = true; // 主界面菜单已消失 → 按键生效，提交进入②
                    break;
                }
                await Delay(10, ct);
            }

            if (!leftMainUi)
            {
                // 一直在主界面 → M 被吞
                if (pressCount >= maxPress)
                {
                    Logger.LogWarning("按 M {PressCount} 次、每次探测 {ProbeMs}ms 仍在主界面，疑似按键持续被吞，交回上层兜底", pressCount, swallowProbeMs);
                    return false;
                }
                // Logger.LogWarning("按 M 后 {ProbeMs}ms 仍在主界面（已按 {PressCount} 次），疑似落地渲染未就绪按键被吞，立即重按", swallowProbeMs, pressCount);
                continue; // 立即重按
            }

            // ── 阶段②：已离开主界面（按键生效），提交耐心等待地图渲染，绝不重按（避免 M 把地图关掉）
            if (await WaitForBigMapUiOrTimeoutAsync(renderToleranceMs))
            {
                return true;
            }

            // 渲染容忍超时仍没进大地图，判断是"回落主界面"还是"仍卡过渡态"
            using var raEnd = CaptureToRectArea();
            // [诊断] 超时后分别用宽松(IsInBigMapUi, OR双判据)与严格(MapScaleButtonRo含ROI)判据探测，
            // 定位"WaitForBigMapUiOrTimeoutAsync 超时但 IsInBigMapUi 误报 true"是否发生。
            bool looseInBigMap = IsInBigMapUiViaAssets(raEnd);
            bool strictScaleButton = raEnd.Find(TpTaskFastDragAssets.Get(raEnd).MapScaleButtonRo).IsExist();
            bool strictSettingsInterpreted = looseInBigMap && !strictScaleButton;
            Logger.LogDebug(
                "[尝试直通-诊断] 渲染超时后：宽松IsInBigMapUi={Loose} 严格ScaleButton={Strict} 仅Settings判为真={OnlySettings}",
                looseInBigMap, strictScaleButton, strictSettingsInterpreted);
            if (strictScaleButton)
            {
                // 缩放按钮(含ROI)也被识别到 → 大地图真正可操作，返回 true（严格确认，防过渡态误报）
                return true;
            }
            if (Bv.IsInMainUi(raEnd))
            {
                // 离开主界面后又回落（罕见：地图没开成）→ 按需重按
                if (pressCount >= maxPress)
                {
                    Logger.LogWarning("离开主界面后又回落主界面，已按 {PressCount} 次，交回上层兜底", pressCount);
                    return false;
                }
                Logger.LogWarning("离开主界面后又回落主界面（已按 {PressCount} 次），重按打开地图", pressCount);
                continue;
            }
            // 仍卡在过渡态：避免死等，交回上层兜底（低配机应调高"传送整体识别延时"放大渲染容忍）
            Logger.LogWarning("按 M {PressCount} 次、等待渲染 {Ms}ms 后仍未进入大地图，交回上层兜底", pressCount, renderToleranceMs);
            return false;
        }
    }

    /// <summary>
    /// 加速识别模式：按 M 后轮询等大地图 UI 出现即返回（单判据）。
    /// 之前为了防"地图特征点未渲染→走 SwitchArea 弯路"加过双判据，但用户实测：
    /// 双判据导致每次都顿一下；旧版本（无特征点判据）也不是每次都走 SwitchArea。
    /// 改回单判据后，"特征点识别"由下游 SwitchRecentlyCountryMap 入口的 3×100ms retry 兜底
    /// （见 SwitchRecentlyCountryMap 注释）。最坏 ~300ms 仍能识别成功，避免误走 SwitchArea。
    ///
    /// fast-drag-recognition-acceleration spec / step 1 boot delay optimization (single criterion)
    /// </summary>
    private async Task<bool> WaitForBigMapUiOrTimeoutAsync(int timeoutMs, int pollMs = 10)
    {
        long deadline = Environment.TickCount + timeoutMs;
        while (Environment.TickCount < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var ra = CaptureToRectArea();
                if (ra.Find(TpTaskFastDragAssets.Get(ra).MapScaleButtonRo).IsExist())
                {
                    await Delay(10, ct);
                    return true;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogDebug("[快速识别] OpenBigMapUi 探测异常: {Msg}", ex.Message);
            }
            await Delay(pollMs, ct);
        }
        Logger.LogWarning("等待大地图界面超时（{TimeoutMs}ms），可能地图尚未打开", timeoutMs);
        return false;
    }

    /// <summary>
    /// 加速识别模式：轮询等指定 RecognitionObject 出现，超时兜底。
    /// 主要用于"等弹窗 / 菜单出现"场景（如 SwitchArea 等地区菜单的白色 X 关闭按钮）。
    /// fast-drag-recognition-acceleration spec
    /// </summary>
    private async Task<bool> WaitForElementOrTimeoutAsync(RecognitionObject ro, int timeoutMs, int pollMs = 15)
    {
        long deadline = Environment.TickCount + timeoutMs;
        while (Environment.TickCount < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var ra = CaptureToRectArea();
                using var found = ra.Find(ro);
                if (found.IsExist())
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogDebug("[快速识别] WaitForElement 探测异常 {Name}: {Msg}", ro.Name, ex.Message);
            }
            await Delay(pollMs, ct);
        }
        return false;
    }


    public async Task<(double, double)> Tp(double tpX, double tpY, string mapName = "Teyvat", bool force = false, bool requireLoadingScreen = false, string? fastSyncId = null)
    {
        // 仅当"点击后选项列表没有传送点(TpPointNotActivate)"时，下一次重试才把缩放拉到 5.5 稳定边沿识别。
        // 用专门标志而非笼统的 retryTimes>=1：后者会把"地图识别失败/亮度过低"等根本没点击过的失败也误判为
        // "点击后没出现传送点"，导致无关失败也拉 5.5。
        bool lastWasTpPointNotActivate = false;
        for (var i = 0; i < 3; i++)
        {
            try
            {
                return await TpOnce(tpX, tpY, mapName, force, i, requireLoadingScreen, fastSyncId, lastWasTpPointNotActivate);
            }
            catch (TpPointNotActivate e)
            {
                lastWasTpPointNotActivate = true;
                // 传送点未激活或不存在 按ESC回到大地图界面
                Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
                await Delay(300, ct);
                TaskControl.Logger.LogWarning(e.Message + "  重试");
            }
            catch (Exception e) when (e is NormalEndException || e is TaskCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                lastWasTpPointNotActivate = false; // 非"点击后没传送点"的失败，下次重试不拉 5.5
                TaskControl.Logger.LogError("传送失败，重试 {I} 次，原因：{Msg}", i + 1, e.Message);
                TaskControl.Logger.LogDebug(e, "传送失败异常详情（重试 {I} 次）", i + 1);
                Simulation.SendInput.Mouse.LeftButtonUp();
                //回到主界面，重置状态
                await new ReturnMainUiTask().Start(ct);
                await Delay(1000, ct);
            }
        }

        throw new InvalidOperationException("传送失败");
    }

    /// <summary>
    /// 任务结束 finally 调用：清空 "上次成功传送地图名"。
    /// 跨任务自动保守（首传走切区，BC-1）；非法关闭进程态自然消逝（不持久化，BC-4）。
    /// </summary>
    internal static void ResetLastSuccessfulTeleportMap() => _lastSuccessfulTeleportMapName = null;

    /// <summary>
    /// 移动地图到指定传送点位置
    /// 可能会移动不对，所以可以重试此方法
    /// </summary>
    /// <param name="x">目标x坐标</param>
    /// <param name="y">目标y坐标</param>
    /// <param name="mapName">地图名称</param>
    /// <param name="finalZoomLevel">到达目标点的最小缩放等级（茶包快速拖动模式下无条件生效；keepCurrentZoom=true 时仅作放大下限）</param>
    /// <param name="country">传送地图国家</param>
    /// <param name="retryTimes">重试次数</param>
    /// <param name="enableEarlyStop">是否启用早停机制（几何早停和容差早停）。默认为 true 保持向后兼容，设为 false 时将精确拖动到目标点正中心</param>
    public async Task MoveMapTo(double x, double y, string mapName, double finalZoomLevel = 2, string? country = null, int retryTimes = 0, bool enableEarlyStop = true, bool keepCurrentZoom = false)
    {
        #region 阶段1：初始中心识别与自救
        // 参数初始化
        using var ra1 = CaptureToRectArea();
        double currentZoomLevel = GetBigMapZoomLevel(ra1);
        // 改法 B（keepCurrentZoom=true）：定位循环里不再把缩放强行归一到 finalZoomLevel(2.0)，
        // 而是保持拖动进入时的实际缩放（把放大下限设为当前缩放本身）。这样纹理少的地图（如沙漠）
        // 不会被强行放大到 2.0 导致 GetBigMapRect 认偏、点空。点击前 >4.4 的上限仍由步骤 5.6b 兜底。
        // 注意：只锁"放大下限"，远距离时仍允许缩小(zoom-out)以把目标拖进屏幕。
        double minZoomLevel;
        if (keepCurrentZoom)
        {
            minZoomLevel = currentZoomLevel;
        }
        else
        {
            // 旧日之海地图上用户配置优先于 2.0 下限：取 Math.Max(finalZoomLevel, _fastDragConfig.MinZoomLevel)
            // 确保用户配置（如 3.0）不被钳制后的 finalZoomLevel（2.0）压低。
            // 非旧日之海地图保持原有 Math.Min 行为不变。
            minZoomLevel = mapName == "SeaOfBygoneEras"
                ? Math.Max(finalZoomLevel, _fastDragConfig.MinZoomLevel)
                : Math.Min(finalZoomLevel, _fastDragConfig.MinZoomLevel);
        }
        double maxZoomLevel = _tpConfig.MaxZoomLevel;
        int exceptionTimes = 0;
        // 拖动未真实发生（MouseMoveMap 采样点检测命中）的连续次数账：仅在拖动生效后清零。
        // 与 exceptionTimes（中心点跳变账）、brightnessLowStreak（亮度账）职责独立。
        var dragNoMoveCount = 0;
        // 亮度过低"连续"轮数：只在亮度恢复正常时清零（连续性中断即重置），
        // 切区域重识别成功不清此账——否则暗图恒能识别会让它永远 ≤1，"地图亮度过低，重新传送"升级永不触发。
        // 与 exceptionTimes（中心点跳变账）职责独立，二者不再互相清零。
        var brightnessLowStreak = 0;
        Point2f mapCenterPoint;
        try
        {
            mapCenterPoint = GetPositionFromBigMap(mapName); // 初始中心
        }
        catch (MapPositionNotRecognizedException)
        {
            Simulation.SendInput.Mouse.LeftButtonUp();
            Logger.LogDebug("初始中心点识别失败，开启自救策略");
            // 判断当前缩放是否离最佳识别缩放（普通地图 4.4 / 霜月 3.0）较远，如果是，则先调整到最佳视角尝试
            var recognitionZoom = GetDisplayTpPointZoomLevel(mapName);
            if (Math.Abs(currentZoomLevel - recognitionZoom) > 0.3) 
            {
                await AdjustMapZoomLevel(currentZoomLevel, recognitionZoom);
                currentZoomLevel = recognitionZoom;
                await Delay(300, ct);

                try
                {
                    mapCenterPoint = GetPositionFromBigMap(mapName);
                    Logger.LogDebug("调整缩放后识别恢复成功");
                }
                catch (MapPositionNotRecognizedException)
                {
                    Logger.LogDebug("缩放后依然失败，尝试强制跃迁...");
                    await ForceJumpToTargetArea(x, y, mapName); 
                    await Delay(100, ct);
                    await WaitMapStableOrTimeoutAsync(1000);
                    
                    try
                    {
                        mapCenterPoint = GetPositionFromBigMap(mapName);
                        Logger.LogDebug("强制切换区域后识别恢复成功");
                    }
                    catch (MapPositionNotRecognizedException ex)
                    {
                        throw new Exception("所有脱困策略均失效，无法获取初始点", ex);
                    }
                    finally
                    {
                        Simulation.SendInput.Mouse.LeftButtonUp();
                    }
                }
            }
            else
            {
                Simulation.SendInput.Mouse.LeftButtonUp();
                Logger.LogDebug("缩放已在最佳区间附近，直接尝试强制跃迁...");
                await ForceJumpToTargetArea(x, y, mapName); 
                await Delay(100, ct);
                await WaitMapStableOrTimeoutAsync(1000);
                
                try
                {
                    mapCenterPoint = GetPositionFromBigMap(mapName);
                    Logger.LogDebug("强制切换区域后识别恢复成功");
                }
                catch (MapPositionNotRecognizedException ex)
                {
                    Simulation.SendInput.Mouse.LeftButtonUp();
                    throw new Exception("初始识别失败且切换区域后依然无效", ex);
                }
            }
        }

        #endregion

        #region 阶段2：清除先验 + 缩小地图
        // 清除第一层先验（半径100会锁死起点），保留第二层（半径500跟随拖动）
        _miniMapPriorGenshin = null;
        _priorIsRegionCenter = false;

        var (xOffset, yOffset) = (x - mapCenterPoint.X, y - mapCenterPoint.Y);
        double totalMoveMouseX = _tpConfig.MapScaleFactor * Math.Abs(xOffset) / currentZoomLevel;
        double totalMoveMouseY = _tpConfig.MapScaleFactor * Math.Abs(yOffset) / currentZoomLevel;
        double mouseDistance = Math.Sqrt(totalMoveMouseX * totalMoveMouseX + totalMoveMouseY * totalMoveMouseY);
        // 缩小地图到恰当的缩放
        if (mouseDistance > _tpConfig.MapZoomOutDistance)
        {
            using var ra = CaptureToRectArea();
            double targetZoomLevel = currentZoomLevel * mouseDistance / _tpConfig.MapZoomOutDistance;
            targetZoomLevel = Math.Min(targetZoomLevel, maxZoomLevel);
            await AdjustMapZoomLevel(currentZoomLevel, targetZoomLevel);
            using var ra2 = CaptureToRectArea();
            double nextZoomLevel = GetBigMapZoomLevel(ra2);
            totalMoveMouseX *= currentZoomLevel / nextZoomLevel;
            totalMoveMouseY *= currentZoomLevel / nextZoomLevel;
            mouseDistance *= currentZoomLevel / nextZoomLevel;
            currentZoomLevel = nextZoomLevel;
        }
        #endregion

        #region 阶段3：拖动主循环
        // 开始移动并放大地图
        for (var iteration = 0; iteration < _tpConfig.MaxIterations; iteration++)
        {
            // 放大决策抽为纯函数 TeleportZoomDecisions.ShouldZoomInThisIteration（便于 PBT）。
            // 修复：快速拖动模式下 mouseDistance 已进入收工区间(<收工阈值) 且 缩放已在传送点可见档时
            // 不再触发对定位无意义的放大；缩放仍大于显示档(普通点不渲染)时即使到位也继续放大，避免点空。
            // 详见 .kiro/specs/teleport-fastmode-drag-redundant-zoom-before-click-fix/。
            if (TeleportZoomDecisions.ShouldZoomInThisIteration(
                    true,
                    _tpConfig.MapZoomEnabled,
                    mouseDistance,
                    currentZoomLevel,
                    minZoomLevel,
                    _tpConfig.PrecisionThreshold,
                    retryTimes,
                    _tpConfig.MapZoomInDistance,
                    GetDisplayTpPointZoomLevel(mapName)))
            {
                double targetZoomLevel = currentZoomLevel * mouseDistance / 600;
                targetZoomLevel = Math.Max(targetZoomLevel, minZoomLevel);
                await AdjustMapZoomLevel(currentZoomLevel, targetZoomLevel);
                using var ra4 = CaptureToRectArea();
                double nextZoomLevel = GetBigMapZoomLevel(ra4);
                totalMoveMouseX *= currentZoomLevel / nextZoomLevel;
                totalMoveMouseY *= currentZoomLevel / nextZoomLevel;
                mouseDistance *= currentZoomLevel / nextZoomLevel;
                currentZoomLevel = nextZoomLevel;
            }

            // 早停：快速拖动模式下传送点已落在含 margin 可点击安全区，提前 break（不必拖到正中心）。
            // 复用当轮已刷新的 mapCenterPoint 推算屏幕坐标，减少冗余拖动轮数。
            // 最终点击仍由 TpOnce 步骤5 的 IsPointInBigMapWindow 二次守门复核，早停不决定点击位置。
            //
            // iteration > 0 前置条件（关键）：MoveMapTo 仅在外层 do-while 的 IsPointInBigMapWindow
            // 刚判定"不可点击"后才被调用，故 iteration==0（尚未拖动任何一次）时的早停必为假阳性——
            // 它与刚做过的权威判据矛盾，且零拖动会导致外层反复重入、地图不动而 livelock 直至重试耗尽
            // （旧日之海中心点识别不稳时的实测失败）。"0 次拖动即可点击"本就由外层在调用 MoveMapTo 前
            // 用 IsPointInBigMapWindow 处理，故此门控不损失任何合法早停；提瓦特/层岩等均在拖动后
            // （iteration>=1）才触发早停，行为不受影响。
            // 详见 .kiro/specs/teleport-drag-early-stop-when-clickable/。
            if (enableEarlyStop
                && iteration > 0
                && TeleportClickSafeZone.ShouldEarlyStopClick(
                    true, x, y, mapCenterPoint.X, mapCenterPoint.Y,
                    _tpConfig.MapScaleFactor, currentZoomLevel, TeleportClickSafeZone.DefaultEarlyStopMargin, country))
            {
                // [诊断] 早停用的是几何投影(mapCenterPoint + currentZoomLevel 变量)。
                // 打出：①早停算出的 clickX/clickY（1080P 空间）②currentZoomLevel 变量值
                // ③实测截图缩放（可能与变量值不一致→就是判据打架根因）。
                // 与 do-while 的 [IsPointInBigMapWindow] 对照：若两者对同一目标点给出相反结论，
                // 且实测缩放≠currentZoomLevel 变量，即坐实"早停几何 vs 模板 rect 尺度不一致"。
                double __esClickX = 960 - _tpConfig.MapScaleFactor * (x - mapCenterPoint.X) / currentZoomLevel;
                double __esClickY = 540 - _tpConfig.MapScaleFactor * (y - mapCenterPoint.Y) / currentZoomLevel;
                double __esMeasuredZoom;
                using (var raEs = CaptureToRectArea()) { __esMeasuredZoom = GetBigMapZoomLevel(raEs); }
                TaskControl.Logger.LogDebug(
                    "[诊断-早停] 提前结束拖动（第 {I} 次）目标({X:0},{Y:0}) 中心({CX:0},{CY:0}) 早停算点=({ClickX:0},{ClickY:0}) currentZoom变量={CZ:0.00} 实测缩放={MZ:0.00} 偏差={Off:0}",
                    iteration + 1, x, y, mapCenterPoint.X, mapCenterPoint.Y, __esClickX, __esClickY,
                    currentZoomLevel, __esMeasuredZoom, Math.Sqrt((x - mapCenterPoint.X) * (x - mapCenterPoint.X) + (y - mapCenterPoint.Y) * (y - mapCenterPoint.Y)));
                break;
            }

            // 非常接近目标点，不再进一步调整
            if (enableEarlyStop && mouseDistance < (retryTimes == 0 ? 400 : 300))
            {
                TaskControl.Logger.LogDebug("移动 {I} 次鼠标后，已经接近目标点，不再移动地图。", iteration + 1);
                break;
            }
            
            // TaskControl.Logger.LogDebug("屏幕参数：{screenHeight}", _screenHeight);
            
            int moveMouseX, moveMouseY;
            (double, double)? landingOverride = null;

            // Dynamic_Runway_Mode：快速拖动 且 MapZoomDistanceForce==0 或 >0 均走动态跑道（>0 时额外加延时）
            // 详见 .kiro/specs/teleport-drag-edge-aware-runway-clamp/design.md §Components 2
            // 茶包快速拖动恒走动态跑道（旧经典分步拖动逻辑已随死代码清理移除）。
            const bool dynamicRunway = true;
            var moveStepDivisor = 40;
            if (dynamicRunway)
            {
                // 意图物理像素位移 = 到目标的满量位移。绝对定位（MouseMoveTo）精确落位、不过冲，
                // 故无需过冲保护系数；满量拖动最多把目标拖到正中心不会过头/回摆，太远则由跑道 t<1
                // 自动截断到屏幕内、下一轮继续。这样每次拖满、速度最快，且不顶边。
                double rawMoveX = totalMoveMouseX * Math.Sign(xOffset);
                double rawMoveY = totalMoveMouseY * Math.Sign(yOffset);

                // 落点：与 MouseMoveMap 快速拖动分支同一随机公式，只算一次，供跑道计算与实际拖动共用
                var captureRect = TaskContext.Instance().SystemInfo.CaptureAreaRect;
                int signX = -Math.Sign((int)rawMoveX);
                int signY = -Math.Sign((int)rawMoveY);
                double landingX = captureRect.Width / 2d + Random.Shared.Next(captureRect.Width / 5, captureRect.Width * 3 / 10) * signX;
                double landingY = captureRect.Height / 2d + Random.Shared.Next(captureRect.Height / 5, captureRect.Height * 3 / 10) * signY;
                landingOverride = (landingX, landingY);

                // 落点绝对物理坐标 = 捕获区左上角 + 落点（虚拟桌面物理坐标，与 GetCursorPos 同空间）。
                // 跑道边界用游戏所在显示器的物理矩形：必须用 Win32 GetMonitorInfo.rcMonitor（与 capRect /
                // GetCursorPos 同一物理虚拟桌面空间），不能用 WinForms Screen.Bounds——后者在高 DPI 缩放
                // 下返回逻辑坐标，与 capRect 混用会导致落点相对坐标错乱、runway<0、t=0 卡死。
                double landingAbsX = captureRect.X + landingX;
                double landingAbsY = captureRect.Y + landingY;

                Vanara.PInvoke.RECT monRect;
                var __hMon = Vanara.PInvoke.User32.MonitorFromWindow(TaskContext.Instance().GameHandle,
                    Vanara.PInvoke.User32.MonitorFlags.MONITOR_DEFAULTTONEAREST);
                var __mi = new Vanara.PInvoke.User32.MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Vanara.PInvoke.User32.MONITORINFO>() };
                if (Vanara.PInvoke.User32.GetMonitorInfo(__hMon, ref __mi))
                {
                    monRect = __mi.rcMonitor;
                }
                else
                {
                    // 兜底：取不到显示器信息时退回捕获区自身范围（绝对坐标），至少不会算出负跑道卡死
                    monRect = new Vanara.PInvoke.RECT(captureRect.X, captureRect.Y,
                        captureRect.X + captureRect.Width, captureRect.Y + captureRect.Height);
                }

                // 落点换算成"相对显示器左上角"，边界用显示器物理宽高，三者同处物理空间
                double monLeft = monRect.Left, monTop = monRect.Top;
                double monW = monRect.Width, monH = monRect.Height;
                double landingRelX = landingAbsX - monLeft;
                double landingRelY = landingAbsY - monTop;

                // 自校准放大比值：MoveMouseBy 相对移动实际物理位移 = 意图位移 × ratio（随 DPI 漂移）。
                // 跑道要约束的是"实际物理位移"，故把意图位移 × ratio 作为预期实际位移喂给跑道计算。
                // 首次未校准（=0）时用 max(1, dpi/2) 作初值（dpi2→1.0、dpi2.5→1.25，贴合实测）。
                double dpiForInit = TaskContext.Instance().DpiScale;
                double amplify = _dragMoveAmplifyRatio > 0 ? _dragMoveAmplifyRatio : Math.Max(1.0, dpiForInit / 2.0);
                double t = TeleportDragRunway.ComputeRunwayScale(
                    landingRelX, landingRelY, rawMoveX * amplify, rawMoveY * amplify,
                    monW, monH, 50);

                moveMouseX = (int)(rawMoveX * t);
                moveMouseY = (int)(rawMoveY * t);

                if (t < 1.0)
                {
                    TaskControl.Logger.LogDebug("[传送拖动] 触发边缘跑道截断 t={T:0.00}（拖到屏幕边缘即停）", t);
                }
            }

            double moveMouseLength = Math.Sqrt(moveMouseX * moveMouseX + moveMouseY * moveMouseY);
            int moveSteps = Math.Max((int)moveMouseLength / moveStepDivisor, 3); // 每次移动的步数最小为 3，避免除 0 错误

            // [诊断-移动量] 拖动前：真实迭代号 iteration（区别于日志里绑 exceptionTimes 的"迭代"字段）、
            // 当前缩放、到目标 offset、算出的鼠标位移(moveMouseX/Y)、期望像素位移长度、步数。
            // 若 moveMouseX/Y 很小 → 拖动量本身就不够（放大过头/收工阈值问题）；
            // 若 moveMouseX/Y 足够大但下一条识别 ratio≈0 → 拖动手势没生效（假设1）。
            TaskControl.Logger.LogDebug(
                "[诊断-移动量] iteration={It} 模式={Mode} 缩放={CZ:0.00} 中心=({CX:0},{CY:0}) offset=({OX:0},{OY:0}) moveMouse=({MX},{MY}) 期望像素长={Len:0} 步数={Steps} landing={Landing}",
                iteration, "动态跑道", currentZoomLevel,
                mapCenterPoint.X, mapCenterPoint.Y, xOffset, yOffset, moveMouseX, moveMouseY,
                moveMouseLength, moveSteps,
                landingOverride is { } lo2 ? $"({lo2.Item1:0},{lo2.Item2:0})" : "无");

            bool dragMovedMap = await MouseMoveMap(moveMouseX, moveMouseY, moveSteps, landingOverride);

            // 拖动未真实发生（MouseMoveMap 采样点检测到地图没动，已 break 中断并返回 false）：
            // 不要当作"已拖动"去预测/盲走（否则 ratio≈0 误判 → 反方向乱拖）。重新拖动本段。
            // 但用计数兜底防死循环：连续多次都拖不动（如地图已到边缘/被弹层反复挡）则放弃本轮拖动，
            // 交给上层重试传送（与"多次尝试未移动到目标"语义一致）。
            if (!dragMovedMap)
            {
                if (++dragNoMoveCount > 3)
                {
                    Simulation.SendInput.Mouse.LeftButtonUp();
                    throw new Exception("多次拖动地图未生效，无法移动地图，重新传送");
                }
                // MouseMoveMap 内部不负责 LeftButtonUp（由调用方释放）；
                // 采样点命中 break 时鼠标仍处于按下态。continue 重拖前必须先松开，否则下一次 LeftButtonDown 叠加、按钮一直按着。
                Simulation.SendInput.Mouse.LeftButtonUp();
                TaskControl.Logger.LogWarning("拖动未真实发生，重新拖动本段（第 {Count} 次）", dragNoMoveCount);
                continue;
            }

            // 动态跑道：拖动越快（小 StepInterval）画面渲染越可能滞后，若立刻识别会读到中间态坐标、
            // 距离虚高导致多拖一轮。拖后先等地图像素稳定再识别（通常几十 ms 即返回，远比多拖一整轮便宜），
            // 使小 StepInterval 也能一次到位。
            await WaitMapStableOrTimeoutAsync(ApplyExtraDelay(300));

            // 推算理论上的移动后坐标 (惯性预测)
            Point2f predictedPoint = mapCenterPoint + new Point2f(
                (float)(moveMouseX * currentZoomLevel / _tpConfig.MapScaleFactor),
                (float)(moveMouseY * currentZoomLevel / _tpConfig.MapScaleFactor));

            // 预测移动距离（原神坐标），供跳变判定与拖动先验半径共用
            double expectedMoveLen = Math.Sqrt(moveMouseX * moveMouseX + moveMouseY * moveMouseY) * currentZoomLevel / _tpConfig.MapScaleFactor;

            // 拖动滑动窗口先验：中心=预测位置，半径=预测移动距离*2（至少给个下限防止半径过小锁死）。
            // 仅提瓦特启用（GetBigMapCenterPoint 内部有 mapName/类型判断兜底）。
            _dragPriorCenterGenshin = predictedPoint;
            _dragPriorRadiusGenshin = Math.Clamp(expectedMoveLen+500, 200, 2000);



            try
            {
                var newCenterPoint = GetPositionFromBigMap(mapName, usePrior: false); // 拖动中识别，走拖动滑动窗口先验（下方 usePrior 参数不影响拖动先验）

                // 计算识别坐标与预测坐标的偏差
                double jumpDistance = Math.Sqrt(Math.Pow(newCenterPoint.X - predictedPoint.X, 2) + Math.Pow(newCenterPoint.Y - predictedPoint.Y, 2));
                double predictedDeltaX = predictedPoint.X - mapCenterPoint.X;
                double predictedDeltaY = predictedPoint.Y - mapCenterPoint.Y;
                double actualDeltaX = newCenterPoint.X - mapCenterPoint.X;
                double actualDeltaY = newCenterPoint.Y - mapCenterPoint.Y;
                double actualMoveLen = Math.Sqrt(actualDeltaX * actualDeltaX + actualDeltaY * actualDeltaY);
                double moveRatio = expectedMoveLen > 0 ? actualMoveLen / expectedMoveLen : 0;
                double expectedLen = Math.Sqrt(predictedDeltaX * predictedDeltaX + predictedDeltaY * predictedDeltaY);
                double moveDirectionCos = (expectedLen <= 1 || actualMoveLen <= 1) ? 1.0
                    : (predictedDeltaX * actualDeltaX + predictedDeltaY * actualDeltaY) / (expectedLen * actualMoveLen);

                Logger.LogDebug("[诊断-拖动循环] 迭代={I} 当前中心=({CX:0},{CY:0}) 预测=({PX:0},{PY:0}) 识别=({NX:0},{NY:0}) jumpDist={J:0} expectedMoveLen={E:0} ratio={R:0.00} cos={Cos:0.00}",
                    exceptionTimes, mapCenterPoint.X, mapCenterPoint.Y, predictedPoint.X, predictedPoint.Y,
                    newCenterPoint.X, newCenterPoint.Y, jumpDistance, expectedMoveLen, moveRatio, moveDirectionCos);

                bool isMoveAnomaly = jumpDistance > Math.Max(200, expectedMoveLen * 2)
                    || (expectedMoveLen > 1200 && (moveRatio < 0.55 || moveRatio > 1.85))
                    || (expectedMoveLen > 1200 && actualMoveLen > 120 && moveDirectionCos < 0.65);

                if (isMoveAnomaly)
                {
                    Simulation.SendInput.Mouse.LeftButtonUp();
                    Logger.LogDebug("坐标异常跳跃({dist:0.0}) ratio={Ratio:0.00} cos={Cos:0.00}，判定为误识别", jumpDistance, moveRatio, moveDirectionCos);
                    throw new MapPositionNotRecognizedException("中心点识别坐标异常跳跃");
                }

                mapCenterPoint = newCenterPoint;
                exceptionTimes = 0;
                // 本次拖动被确认生效（识别坐标与预测一致），清零拖动未生效账
                dragNoMoveCount = 0;
            }
            catch (MapPositionNotRecognizedException)
            {
                exceptionTimes++;
                Simulation.SendInput.Mouse.LeftButtonUp();

                // 独立地图 / 非提瓦特：保持旧逻辑（第 2 次抛重传），零回归（bugfix BC-3 / CC5）。
                // 处理完 mapCenterPoint 后自然 fall-through 到 catch 后的清先验/亮度检测/重算 offset，不在 catch 内 return/continue。
                if (!AutoTrackPositionRecoveryDecisions.IsRecoveryApplicable(mapName == MapTypes.Teyvat.ToString()))
                {
                    if (exceptionTimes > 1)
                    {
                        throw new Exception("多次中心点识别失败或异常，惯性推算失效，重新传送");
                    }

                    Logger.LogDebug("进入盲走推算 (跳过次数: {times})", exceptionTimes);
                    mapCenterPoint = predictedPoint;
                }
                else
                {
                    // 提瓦特连续大图：分级补救（bugfix BC-1 / design 组件2）。
                    // 第 1 级盲走（现状）→ 第 2 级拉大缩放再识别 → 第 3 级切地区 → 兜底抛重传。
                    switch (AutoTrackPositionRecoveryDecisions.Decide(exceptionTimes, currentZoomLevel))
                    {
                        case CenterRecoveryAction.BlindWalk:
                            Logger.LogDebug("进入盲走推算 (跳过次数: {times})", exceptionTimes);
                            mapCenterPoint = predictedPoint;
                            break;

                        case CenterRecoveryAction.ZoomInThenRecog:
                            await AdjustMapZoomLevel(currentZoomLevel, AutoTrackPositionRecoveryDecisions.RecoverStableZoom);
                            currentZoomLevel = AutoTrackPositionRecoveryDecisions.RecoverStableZoom; // 同步局部变量，后续 offset 用新缩放
                            TryRecoverRecenter(mapName, x, y, ref mapCenterPoint, ref xOffset, ref yOffset,
                                ref totalMoveMouseX, ref totalMoveMouseY, ref mouseDistance, ref exceptionTimes, currentZoomLevel);
                            break;

                        case CenterRecoveryAction.SwitchAreaThenRecog:
                            await SwitchRecentlyCountryMap(x, y, country); // 切地区，内部逻辑零改动（CC4）
                            TryRecoverRecenter(mapName, x, y, ref mapCenterPoint, ref xOffset, ref yOffset,
                                ref totalMoveMouseX, ref totalMoveMouseY, ref mouseDistance, ref exceptionTimes, currentZoomLevel);
                            break;

                        case CenterRecoveryAction.ThrowRetry:
                        default:
                            throw new Exception("多次中心点识别失败或异常，惯性推算失效，重新传送");
                    }
                }
            }

            // 清掉拖动滑动窗口先验前，把最终中心点保存到 _lastDragCenterGenshin
            if (_dragPriorCenterGenshin is Point2f lastCenter)
            {
                _lastDragCenterGenshin = lastCenter;
            }
            // 清掉拖动滑动窗口先验，避免影响本轮循环内后续识别（亮度切图等）
            _dragPriorCenterGenshin = null;
            _dragPriorRadiusGenshin = 0;

            // 地图亮度检测（快速拖动模式）：亮度过低 → 切图重识别（避免大地图空转）
            using var ra = CaptureToRectArea().SrcMat;
            double brightness = Cv2.Mean(ra).Val0;
            TaskControl.Logger.LogDebug("地图亮度:{brightness}", brightness);
            if (brightness < (mapName=="SeaOfBygoneEras" ? 32:50))
            {
                brightnessLowStreak++;
            
                if (brightnessLowStreak > 1)
                {
                    Simulation.SendInput.Mouse.LeftButtonUp();
                    throw new Exception("地图亮度过低，重新传送");
                }

                if (brightnessLowStreak > 0)
                {
                    Simulation.SendInput.Mouse.LeftButtonUp();
                    TaskControl.Logger.LogWarning("地图亮度过低");
                    if (mapName == MapTypes.Teyvat.ToString())
                    {
                        // 计算传送点位置离哪张地图切换后的中心点最近，切换到该地图
                        await SwitchRecentlyCountryMap(x, y, country);
                    }
                    else
                    {
                        // 直接切换地区
                        await SwitchArea(MapTypesExtensions.ParseFromName(mapName).GetDescription());
                    }
                    // 切换地图/地区后画面完全变化，旧 mapCenterPoint 已失效。若直接 continue，下一轮会先
                    // 用切换前的旧中心点算拖动方向/距离（真正的重新识别要等下一轮拖动后才发生），导致拖错方向。
                    // 故此处等地图稳定后立即重新识别中心点并重算 offset/mouseDistance；识别失败则维持旧行为，
                    // 交由下一轮拖动后识别兜底。
                    await WaitMapStableOrTimeoutAsync(1000);
                    try
                    {
                        mapCenterPoint = GetPositionFromBigMap(mapName, usePrior: false); // 切图后识别，不用先验
                        (xOffset, yOffset) = (x - mapCenterPoint.X, y - mapCenterPoint.Y);
                        totalMoveMouseX = _tpConfig.MapScaleFactor * Math.Abs(xOffset) / currentZoomLevel;
                        totalMoveMouseY = _tpConfig.MapScaleFactor * Math.Abs(yOffset) / currentZoomLevel;
                        mouseDistance = Math.Sqrt(totalMoveMouseX * totalMoveMouseX + totalMoveMouseY * totalMoveMouseY);
                        // 切图重识别成功，等同主路径"识别成功即清零失败账"的约定：清零 exceptionTimes。
                        // 否则切图前旧地图上累积的 exceptionTimes 会赖到切图后的新地图账上，使容错额度被历史
                        // 欠账吃掉——动态跑道模式阈值仅 1，切图后再有一次跳跃即撞线抛"惯性推算失效"，
                        // 导致"地图正常、传送点已可点击"却误报传送失败重试。
                        // 只清跳变账 exceptionTimes（切图后新地图不该背旧地图的跳变欠账）。
                        // 【关键】不再清 brightnessLowStreak：亮度过低是否持续，只由"亮度是否恢复正常"决定；
                        // 切区域重识别成功≠亮度恢复（暗图半径2500兜底几乎恒能识别）。清零会让连续亮度过低
                        // 永远升不到 >1，"地图亮度过低，重新传送"永不触发，传送在大地图空转数十秒（本 bug 根因）。
                        exceptionTimes = 0;
                        TaskControl.Logger.LogDebug("亮度过低切换地图后重新识别中心点成功");
                    }
                    catch (MapPositionNotRecognizedException)
                    {
                        // 切换后地图尚未完全就绪、识别失败：不更新坐标，交给下一轮循环拖动后识别兜底。
                        TaskControl.Logger.LogDebug("亮度过低切换地图后中心点识别仍失败，下一轮再试");
                    }
                    continue;
                }
            }
            else
            {
                // 亮度恢复正常 → 连续亮度过低中断，清零累计（连续性语义）
                brightnessLowStreak = 0;
            }

            (xOffset, yOffset) = (x - mapCenterPoint.X, y - mapCenterPoint.Y);
            totalMoveMouseX = _tpConfig.MapScaleFactor * Math.Abs(xOffset) / currentZoomLevel;
            totalMoveMouseY = _tpConfig.MapScaleFactor * Math.Abs(yOffset) / currentZoomLevel;
            mouseDistance = Math.Sqrt(totalMoveMouseX * totalMoveMouseX + totalMoveMouseY * totalMoveMouseY);
            Simulation.SendInput.Mouse.LeftButtonUp();
        }
        #endregion
    }

    /// <summary>
    /// 补救动作（拉缩放或切地区）后，重新识别中心点并重算 offset/mouseDistance。
    /// 识别成功 → 清零 exceptionTimes；失败 → 静默不更新坐标，交由下一轮循环继续计数，
    /// 最终达第 4 级触发 ThrowRetry 兜底（teleport-drag-center-recognition-escalating-recovery spec）。
    /// 与"亮度过低→切图重识别"分支（同方法下方）逻辑一致，但不改动该分支（CC6）。
    ///
    /// 此处 catch 静默是设计上的有意妥协：识别失败为可恢复异常，交由分级计账在下一轮继续累加后上抛处理，
    /// 不是无条件吞掉错误——故不在此处重新抛出，而是让异常计数持续增长直至触发 ThrowRetry。
    /// </summary>
    private void TryRecoverRecenter(string mapName, double x, double y,
        ref Point2f mapCenterPoint, ref double xOffset, ref double yOffset,
        ref double totalMoveMouseX, ref double totalMoveMouseY, ref double mouseDistance,
        ref int exceptionTimes, double zoom)
    {
        try
        {
            mapCenterPoint = GetPositionFromBigMap(mapName, usePrior: false); // 甩掉旧先验，重新识别
            (xOffset, yOffset) = (x - mapCenterPoint.X, y - mapCenterPoint.Y);
            totalMoveMouseX = _tpConfig.MapScaleFactor * Math.Abs(xOffset) / zoom;
            totalMoveMouseY = _tpConfig.MapScaleFactor * Math.Abs(yOffset) / zoom;
            mouseDistance = Math.Sqrt(totalMoveMouseX * totalMoveMouseX + totalMoveMouseY * totalMoveMouseY);
            exceptionTimes = 0; // 识别成功，清零失败账（与主路径约定一致）
        }
        catch (MapPositionNotRecognizedException)
        {
            // 补救后仍识别失败：不更新坐标，交由下一轮循环拖动后识别兜底（分级晋级直到 ThrowRetry）。
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
        // 缩放滑轨拖动专用：用"实时读取游戏窗口"的比例放大 1080p 参数坐标，替代 GameRegionMove 内部
        // 依赖的 SystemInfo.ScaleTo1080PRatio。后者在 BetterGI 启动时构建、不随窗口变化刷新，
        // "运行中切换游戏分辨率/窗口大小"后会过期（曾实测 2K 下仍 =1.000），导致拖动落点脱离实际滑块。
        // 正常场景（窗口未变）：实时比例 == ScaleTo1080PRatio，行为逐字节不变（防回归）。
        var handle = TaskContext.Instance().GameHandle;
        var realRect = SystemControl.GetCaptureRect(handle);       // 实时获取当前游戏窗口矩形（跟随中途切分辨率）
        var realScale = Math.Max(1e-6, realRect.Width / 1920d);    // 实时 1080p→实际 比例
        double sx1 = realRect.X + x1 * realScale;
        double sy1 = realRect.Y + y1 * realScale;
        double sx2 = realRect.X + x2 * realScale;
        double sy2 = realRect.Y + y2 * realScale;

        // GlobalMethod.MoveMouseTo(x1, y1);
        DesktopRegion.DesktopRegionMove(sx1, sy1);
        await Delay(ApplyExtraDelay(50), ct);
        GlobalMethod.LeftButtonDown();
        await Delay(ApplyExtraDelay(50), ct);
        // GlobalMethod.MoveMouseTo(x2, y2);
        DesktopRegion.DesktopRegionMove(sx2, sy2);
        await Delay(ApplyExtraDelay(50), ct);
        GlobalMethod.LeftButtonUp();
        await Delay(ApplyExtraDelay(50), ct);
        // 拖动结束后回到地图区域中心（与 GameRegionMove 移到中心的旧行为一致：X 复用 Width 是遗留，未改动）
        GameCaptureRegion.GameRegionMove((rect, scale) => (rect.Width / 2d, rect.Width / 2d));
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
        // Logger.LogInformation("调整地图缩放等级：{zoomLevel:0.000} -> {targetZoomLevel:0.000}", zoomLevel, targetZoomLevel);
        int initialY = (int)(_tpConfig.ZoomStartY + (_tpConfig.ZoomEndY - _tpConfig.ZoomStartY) * (zoomLevel - 1) / 5d);
        int targetY = (int)(_tpConfig.ZoomStartY + (_tpConfig.ZoomEndY - _tpConfig.ZoomStartY) * (targetZoomLevel - 1) / 5d);
        // [缩放坐标诊断] 只读日志，不改任何计算。打印拖动起点/终点（1080p config 坐标）、缓存比例与实时比例，验证 2K/4K 缩放偏移修复是否生效。
        var realRectNow = SystemControl.GetCaptureRect(TaskContext.Instance().GameHandle);
        TaskControl.Logger.LogDebug(
            "[缩放坐标诊断-写入] zoom={Zoom:0.00} target={Target:0.00} initialY={InitialY} targetY={TargetY} ZoomButtonX={BtnX} ZoomStartY={StartY} ZoomEndY={EndY} 缓存ScaleTo1080PRatio={CachedRatio:0.000} 实时Scale={RealRatio:0.000} 实时窗口={RealW}x{RealH}",
            zoomLevel, targetZoomLevel, initialY, targetY, _fastDragConfig.ZoomButtonX, _tpConfig.ZoomStartY, _tpConfig.ZoomEndY, TaskContext.Instance().SystemInfo.ScaleTo1080PRatio, realRectNow.Width / 1920d, realRectNow.Width, realRectNow.Height);
        //当前缩放LOG显示
        await MouseClickAndMove(_fastDragConfig.ZoomButtonX+10, initialY, _fastDragConfig.ZoomButtonX+10, targetY);
        await Delay(ApplyExtraDelay(50), ct);
    }

    /// <summary>
    /// 拖动地图。总位移 = (pixelDeltaX, pixelDeltaY)，steps 步缓动完成。
    /// 快速拖动/动态跑道模式下拖动中途检测采样点 (500,500)/(600,500)：
    /// 若像素与拖动前一致且意图位移显著，说明这次拖动地图根本没动（被弹层挡住 / 拖到边界 / 手势未生效），
    /// 返回 false 交由上层"重新拖动本段"而不是当作已拖动去预测（否则 ratio≈0 误判 → 盲走乱拖）。
    /// </summary>
    /// <returns>true=本次拖动生效（可信）；false=检测到拖动中途地图没动（应重拖本段）。</returns>
    private async Task<bool> MouseMoveMap(int pixelDeltaX, int pixelDeltaY, int steps = 10, (double, double)? landingOverride = null)
    {
        double dpi = TaskContext.Instance().DpiScale;
        int[] stepX = GenerateSteps((int)(pixelDeltaX / dpi), steps);
        int[] stepY = GenerateSteps((int)(pixelDeltaY / dpi), steps);
        //检查标记
        var isMark = true;
        // 是否检测到"拖动中途地图没动"：命中采样点未变 + 意图位移显著 时置 true，
        // 方法末尾返回 !__noMoveDetected 通知上层"应重拖本段"。仅快速拖动分支有意义。
        bool __noMoveDetected = false;

        if (landingOverride is { } lp)
        {
            // Dynamic_Runway_Mode：使用 MoveMapTo 已算好的落点（与跑道计算同一取值），
            // 保证惯性推算位移与真实拖动一致。详见 teleport-drag-edge-aware-runway-clamp spec。
            GameCaptureRegion.GameRegionMove((_, _) => (lp.Item1, lp.Item2));
        }
        else
        {
            // Custom_Fixed_Mode：原随机落点公式，逐字节不变
            int signX = -Math.Sign(pixelDeltaX);
            int signY = -Math.Sign(pixelDeltaY);
            GameCaptureRegion.GameRegionMove((rect, _) =>
                (rect.Width / 2d + Random.Shared.Next(rect.Width / 5, rect.Width *3/10)*signX,
                    rect.Height / 2d + Random.Shared.Next(rect.Height / 5, rect.Height *3/10)*signY));
        }

        await Delay(ApplyExtraDelay(50+_tpConfig.StepIntervalMilliseconds-2), ct);
        Simulation.SendInput.Mouse.LeftButtonDown();
        await Delay(ApplyExtraDelay(50+_tpConfig.StepIntervalMilliseconds-2), ct);

        // 动态跑道自校准：拖动前读真实光标物理坐标（GetCursorPos 返回物理像素），
        // 拖动后再读一次，用前后差实测 MoveMouseBy 放大比值。仅动态跑道模式需要。
        bool __needCalib = landingOverride is not null;
        Vanara.PInvoke.POINT __curBefore = default;
        if (__needCalib)
        {
            Vanara.PInvoke.User32.GetCursorPos(out __curBefore);
        }

        using (var image = CaptureToRectArea())
        {
            var pos = image.SrcMat.At<Vec3b>(500,500);
            var pos2 = image.SrcMat.At<Vec3b>(600,500);

                // 动态跑道模式：用绝对定位 MoveMouseTo 分步，从落点 lp 精确移动到 lp+pixelDelta。
                // 绝对定位不受 Windows 指针加速影响（相对 MoveMouseBy 会被非线性放大，实测大位移放大 1.65×
                // 导致顶边/回摆），位移精确可预测。capture 区坐标与物理桌面 1:1（已由光标读回验证）。
                // 仅动态模式（landingOverride 非空）走此路；Custom_Fixed 仍走下方相对分步，逐字节不变。
                bool __absDrag = landingOverride is not null;
                double __startX = landingOverride?.Item1 ?? 0;
                double __startY = landingOverride?.Item2 ?? 0;
                // 绝对分步用不除 dpi 的缓动步进（capture 像素空间），累计和 = pixelDelta
                int[] stepXAbs = __absDrag ? GenerateSteps(pixelDeltaX, steps) : stepX;
                int[] stepYAbs = __absDrag ? GenerateSteps(pixelDeltaY, steps) : stepY;
                double __accX = 0, __accY = 0;

                for (var i = 1; i < steps; i++)
                {
                    var i1 = i;

                    if (__absDrag)
                    {
                        // 绝对定位：累计缓动步进得到当前应到达的 capture 坐标，MoveMouseTo 精确落位
                        __accX += stepXAbs[i1];
                        __accY += stepYAbs[i1];
                        double tx = __startX + __accX;
                        double ty = __startY + __accY;
                        GameCaptureRegion.GameRegionMove((_, _) => (tx, ty));
                    }
                    else
                    {
                        // Simulation.SendInput.Mouse.MoveMouseBy(stepX[i], stepY[i]);
                        GameCaptureRegion.GameRegionMoveBy((_, scale) => (stepX[i1] * scale, stepY[i1] * scale));
                    }
                    if(i==1) await Delay(50, ct);
                    // 绝对定位（动态跑道）用精确延时，让 StepInterval<15ms 真正生效（否则被系统计时精度钳到 ~15.6ms）；
                    // 其它路径保持原 Task.Delay 行为不变。
                    if (__absDrag)
                        await PreciseDelay(ApplyExtraDelay(_tpConfig.StepIntervalMilliseconds), ct);
                    else
                        await Delay(ApplyExtraDelay(_tpConfig.StepIntervalMilliseconds), ct);
                    
                    if (i >= steps/2 && steps > 3 && isMark)
                    {
                        using (var image2 = CaptureToRectArea())
                        {
                            var pos3 = image2.SrcMat.At<Vec3b>(500,500);
                            var pos4 = image2.SrcMat.At<Vec3b>(600,500);
                            if (pos3 == pos && pos4 == pos2)
                            {
                                using var esc = image2.Find(TpTaskFastDragAssets.Get(image2).MapCloseButtonWhiteRo);
                                if (esc.IsExist())
                                {
                                    // [诊断] 拖动中途采样点(500,500)/(600,500)像素与拖动前一致 + 有关闭按钮 → 判定地图被弹层遮挡。
                                    TaskControl.Logger.LogWarning("地图遮挡，重新调整 [诊断] 采样点像素未变(拖动被弹层挡住) step={I}/{Steps}", i, steps);
                                    await Delay(1500, ct);
                                    Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
                                    await Delay(1500, ct);
                                }
                                else
                                {
                                    // [诊断] 采样点像素未变 + 无关闭按钮 → 拖动中途地图没动（假设1直接证据）。
                                    // 但仅当意图位移足够大时才算"应该动却未动"：位移很小（已接近到位）时采样点不变是
                                    // 正常的，不应误判为重拖。60px 约等于一步缓动，小于此值说明基本到位、无需重拖。
                                    bool intentSignificant = Math.Abs(pixelDeltaX) + Math.Abs(pixelDeltaY) >= 60;
                                    if (intentSignificant)
                                    {
                                        if (_tpConfig.StepIntervalMilliseconds > 6)
                                        {
                                            // 恢复 ec5ed76b4 注释掉的诊断：证明"拖动未真实发生"确实被命中（而非采样点恰在同色区）。
                                            TaskControl.Logger.LogWarning(
                                                "地图拖动异常，重新调整 [诊断] 拖动中途采样点像素未变 step={I}/{Steps} p(500,500)前={A} 后={C} p(600,500)前={B} 后={D}",
                                                i, steps, pos.ToString(), pos3.ToString(), pos2.ToString(), pos4.ToString());
                                            // 鼠标移动间隔 ≤6 时拖动本身很快，"像素未变→重拖本段"无必要，不触发重拖（检测仍记录日志）。
                                            // 间隔 >6 时保留原行为：标记本次拖动未生效，跳出分步循环（让调用方 LeftButtonUp），
                                            // 方法末尾返回 false 通知上层重拖本段。
                                            __noMoveDetected = true;
                                            break;
                                        }
                                    }
                                }
                            }
                            isMark = false;
                        }
                    } 
                }

            // 拖动后读真实光标物理坐标，实测 MoveMouseBy 放大比值 = 实际物理位移 / 意图 pixelDelta，
            // 用较大分量轴计算（信噪比高），EMA 平滑写入 _dragMoveAmplifyRatio 供跑道计算自校准。
            if (__needCalib)
            {
                Vanara.PInvoke.User32.GetCursorPos(out var __curAfter);
                int __realDx = __curAfter.X - __curBefore.X;
                int __realDy = __curAfter.Y - __curBefore.Y;
                double __measured = 0;
                if (Math.Abs(pixelDeltaX) >= Math.Abs(pixelDeltaY) && pixelDeltaX != 0)
                {
                    __measured = Math.Abs(__realDx) / (double)Math.Abs(pixelDeltaX);
                }
                else if (pixelDeltaY != 0)
                {
                    __measured = Math.Abs(__realDy) / (double)Math.Abs(pixelDeltaY);
                }
                // 只在本次确实产生了有意义位移、且比值落在合理区间时校准（防中断/遮挡的异常样本污染）
                if (__measured >= 0.5 && __measured <= 3.0 && (Math.Abs(__realDx) + Math.Abs(__realDy)) > 20)
                {
                    _dragMoveAmplifyRatio = _dragMoveAmplifyRatio > 0
                        ? _dragMoveAmplifyRatio * 0.6 + __measured * 0.4   // EMA 平滑
                        : __measured;                                       // 首次直接采纳
                }
            }
        }

        // 返回本次拖动是否生效：检测到"地图没动"则返回 false（通知上层重拖本段），否则返回 true。
        // LeftButtonUp 由调用方 MoveMapTo 负责（break/异常/循环尾），此处不重复。
        return !__noMoveDetected;
    }

    /// <summary>
    /// 快速拖动模式下：等大地图视区像素稳定再返回，超时兜底。
    /// 通过对 (500,500) / (600,500) 两点 BGR 像素连续采样，连续 stableHits 次相等视为稳定。
    /// 由调用方在快速识别模式下决定是否使用。
    /// fast-drag-recognition-acceleration spec / design.md §3.1
    /// </summary>
    /// <param name="timeoutMs">兜底超时（与原固定 Delay 等值），超时即返回</param>
    /// <param name="pollMs">每次轮询间隔，默认 30ms（约一帧）</param>
    /// <param name="stableHits">连续多少次采样像素一致视为稳定，默认 2</param>
    private async Task WaitMapStableOrTimeoutAsync(int timeoutMs, int pollMs = 30, int stableHits = 2)
    {
        long deadline = Environment.TickCount + timeoutMs;
        Vec3b? prev1 = null, prev2 = null;
        int hits = 0;
        while (Environment.TickCount < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var ra = CaptureToRectArea();
                var p1 = ra.SrcMat.At<Vec3b>(860, 500);
                var p2 = ra.SrcMat.At<Vec3b>(860, 540);
                if (ra.Find(TpTaskFastDragAssets.Get(ra).MapScaleButtonRo).IsExist() && prev1.HasValue && p1 == prev1.Value && p2 == prev2!.Value)
                {
                    if (++hits >= stableHits)
                    {
                        Logger.LogDebug("检测到地图稳定");
                        return;
                    }
                }
                else
                {
                    hits = 0;
                }
                prev1 = p1;
                prev2 = p2;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 截图异常（暂停态/帧不可用）→ 不抛，下一轮重试，外层暂停信号会接管
                Logger.LogDebug("[快速识别] 像素采样异常: {Msg}", ex.Message);
                hits = 0;
            }
            await Delay(pollMs, ct);
        }
        Logger.LogDebug("检测到地图失败");
    }

    /// <summary>
    /// 快速识别模式下：地图点击后等"传送"按钮 popup + 点按钮 + 用 IsLoadingScreen 确认进入传送加载。
    /// 替代 Delay(500) + ClickTpPoint：
    /// 1. 高配机按钮 popup 50-150ms 就出现，立即点
    /// 2. 容错点击：点完按钮持续探测 IsLoadingScreen；未进入加载页则在窗口内重点（最多 3 次）
    /// 3. 仍未进 → 抛异常让上层走原 ClickTpPoint 兜底（保证不丢传送）
    /// 返回 true 表示已确认进入传送加载（IsLoadingScreen 命中），false 表示需要走兜底。
    /// fast-drag-recognition-acceleration spec / final click pre-stop optimization v2
    /// </summary>
    private async Task<bool> FastClickTeleportButtonAsync(int popupTimeoutMs = 500, int loadingTimeoutMs = 4500, int pollMs = 30)
    {
        long popupDeadline = Environment.TickCount + popupTimeoutMs;
        // 阶段 1：等按钮 popup 出现
        while (Environment.TickCount < popupDeadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var ra = CaptureToRectArea();
                using var found = ra.Find(_assets.TeleportButtonRo);
                if (found.IsExist())
                {
                    found.Click();
                    goto AfterClick;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogDebug("[快速识别] 探测传送按钮异常: {Msg}", ex.Message);
            }
            await Delay(pollMs, ct);
        }
        return false; // 阶段 1 超时：上层走 ClickTpPoint 兜底

    AfterClick:
        // 阶段 2：容错重点 + IsLoadingScreen 确认。点击可能因动画 popup 中"按钮可见但不可点"而无效。
        long loadingDeadline = Environment.TickCount + loadingTimeoutMs;
        int reclickCount = 0;
        while (Environment.TickCount < loadingDeadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var ra = CaptureToRectArea();
                if (TeleportLoadingDetector.IsLoadingScreen(ra.SrcMat))
                {
                    return true;
                }
                // 未进入加载页：尝试在窗口内重点按钮（最多 3 次）
                using var found = ra.Find(_assets.TeleportButtonRo);
                if (found.IsExist() && reclickCount < 3)
                {
                    found.Click();
                    reclickCount++;
                    Logger.LogDebug("[快速识别] 阶段 2 容错重点传送按钮（第 {N} 次）", reclickCount);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogDebug("[快速识别] 阶段 2 异常: {Msg}", ex.Message);
            }
            await Delay(pollMs, ct);
        }
        return false; // 阶段 2 超时：回退到 ClickTpPoint 兜底
    }

    /// <summary>
    /// 精确短延时：Task.Delay 受 Windows 系统计时器精度（默认 ~15.6ms）钳制，设 2ms 实际睡 ~15.6ms，
    /// 导致动态拖动的 StepInterval 参数对 &lt;15ms 的取值完全失效（拖动"慢且调参无感"）。
    /// 本方法用 Stopwatch + SpinWait 忙等到精确时长，让小 StepInterval 真正生效、拖动提速。
    /// 仅用于拖动循环这类几百毫秒的短程忙等，代价是这期间占用一点 CPU，可接受。
    /// ms &gt;= 15 时直接走 Task.Delay（此时精度足够，不必忙等占 CPU）。
    /// </summary>
    private async Task PreciseDelay(int ms, CancellationToken token)
    {
        if (ms <= 0) return;
        if (ms >= 15)
        {
            await Delay(ms, token);
            return;
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var target = TimeSpan.FromMilliseconds(ms);
        while (sw.Elapsed < target)
        {
            token.ThrowIfCancellationRequested();
            Thread.SpinWait(200);
        }
    }

    private int[] GenerateSteps(int delta, int steps)
    {
        double[] factors = new double[steps];
        double sum = 0;
        for (int i = 0; i < steps; i++)
        {
            factors[i] = Math.Cos(i * Math.PI / (2 * steps));
            sum += factors[i];
        }

        int[] stepsArr = new int[steps];
        int remaining = delta;

        // 两阶段分配：基础值 + 余数补偿
        for (int i = 0; i < steps; i++)
        {
            double ratio = factors[i] / sum;
            stepsArr[i] = (int)(delta * ratio); // 基础值
            remaining -= stepsArr[i];
        }

        int center = steps / 2;
        for (int r = 0; r < Math.Abs(remaining); r++)
        {
            int target = (center + r) % steps; // 从中点开始螺旋分配
            stepsArr[target] += remaining > 0 ? 1 : -1;
        }

        return stepsArr;
    }

    public Point2f GetPositionFromBigMap(string mapName, bool usePrior = true)
    {
        return GetBigMapCenterPoint(mapName, usePrior);
    }

    public Point2f? GetPositionFromBigMapNullable(string mapName, bool usePrior = true)
    {
        try
        {
            return GetBigMapCenterPoint(mapName, usePrior);
        }
        catch
        {
            return null;
        }
    }

    public Rect GetBigMapRect(string mapName)
    {
        var rect = new Rect();
        bool scrolledOnce = false;      // 滚轮兜底只做一次
        bool layerSwitchedOnce = false; // 图层切换只做一次（防止循环点击地上/地下切换按钮）
        bool zoomChangedForRecover = false; // 是否为识别把缩放临时拉到 5.5（成功后需复原）
        double savedZoomLevel = 0;      // 记录拉 5.5 前的原缩放，识别成功后复原，避免污染调用方
        NewRetry.Do(() =>
        {
            // 判断是否在地图界面
            using var ra = CaptureToRectArea();
            using var mapScaleButtonRa = ra.Find(GetQuickTeleportRecognitionObject("MapScaleButton", ra));
            if (mapScaleButtonRa.IsExist())
            {
                try
                {  
                    using var ra2 = CaptureToRectArea();
                    using var mapScaleButtonRa2 = ra2.Find(TpTaskFastDragAssets.Get(ra2).MapScaleButtonRo);
                    if (mapScaleButtonRa2.IsExist())
                    {
                        rect = MapManager.GetMap(mapName, _mapMatchingMethod).GetBigMapRect(ra.CacheGreyMat);
                    }
                }
                catch (Exception)
                {
                    rect = default; // 发生异常视为识别失败
                }
                
                if (rect == default)
                {
                    if (!scrolledOnce)
                    {
                        // 第一次识别失败：滚轮调整一次后再识别（只做一次）
                        Simulation.SendInput.Mouse.VerticalScroll(2);
                        scrolledOnce = true;
                        Sleep(500);
                    }
                    else
                    {
                        // 滚轮一次后仍失败：若在地下图层先切回地上（只切换一次，防止循环点击）；
                        // 再把缩放临时拉到 5.5 稳定识别（5.5 下画面缩小、特征更多，位置匹配更稳）。
                        // 识别成功后在方法末尾把缩放复原，避免污染 MoveMapTo 等调用方持有的当前缩放值。
                        if (!layerSwitchedOnce)
                        {
                            using var raUnder = CaptureToRectArea();
                            if (IsBigMapUndergroundViaAssets(raUnder))
                            {
                                TaskControl.Logger.LogInformation("识别大地图位置失败：检测到地下图层，切换到地上");
                                using var raSwitch = CaptureToRectArea();
                                raSwitch.Find(_assets.MapUndergroundToGroundButtonRo, rg => rg.Click());
                                layerSwitchedOnce = true;  // 标记已切换，防止重复点击
                                Sleep(300);
                            }
                        }
                        using var raZoom = CaptureToRectArea();
                        double zoomNow = GetBigMapZoomLevel(raZoom);
                        if (Math.Abs(zoomNow - 5.5) > _tpConfig.PrecisionThreshold)
                        {
                            if (!zoomChangedForRecover)
                            {
                                savedZoomLevel = zoomNow;   // 仅第一次记录原缩放
                                zoomChangedForRecover = true;
                            }
                            TaskControl.Logger.LogInformation("识别大地图位置失败：缩放临时拉到 5.5 稳定识别");
                            AdjustMapZoomLevel(zoomNow, 5.5).GetAwaiter().GetResult();
                            Sleep(300);
                        }
                    }
                    throw new RetryException("识别大地图位置失败");
                }
            }
            else
            {
                throw new RetryException("当前不在地图界面");
            }
        }, TimeSpan.FromMilliseconds(60), 20);

        // 识别结束：若曾为识别临时拉过 5.5，复原到 4.4（传送点可点击档），避免污染调用方
        if (zoomChangedForRecover && savedZoomLevel > 0)
        {
            try
            {
                using var raNow = CaptureToRectArea();
                double zoomNow = GetBigMapZoomLevel(raNow);
                // 复原到传送点可点击档（普通地图 4.4 / 霜月 3.0），不是 savedZoomLevel（1.9 下 GetBigMapRect 不稳定才触发拉 5.5）
                double targetZoom = GetDisplayTpPointZoomLevel(mapName);
                if (Math.Abs(zoomNow - targetZoom) > _tpConfig.PrecisionThreshold)
                {
                    // 先把本次按 5.5 识别到的 rect 记录下来（用于复原后与【重识别 rect】对比 + 重识别失败时的兜底）
                    var rectOn55 = rect;

                    AdjustMapZoomLevel(zoomNow, targetZoom).GetAwaiter().GetResult();
                    TaskControl.Logger.LogInformation("识别完成：缩放复原到 {Z:0.0}（传送点可点击档）", targetZoom);

                    // 🔴 关键修复：复原到目标缩放后，以当前真实缩放【重新识别一次 rect】，取代原来
                    //   "按 targetZoom/5.5 比例缩放修正 rect" 的几何猜算。
                    //   比例修正在跨缩放(5.5→4.4)时存在屏幕中心/边界锚点误差，会传导成点击坐标偏移
                    //   （点偏 → ClickTpPoint 误报"传送点未激活或不存在"）。
                    //   复原后画面已稳定在目标缩放，重新识别一次是确定性的，误差最小。
                    //   仅自救分支多此一次识别（~100-200ms），正常传送路径（zoomChangedForRecover=false）
                    //   不走这里、零开销、零速度影响。
                    //   重识别失败 → 用回 5.5 时的 rect 作为兜底（退化到比例修正的旧行为，不抛）。
                    try
                    {
                        using var raRedo = CaptureToRectArea();
                        using var mapScaleButtonRedo = raRedo.Find(TpTaskFastDragAssets.Get(raRedo).MapScaleButtonRo);
                        if (mapScaleButtonRedo.IsExist())
                        {
                            rect = MapManager.GetMap(mapName, _mapMatchingMethod).GetBigMapRect(raRedo.CacheGreyMat);
                            TaskControl.Logger.LogInformation(
                                "[自救后重识别 rect] 5.5识别={Rect55} 复原{From:0.0}→{To:0.0} 重识别={Rect}", 
                                rectOn55, zoomNow, targetZoom, rect);
                        }
                        else
                        {
                            rect = rectOn55; // 不在大地图界面，保留 5.5 的 rect 兜底
                            TaskControl.Logger.LogWarning("[自救后重识别 rect] 复原后不在大地图界面，保留 5.5 识别 rect：{Rect}", rectOn55);
                        }
                    }
                    catch (Exception exRe)
                    {
                        rect = rectOn55; // 重识别失败，用 5.5 的 rect 兜底（等同旧比例修正，安全性不降）
                        TaskControl.Logger.LogWarning("[自救后重识别 rect] 重识别失败，保留 5.5 识别 rect：{Msg}", exRe.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                // 复原缩放失败不影响已识别到的 rect：记录告警，交由调用方后续按当前缩放继续
                TaskControl.Logger.LogWarning("识别后复原缩放失败：{Msg}", ex.Message);
            }
        }

        if (rect == default)
        {
            throw new InvalidOperationException("多次重试后，识别大地图位置失败");
        }

        Debug.WriteLine("识别大地图在全地图位置矩形：" + rect);
        // 提瓦特大陆由于用的256的图，需要做特殊逻辑
        if (mapName == MapTypes.Teyvat.ToString())
        {
            const int s = TeyvatMap.BigMap256ScaleTo2048; // 相对2048做8倍缩放
            rect = new Rect(rect.X * s, rect.Y * s, rect.Width * s, rect.Height * s);
        }

        return MapManager.GetMap(mapName, _mapMatchingMethod).ConvertImageCoordinatesToGenshinMapCoordinates(rect)!.Value;
    }

    /// <summary>
    /// 打开大地图前采集第一层小地图先验（原神坐标）：主界面下截一帧小地图识别当前坐标；
    /// 失败退回 NavigationInstance 缓存坐标；都无效返回 null。
    /// 详见 .kiro/specs/teleport-bigmap-position-region-constrained-match/design.md §组件4。
    /// </summary>
    private Point2f? TryGetMiniMapPriorGenshin(string mapName)
    {
        // try
        // {
        //     // using var ra = CaptureToRectArea();
        //     // var colorMat = new Mat(ra.SrcMat, MapAssets.Instance.MimiMapRect);
        //     // var p = MapManager.GetMap(mapName, _mapMatchingMethod).GetMiniMapPosition(colorMat);
        //     // if (!p.IsEmpty())
        //     // {
        //     //     var g = MapManager.GetMap(mapName, _mapMatchingMethod).ConvertImageCoordinatesToGenshinMapCoordinates(p);
        //     //     if (g is Point2f gp) return gp;
        //     // }
        // }
        // catch (Exception ex)
        // {
        //     // 小地图先验识别失败不影响传送主流程（可恢复）：记录后退回缓存兜底
        //     Logger.LogDebug(ex, "[大地图定位] 小地图先验识别异常，退回缓存坐标");
        // }
        var (px, py) = TpMapPositionPrior.GetTpPriorPosition();  // 读传送先验专用缓存，不受 WarmUp 影响
        if (px > 0 && py > 0)
        {
            var g = MapManager.GetMap(mapName, _mapMatchingMethod).ConvertImageCoordinatesToGenshinMapCoordinates(new Point2f(px, py));
            if (g is Point2f gp)
            {
                return gp;
            }
        }
        return null;
    }

    /// <summary>
    /// 分层先验区块限定匹配 + 层内合理性校验 + 对比日志（返回 256 尺度图像坐标，
    /// 与 TeyvatMap.GetBigMapPosition 返回值同量纲，供 GetBigMapCenterPoint 后续 *8 → 转原神坐标）。
    /// 详见 .kiro/specs/teleport-bigmap-position-region-constrained-match/design.md §组件4。
    /// </summary>
    private Point2f ResolveBigMapPositionLayered(SceneBaseMap teyvat, Mat greyBigMapMat)
    {
        // 把 256 尺度结果转原神坐标（用于合理性校验/日志）：*8 → Convert
        Point2f? ToGenshin(Point2f p256)
        {
            if (p256.IsEmpty()) return null;
            var g = teyvat.ConvertImageCoordinatesToGenshinMapCoordinates(
                new Point2f(p256.X * TeyvatMap.BigMap256ScaleTo2048, p256.Y * TeyvatMap.BigMap256ScaleTo2048));
            return g;
        }

        Point2f result256 = default;

        // 第一层：小地图/缓存先验，range=100
        if (_miniMapPriorGenshin is Point2f c1)
        {
            double layer1Range = _priorIsRegionCenter
                ? BigMapPriorMatchDecisions.RegionCenterRangeGenshin
                : BigMapPriorMatchDecisions.Layer1RangeGenshin;
            Point2f r256;
            try
            {
                r256 = TpMapRegionMatch.GetBigMapPositionInRange(teyvat, greyBigMapMat, c1, layer1Range);
            }
            catch (Exception ex)
            {
                // 区块内特征点不足（如沙漠低特征区）→ FindHomography 抛异常。视为该层失败，降级下一层。
                Logger.LogDebug("[大地图定位] 第一层区块匹配异常(特征点不足)，降级: {Msg}", ex.Message);
                r256 = default;
            }
            var g = ToGenshin(r256);
            bool acc1 = g is Point2f gpChk1
                && BigMapPriorMatchDecisions.IsResultAcceptable(false, gpChk1, c1, layer1Range);
            if (g is Point2f gpL1)
            {
                double distL1 = BigMapPriorMatchDecisions.Distance(gpL1, c1);
                Logger.LogDebug("[诊断-分层先验] 第一层 中心=({CX:0},{CY:0}) 半径={R:0} 结果=({RX:0},{RY:0}) 距中心={D:0} 采纳={Acc}",
                    c1.X, c1.Y, layer1Range, gpL1.X, gpL1.Y, distL1, acc1);
            }
            else
                Logger.LogDebug("[诊断-分层先验] 第一层 中心=({CX:0},{CY:0}) 半径={R:0} 结果=空", c1.X, c1.Y, layer1Range);
            if (g is Point2f gp && acc1)
            {
                result256 = r256;
            }
        }

        // 第二层：目标传送点先验，range=500
        if (result256.IsEmpty() && _targetPriorGenshin is Point2f c2)
        {
            Point2f r256;
            try
            {
                r256 = TpMapRegionMatch.GetBigMapPositionInRange(teyvat, greyBigMapMat, c2, BigMapPriorMatchDecisions.Layer2RangeGenshin);
            }
            catch (Exception ex)
            {
                // 区块内特征点不足 → FindHomography 抛异常。视为该层失败，降级全图兜底。
                Logger.LogDebug("[大地图定位] 第二层区块匹配异常(特征点不足)，降级: {Msg}", ex.Message);
                r256 = default;
            }
            var g = ToGenshin(r256);
            bool acc2 = g is Point2f gpChk2
                && BigMapPriorMatchDecisions.IsResultAcceptable(false, gpChk2, c2, BigMapPriorMatchDecisions.Layer2RangeGenshin);
            if (g is Point2f gpL2)
            {
                double distL2 = BigMapPriorMatchDecisions.Distance(gpL2, c2);
                Logger.LogDebug("[诊断-分层先验] 第二层 中心=({CX:0},{CY:0}) 半径={R:0} 结果=({RX:0},{RY:0}) 距中心={D:0} 采纳={Acc}",
                    c2.X, c2.Y, BigMapPriorMatchDecisions.Layer2RangeGenshin, gpL2.X, gpL2.Y, distL2, acc2);
            }
            else
                Logger.LogDebug("[诊断-分层先验] 第二层 中心=({CX:0},{CY:0}) 半径={R:0} 结果=空", c2.X, c2.Y, BigMapPriorMatchDecisions.Layer2RangeGenshin);
            if (g is Point2f gp && acc2)
            {
                result256 = r256;
            }
        }

        // 最终兜底：全图盲搜（旧行为）
        if (result256.IsEmpty())
        {
            Logger.LogWarning("[大地图定位] 前两层先验均未采纳，进入全图盲搜兜底（此路径存在自相似区误识别风险）");
            var full = teyvat.GetBigMapPosition(greyBigMapMat);
            var fullG = ToGenshin(full);
            Logger.LogDebug("[诊断-分层先验] 全图盲搜结果=({FX:0},{FY:0})", fullG is Point2f fg ? fg.X : 0, fullG is Point2f fg2 ? fg2.Y : 0);
            result256 = full;
        }

        return result256;
    }

    /// <summary>
    /// 拖动滑动窗口先验识别：以 dragCenter 为中心、radiusGenshin 为半径做区块限定匹配。
    /// 匹配到且距中心≤半径 → 采用；否则/异常 → 降级全图盲搜。返回 256 尺度图像坐标。
    /// </summary>
    private Point2f ResolveDragPriorPosition(SceneBaseMap teyvat, Mat greyBigMapMat, Point2f dragCenter, double radiusGenshin)
    {
        Point2f? ToGenshin(Point2f p256)
        {
            if (p256.IsEmpty()) return null;
            return teyvat.ConvertImageCoordinatesToGenshinMapCoordinates(
                new Point2f(p256.X * TeyvatMap.BigMap256ScaleTo2048, p256.Y * TeyvatMap.BigMap256ScaleTo2048));
        }

        Point2f r256 = default;
        try
        {
            r256 = TpMapRegionMatch.GetBigMapPositionInRange(teyvat, greyBigMapMat, dragCenter, radiusGenshin);
        }
        catch (Exception ex)
        {
            Logger.LogDebug("[大地图定位] 拖动先验区块匹配异常(特征点不足)，降级全图: {Msg}", ex.Message);
            r256 = default;
        }

        var g = ToGenshin(r256);
        bool acc = g is Point2f gp
            && BigMapPriorMatchDecisions.IsResultAcceptable(false, gp, dragCenter, radiusGenshin);

        // 【诊断】拖动先验局部搜索结果
        if (g is Point2f gpLog)
        {
            double distLog = BigMapPriorMatchDecisions.Distance(gpLog, dragCenter);
            Logger.LogDebug("[诊断-拖动先验] 中心=({CX:0},{CY:0}) 半径={R:0} 局部搜索结果=({RX:0},{RY:0}) 距中心={D:0} 采纳={Acc}",
                dragCenter.X, dragCenter.Y, radiusGenshin, gpLog.X, gpLog.Y, distLog, acc);
        }
        else
        {
            Logger.LogDebug("[诊断-拖动先验] 中心=({CX:0},{CY:0}) 半径={R:0} 局部搜索结果=空 采纳=false",
                dragCenter.X, dragCenter.Y, radiusGenshin);
        }

        if (g is Point2f gpFinal && acc)
        {
            return r256;
        }

        // 降级全图盲搜
        var full256 = teyvat.GetBigMapPosition(greyBigMapMat);
        var fullG = ToGenshin(full256);
        Logger.LogDebug("[诊断-拖动先验] 降级全图盲搜结果=({FX:0},{FY:0})",
            fullG is Point2f fg ? fg.X : 0, fullG is Point2f fg2 ? fg2.Y : 0);
        return full256;
    }

    /// <summary>
    /// 非提瓦特"先按先验坐标在该图找一次"（不切块）：把先验原神坐标转本图图像坐标，
    /// 从本图主层特征点里筛出先验附近（半径内）的特征子集做 SIFT 匹配，近似"先验局部优先"。
    /// 只读 scene 特征（TrainKeyPoints/TrainDescriptors），不改任何共享状态 → 公版零影响。
    /// 失败/特征不足/先验越界 → 返回 default，由调用方降级全图（旧行为）。
    /// </summary>
    private Point2f TryBigMapPriorLocalMatch(SceneBaseMap scene, Mat greyBigMapMat, Point2f priorGenshin)
    {
        try
        {
            var layer = scene.Layers[0];
            if (layer.TrainKeyPoints.Length == 0 || layer.TrainDescriptors.Empty())
            {
                return default;
            }

            // 先验原神坐标 → 本图图像坐标；越界（跨任务陈旧/垃圾坐标）→ 返回空走全图
            var centerImg = scene.ConvertGenshinMapCoordinatesToImageCoordinates(priorGenshin);
            if (float.IsNaN(centerImg.X) || float.IsNaN(centerImg.Y)
                || centerImg.X < 0 || centerImg.Y < 0
                || centerImg.X > scene.MapSize.Width || centerImg.Y > scene.MapSize.Height)
            {
                return default;
            }

            // 筛选半径（图像坐标）：先验通常=上次落点≈当前大地图中心附近；半径取屏幕内常见视野（约 300~500 原神距离）
            double rImg = BigMapPriorMatchDecisions.Layer2RangeGenshin * scene.MapImageBlockWidthScale;

            // 按坐标筛出先验附近的特征点索引（KeyPoint 自带坐标，无需切块）
            var idx = new List<int>();
            for (int i = 0; i < layer.TrainKeyPoints.Length; i++)
            {
                var kp = layer.TrainKeyPoints[i].Pt;
                if (Math.Abs(kp.X - centerImg.X) <= rImg && Math.Abs(kp.Y - centerImg.Y) <= rImg)
                {
                    idx.Add(i);
                }
            }
            if (idx.Count < 7)
            {
                Logger.LogDebug("[大地图定位] 非提瓦特先验附近特征不足({N})，降级全图", idx.Count);
                return default;
            }

            // 组装特征子集 + 对应描述子行
            var subKps = new KeyPoint[idx.Count];
            using var subDesc = new Mat(idx.Count, layer.TrainDescriptors.Cols, MatType.CV_32FC1);
            for (int j = 0; j < idx.Count; j++)
            {
                subKps[j] = layer.TrainKeyPoints[idx[j]];
                layer.TrainDescriptors.Row(idx[j]).CopyTo(subDesc.Row(j));
            }

            Logger.LogDebug("[大地图定位] 非提瓦特先验局部匹配 map={Map} 先验=({PX:0},{PY:0}) 半径={R:0} 特征数={N}",
                scene.Type, priorGenshin.X, priorGenshin.Y, rImg, idx.Count);
            return scene.SiftMatcher.Match(subKps, subDesc, greyBigMapMat);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogDebug(ex, "[大地图定位] 非提瓦特先验局部匹配异常，降级全图: {Msg}", ex.Message);
            return default;
        }
    }

    public Point2f GetBigMapCenterPoint(string mapName, bool usePrior = true)
    {
        Point2f p = new Point2f();
        bool inMapUi = false;

        // 大地图可能打开较慢，重试 5 次、每次间隔 100 毫秒，直到识别到非空位置
        for (int attempt = 0; attempt < 10; attempt++)
        {
            // 判断是否在地图界面
            using var ra = CaptureToRectArea();
            using var mapScaleButtonRa = ra.Find(TpTaskFastDragAssets.Get(ra).MapScaleButtonRo);
            if (mapScaleButtonRa.IsExist())
            {
                inMapUi = true;
                try
                {
                    var scene = MapManager.GetMap(mapName, _mapMatchingMethod);
                    // 拖动滑动窗口先验（拖动循环专用，最高优先级）：中心=预测位置，半径=预测移动距离*2
                    if (mapName == MapTypes.Teyvat.ToString()
                        && (scene is TeyvatMap || scene is TeyvatMapTest)
                        && scene is SceneBaseMap sceneDrag
                        && _dragPriorCenterGenshin is Point2f dragCenter
                        && _dragPriorRadiusGenshin > 0)
                    {
                        p = ResolveDragPriorPosition(sceneDrag, ra.CacheGreyMat, dragCenter, _dragPriorRadiusGenshin);
                    }
                    // 拖动先验已清空但 _lastDragCenterGenshin 有值：用保存的最终中心点再试一次
                    else if (mapName == MapTypes.Teyvat.ToString()
                        && (scene is TeyvatMap || scene is TeyvatMapTest)
                        && scene is SceneBaseMap sceneLastDrag
                        && _lastDragCenterGenshin is Point2f lastCenter)
                    {
                        p = ResolveDragPriorPosition(sceneLastDrag, ra.CacheGreyMat, lastCenter, 2500);
                    }
                    // 提瓦特大地图(真实类型) + 至少有一层先验 + usePrior=true → 分层区块限定；否则逐字节走旧全图路径
                    else if (mapName == MapTypes.Teyvat.ToString()
                        && (scene is TeyvatMap || scene is TeyvatMapTest)
                        && scene is SceneBaseMap sceneBase
                        && (_miniMapPriorGenshin != null || _targetPriorGenshin != null)
                        && usePrior)
                    {
                        p = ResolveBigMapPositionLayered(sceneBase, ra.CacheGreyMat);
                    }
                    // 非提瓦特 + 至少一层先验 + usePrior → 先按先验坐标在该图找一次"（不切块，直接筛坐标附近特征子集匹配），
                    // 找不到再降级全图。只读 scene 特征，不改任何共享状态 → 公版零影响。
                    else if (mapName != MapTypes.Teyvat.ToString()
                        && scene is SceneBaseMap sceneNonTeyvat
                        && (_miniMapPriorGenshin is Point2f mpNonTeyvat || _targetPriorGenshin is Point2f tpNonTeyvat)
                        && usePrior)
                    {
                        var prior = _miniMapPriorGenshin ?? _targetPriorGenshin;
                        p = TryBigMapPriorLocalMatch(sceneNonTeyvat, ra.CacheGreyMat, prior!.Value);
                        if (p.IsEmpty())
                        {
                            p = sceneNonTeyvat.GetBigMapPosition(ra.CacheGreyMat); // 先验附近没找到 → 全图兜底
                        }
                    }
                    else
                    {
                        // 分层先验未启用（非提瓦特 / 或两层先验均为 null / 或 usePrior=false）→ 走全图旧路径
                        Logger.LogInformation("[大地图定位] 未启用分层先验，走全图旧路径: map={Map} 第一层先验={M} 第二层先验={T}",
                            mapName,
                            _miniMapPriorGenshin is Point2f mp ? $"({mp.X:0},{mp.Y:0})" : "无",
                            _targetPriorGenshin is Point2f tp ? $"({tp.X:0},{tp.Y:0})" : "无");
                        p = scene.GetBigMapPosition(ra.CacheGreyMat); // 旧行为，逐字节不变
                    }
                }
                catch (Exception ex)
                {
                    throw new MapPositionNotRecognizedException("大地图特征点匹配引发异常：" + ex.Message, ex);
                }

                if (!p.IsEmpty())
                {
                    break;
                }
            }

            if (attempt < 4)
            {
                Thread.Sleep(70);
            }
        }

        if (!inMapUi)
        {
            Simulation.SendInput.Mouse.LeftButtonUp();
            throw new InvalidOperationException("当前不在地图界面");
        }

        if (p.IsEmpty())
        {
            Simulation.SendInput.Mouse.LeftButtonUp();
            throw new MapPositionNotRecognizedException("大地图特征点匹配识别位置失败");
        }

        Debug.WriteLine("识别大地图在全地图位置：" + p);
        // 提瓦特大陆由于用的256的图，需要做特殊逻辑
        var (x, y) = (p.X, p.Y);
        if (mapName == MapTypes.Teyvat.ToString())
        {
            (x, y) = (p.X * TeyvatMap.BigMap256ScaleTo2048, p.Y * TeyvatMap.BigMap256ScaleTo2048);
        }

        return MapManager.GetMap(mapName, _mapMatchingMethod).ConvertImageCoordinatesToGenshinMapCoordinates(new Point2f(x, y))!.Value;
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
            foreach (var (country, position) in MapLazyAssets.Get().CountryPositions)
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
        return MapLazyAssets.Get().ScenesDic[mapName].Points
            .OrderBy(tp => Math.Pow(tp.X - x, 2) + Math.Pow(tp.Y - y, 2))
            .Take(n)
            .ToList();
    }

    private static IReadOnlyList<SpecialAdjacentTpPoint>? _userSpecialListCache;
    private static bool _userSpecialListLoaded;

    /// <summary>
    /// 返回用于命中判定的合并清单 = 内置硬编码清单 + 用户 JSON 清单（内存拼接，零额外 IO）。
    /// 内置清单恒在（开箱即用）；用户清单懒加载缓存，缺失/坏 JSON → 空清单（不影响内置）。
    /// 详见 .kiro/specs/teleport-adjacent-point-misclick-zoom-whitelist-fix/design.md §组件 3。
    /// </summary>
    private IReadOnlyList<SpecialAdjacentTpPoint> GetSpecialAdjacentTpPointList()
    {
        var user = GetUserSpecialAdjacentTpPointList();
        var builtins = SpecialAdjacentTpPointBuiltins.List;

        // 常见情况：用户清单为空 → 直接返回内置列表引用，零分配
        if (user.Count == 0)
        {
            return builtins;
        }

        // 合并（内置在前，用户在后）。清单极小，一次性内存拼接。
        var merged = new List<SpecialAdjacentTpPoint>(builtins.Count + user.Count);
        merged.AddRange(builtins);
        merged.AddRange(user);
        return merged;
    }

    /// <summary>用户 JSON 清单：懒加载 + 缓存，兜底不抛异常。程序只读，绝不写/删该文件。</summary>
    private IReadOnlyList<SpecialAdjacentTpPoint> GetUserSpecialAdjacentTpPointList()
    {
        if (_userSpecialListLoaded)
        {
            return _userSpecialListCache ?? System.Array.Empty<SpecialAdjacentTpPoint>();
        }
        _userSpecialListLoaded = true;
        try
        {
            var path = BetterGenshinImpact.Core.Config.Global.Absolute(@"User\AutoTrackPath\special_adjacent_tp_points.json");
            if (!System.IO.File.Exists(path))
            {
                // 文件缺失是正常情况（多数用户只用内置清单）→ 用户来源视为空，不抛异常。
                // 注意：程序绝不自动创建此文件（内置点已由硬编码保证"自带"）。
                _userSpecialListCache = System.Array.Empty<SpecialAdjacentTpPoint>();
                return _userSpecialListCache;
            }
            var json = System.IO.File.ReadAllText(path);
            _userSpecialListCache = System.Text.Json.JsonSerializer
                .Deserialize<List<SpecialAdjacentTpPoint>>(json)
                ?? new List<SpecialAdjacentTpPoint>();
        }
        catch (Exception ex)
        {
            // 解析失败：结构化告警 + 用户来源降级为空清单（内置清单仍生效）。
            // 绝不因用户清单坏掉而中断传送（可恢复，仍走内置 + 原路径）。
            TaskControl.Logger.LogWarning(ex, "用户特殊相邻传送点清单加载失败，本次运行仅按内置清单处理（不影响传送）");
            _userSpecialListCache = System.Array.Empty<SpecialAdjacentTpPoint>();
        }
        return _userSpecialListCache;
    }

    public async Task<bool> SwitchRecentlyCountryMap(double x, double y, string? forceCountry = null)
    {
        // 可能是地下地图，切换到地上地图
        using var ra2 = CaptureToRectArea();
        if (IsBigMapUndergroundViaAssets(ra2))
        {
            using var ra3 = CaptureToRectArea();
            ra3.Find(_assets.MapUndergroundToGroundButtonRo, rg => rg.Click());
            await Delay(170, ct);
        }

        // 识别当前位置
        // 第一次识别可能因地图刚打开特征点未渲染而失败 → 短轮询补救（最多 ~450ms）。
        // fast-drag-recognition-acceleration spec / SwitchRecentlyCountryMap regression safety net：
        // 防止"识别失败 → minDistance 保持 MaxValue → 误走 SwitchArea 弯路（即使传送点就在旁边）"
        var minDistance = double.MaxValue;
        Point2f? bigMapCenterPointNullable = GetPositionFromBigMapNullable(MapTypes.Teyvat.ToString());
        if (bigMapCenterPointNullable == null)
        {
            for (int i = 0; i < 3 && bigMapCenterPointNullable == null; i++)
            {
                await Delay(150, ct);
                bigMapCenterPointNullable = GetPositionFromBigMapNullable(MapTypes.Teyvat.ToString());
            }
        }

        if (bigMapCenterPointNullable != null)
        {
            var bigMapCenterPoint = bigMapCenterPointNullable.Value;
            TaskControl.Logger.LogDebug("识别当前大地图位置：{Pos}", bigMapCenterPoint);
            minDistance = Math.Sqrt(Math.Pow(bigMapCenterPoint.X - x, 2) + Math.Pow(bigMapCenterPoint.Y - y, 2));
            if (minDistance < 50)
            {
                // TaskControl.Logger.LogError("地图位置已经在传送点附近，不切换");
                // 点位很近的情况下不切换
                return false;
            }
        }
        
        string minCountry = "当前位置";
        foreach (var (country, position) in MapLazyAssets.Get().CountryPositions)
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

    /// <summary>
    /// 地区菜单模板匹配优先：命中则返回模板命中区（Rect），未命中 / 阈值不过 / 模板缺失返回 null（OCR 兜底）。
    /// switch-area-template-match spec：SwitchArea 用模板像素级定位替代 OCR，加快切换；失效静默回落 OCR。
    /// </summary>
    private Rect? TryMatchSwitchAreaTemplate(ImageRegion ra, string areaName)
    {
        // 模板不绑定格子（菜单 2×8 网格、每次显示 1~16 个地区、顺序可能乱）：
        // 遍历 16 个菜单格子，用目标地区模板逐格匹配，取最高分且过阈值的命中格。
        // 命中 → 返回该格矩形；模板缺失 / 未在菜单上出现 / 全不过阈值 → null → OCR 兜底。
        // 与旧"整块右 1/3 搜索"相比，分隔到单格搜索让格内背景干净，CCoeffNormed 匹配度显著提升，避免掉进 OCR。
        return _switchAreaRegionAssets.MatchInAllCells(ra, areaName);
    }

    internal async Task SwitchArea(string areaName)
    {
        GameCaptureRegion.GameRegionClick((rect, scale) => (rect.Width - 80 * scale, rect.Height - 62 * scale));
        
        // 加速识别模式：等地区菜单弹出（白色 X 关闭按钮出现），兜底 300ms 与旧 Delay 等值。
        // MapCloseButtonWhiteRo = 弹出层（含地区菜单）的白色 X 关闭按钮。
        // fast-drag-recognition-acceleration spec / SwitchArea menu popup optimization
        await Delay(ApplyExtraDelay(100), ct);
        var systemInfo = TaskContext.Instance().SystemInfo;
        var captureRect = systemInfo.ScaleMax1080PCaptureRect;
        await WaitForElementOrTimeoutAsync(TpTaskFastDragAssets.Get(captureRect.Width, captureRect.Height).MapCloseButtonWhiteRo, timeoutMs:ApplyExtraDelay(1000));
        
        await Delay(ApplyExtraDelay(50), ct);
        
        using var ra = CaptureToRectArea();
        // —— 新增：地区模板匹配优先（switch-area-template-match spec）——
        // 命中 → 用模板命中区点击；未命中 / 阈值不过 / 模板缺失 → 走现有 OCR 分支（逐字节保留）。
        Rect? templateHit = TryMatchSwitchAreaTemplate(ra, areaName);
        Region? matchRect;
        if (templateHit.HasValue)
        {
            matchRect = ra.DeriveCrop(templateHit.Value);
            TaskControl.Logger.LogInformation("切换区域（模板匹配）：{Country}", areaName);
        }
        else
        {
            // —— 现有 OCR 分支（逻辑逐字节保留）——
            TaskControl.Logger.LogInformation("切换区域（OCR）：{Country}", areaName);
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
        matchRect = list.OrderByDescending(r => r.Y).FirstOrDefault(r => r.Text.Contains(minCountryLocalized));
        }
        if (matchRect == null)
        {
            Logger.LogWarning("切换区域失败：{Country}", areaName);
            if (areaName == MapTypes.TheChasm.GetDescription() || areaName == MapTypes.Enkanomiya.GetDescription() || areaName == MapTypes.SeaOfBygoneEras.GetDescription() || areaName == MapTypes.AncientSacredMountain.GetDescription() || areaName == MapTypes.TempleOfSpace.GetDescription())
            {
                throw new Exception($"切换独立地图区域[{areaName}]失败");
            }
            // 非独立地图也抛出异常，不再静默恢复
            throw new Exception($"切换区域[{areaName}]失败");
        }
        else
        {
            matchRect.Click();
            TaskControl.Logger.LogInformation("切换到区域：{Country}", areaName);
            // 切换区域后，用该区域的固定中心点作为第一层先验（切换后大地图自动跳到区域中心）
            if (MapLazyAssets.Get().CountryPositions.TryGetValue(areaName, out var centerPos))
            {
                _miniMapPriorGenshin = new Point2f((float)centerPos[0], (float)centerPos[1]);
                _priorIsRegionCenter = true; // 区域中心点先验，用较大半径200
            }
            else
            {
                _miniMapPriorGenshin = null; // 未知区域，清空先验
                _priorIsRegionCenter = false;
            }
            _targetPriorGenshin = null; // 第二层先验清空（切换后与上次传送目标无关）
        }

        // 层岩巨渊渲染较慢，额外等待100ms
        if (areaName == "层岩巨渊")
        {
            await Delay(100, ct);
        }

        // 加速识别模式：等地图视区像素稳定即继续，兜底 500ms（与旧 Delay 等值）
        // fast-drag-recognition-acceleration spec / SwitchArea tail wait optimization
        await Delay(ApplyExtraDelay(100), ct);
        await WaitMapStableOrTimeoutAsync(timeoutMs: ApplyExtraDelay(500));
    }

    public async Task ClickTpPoint(ImageRegion imageRegion)
    {
        // 1.判断是否在地图界面
        if (!IsInBigMapUiViaAssets(imageRegion)) throw new RetryException("不在地图界面");

        // 2. 判断是否已经点出传送按钮
        var hasTeleportButton = CheckTeleportButton(imageRegion);
        if (hasTeleportButton) return;   // 可以传送了，结束
        // 3. 没点出传送按钮，且不存在外部地图关闭按钮
        // 说明只有两种可能，a. 点出来的是未激活传送点或者标点 b. 选择传送点选项列表
        var mapCloseRa1 = imageRegion.Find(GetQuickTeleportRecognitionObject("MapCloseButton", imageRegion));
        if (!mapCloseRa1.IsEmpty()) throw new TpPointNotActivate("传送点未激活或不存在");

        // 4. 循环判断选项列表是否有传送点(未激活点位也在里面)
        var hasMapChooseIcon = CheckMapChooseIcon(imageRegion);
        // 没有传送点说明不是传送点
        if (!hasMapChooseIcon) throw new TpPointNotActivate("选项列表不存在传送点");
        var teleportButtonFound = await NewRetry.WaitForElementAppear(
            GetQuickTeleportRecognitionObject("TeleportButton"),
            () => { },
            ct,
            6,
            300
        );
        if (!teleportButtonFound) throw new TpPointNotActivate("选项列表的传送点未激活");
        await NewRetry.WaitForElementDisappear(
            GetQuickTeleportRecognitionObject("TeleportButton"),
            screen =>
            {
                screen.Find(GetQuickTeleportRecognitionObject("TeleportButton", screen), ra =>
                {
                    ra.Click();
                    ra.Dispose();
                });
            },
            ct,
            6,
            300
        );
    }

    private bool CheckTeleportButton(ImageRegion imageRegion)
    {
        var hasTeleportButton = false;
        imageRegion.Find(GetQuickTeleportRecognitionObject("TeleportButton", imageRegion), ra =>
        {
            ra.Click();
            hasTeleportButton = true;
        });
        return hasTeleportButton;
    }

    /// <summary>
    /// 全匹配一遍并进行文字识别
    /// 60ms ~200ms
    /// </summary>
    /// <param name="imageRegion"></param>
    /// <returns></returns>
    private bool CheckMapChooseIcon(ImageRegion imageRegion)
    {
        var isHdrCapture = TaskContext.Instance().Config.CaptureMode == nameof(CaptureModes.WindowsGraphicsCaptureHdr);
        var hasMapChooseIcon = false;

        // 全匹配一遍
        using var mapChooseIconRoi = imageRegion.CacheGreyMat[_assets.MapChooseIconRoi].Clone();
        var rResultList = MatchTemplateHelper.MatchMultiPicForOnePic(mapChooseIconRoi, _assets.MapChooseIconGreyMatList, isHdrCapture ? 0.7 : 0.8);
        // 按高度排序
        if (rResultList.Count > 0) {
           
            rResultList = [.. rResultList.OrderBy(x => x.Y)];
            // 点击最高的
            foreach (var iconRect in rResultList)
            {
                // 200宽度的文字区域
                using var ra = imageRegion.DeriveCrop(_assets.MapChooseIconRoi.X + iconRect.X + iconRect.Width, _assets.MapChooseIconRoi.Y + iconRect.Y - 8, 200, iconRect.Height + 16);
                using var textRegion = ra.Find(new RecognitionObject
                {
                    // RecognitionType = RecognitionTypes.Ocr,
                    RecognitionType = isHdrCapture ? RecognitionTypes.Ocr : RecognitionTypes.ColorRangeAndOcr,
                    LowerColor = new Scalar(249, 249, 249), // 只取白色文字
                    UpperColor = new Scalar(255, 255, 255),
                });
                if (string.IsNullOrEmpty(textRegion.Text) || textRegion.Text.Length == 1)
                {
                    continue;
                }

                TaskControl.Logger.LogInformation("传送：点击 {Option}", textRegion.Text.Replace(">", ""));
                Thread.Sleep(200);
                ra.Click();
                hasMapChooseIcon = true;
                break;
            }
        }

        return hasMapChooseIcon;
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
        // 失败重试：原实现是死代码——底层 Bv.GetBigMapScale 在找不到缩放按钮时直接 throw，
        // for 循环里 if(s>0) break 与「重试中」日志永远走不到，第一次就抛普通 Exception，
        // 打断传送。现改为：捕获 throw 并重新截图重试（避免同一帧失败画面反复重试），
        // 仍全部失败则返回显示档兜底值，绝不再向外裸抛普通 Exception 中断（复苏路径）。
        //
        // 兜底值语义（DisplayTpPointZoomLevel=4.4，普通地图显示档）：
        //   - 普通地图：命中各调用点 if(zoomNow > display + threshold) 判据的反面 → 跳过本轮缩放微调，行为安全；
        //   - 霜月/旧日之海：4.4 > 3.0 会触发一次「降到 ≤3.0」调整，而这两个地图的传送点本就仅 ≤3.0 渲染，
        //     调整方向正确；
        //   - 原实现失败时返回 6.0（最大缩小），那才是真正有害的（强制触发放大/认偏），本改动一并修正。
        for (int i = 0; i < 3; i++)
        {
            using var ra = CaptureToRectArea();
            try
            {
                double s = GetBigMapScaleViaFastDragAssets(ra);
                // 用快速传送自持 TpTaskFastDragAssets.MapScaleButtonRo（FastDrag 资源）读取归一化缩放。
                // 方案 X：不改共享 BvStatus；快速传送缩放检测自包含（teleport-dual-engine-asset-separation spec）。
                // 返回 0~1 滑轨归一化位置，0 是合法边界值，对应最终缩放等级 6。
                // 未识别时该方法会抛异常，由外层 catch 负责重试，因此这里不再用 s>0 判断成功。
                return (-5 * s) + 6;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                TaskControl.Logger.LogWarning("获取大地图缩放级别失败（{Attempt}/3），原因：{Msg}，重试中...", i + 1, ex.Message);
                TaskControl.Logger.LogDebug(ex, "GetBigMapScale 失败详情（{Attempt}/3）", i + 1);
            }
            Thread.Sleep(100);
        }

        TaskControl.Logger.LogWarning("获取大地图缩放级别连续失败，返回显示档兜底值 {Zoom:0.00}，跳过本轮缩放微调", DisplayTpPointZoomLevel);
        return DisplayTpPointZoomLevel;
    }

    /// <summary>
    /// 用快速传送自持资产（TpTaskFastDragAssets.MapUndergroundSwitchButtonRo，FastDrag 资源）检测是否处于地下图层。
    /// 方案 A：不改共享 BvStatus.BigMapIsUnderground（公版用公版 key/图）；快速传送探测自包含，避免恢复公版后失效。
    /// </summary>
    /// <param name="region">当前截图。</param>
    /// <returns>FastDrag 的地下图层开关按钮存在则 true（当前在地下）。</returns>
    private bool IsBigMapUndergroundViaAssets(ImageRegion region)
    {
        using var ra = region.Find(_assets.MapUndergroundSwitchButtonRo);
        return ra.IsExist();
    }

    /// <summary>
    /// 用快速传送自持资产（TpTaskFastDragAssets.MapScaleButtonRo，FastDrag 资源）判断是否在大地图界面。
    /// 快速传送完全使用自己的资源，不依赖共享公版 Bv.IsInBigMapUi（其内部用公版 MapScaleButton key）。
    /// </summary>
    /// <param name="region">当前截图。</param>
    /// <returns>FastDrag 的缩放滑轨按钮存在则 true（在大地图界面）。</returns>
    private bool IsInBigMapUiViaAssets(ImageRegion region)
    {
        using var scaleRa = region.Find(_assets.MapScaleButtonRo);
        return scaleRa.IsExist();
    }

    /// <summary>
    /// 用快速传送自持资产（TpTaskFastDragAssets.MapScaleButtonRo，FastDrag 资源）计算大地图缩放滑轨归一化位置。
    /// 方案 X：不改共享 BvStatus；快速传送缩放检测自包含（teleport-dual-engine-asset-separation spec）。
    /// 公式与 Bv.GetBigMapScale 逐字节等价；唯一差异是识别滑块用的 RecognitionObject 换成 _assets.MapScaleButtonRo（FastDrag）。
    /// </summary>
    /// <param name="region">当前截图。</param>
    /// <returns>0~1 的滑轨归一化位置（未识别时抛异常，由调用方 GetBigMapZoomLevel 的重试循环捕获）。</returns>
    private double GetBigMapScaleViaFastDragAssets(ImageRegion region)
    {
        using var scaleRa = region.Find(_assets.MapScaleButtonRo);  // TpTaskFastDragAssets，已改为 FastDrag
        if (scaleRa.IsEmpty())
        {
            throw new Exception("当前未处于大地图界面，不能使用GetBigMapScale方法");
        }

        var start = TaskContext.Instance().Config.TpConfig.ZoomStartY;
        var end = TaskContext.Instance().Config.TpConfig.ZoomEndY;
        if (end <= start)
        {
            throw new InvalidOperationException($"大地图缩放区间配置无效：start={start}, end={end}");
        }

        var cur = (scaleRa.Y + scaleRa.Height / 2.0) * _zoomOutMax1080PRatio;  // 与 Bv.GetBigMapScale 同公式（转换到1080p坐标系）
        var normalizedScale = (end - cur) / (end - start);
        if (!double.IsFinite(normalizedScale))
        {
            throw new InvalidOperationException($"大地图缩放识别结果无效：start={start}, end={end}, current={cur}");
        }

        // 0 和 1 都是合法的边界值：滑块在最下方时 normalizedScale=0，对应最终缩放等级 6。
        return Math.Clamp(normalizedScale, 0.0, 1.0);
    }

    /// <summary>
    /// 计算第 attempt 次尝试点击传送点时应使用的"可点击缩放"目标级别。
    /// 缩放语义：值越小越放大（图标越大越易点出传送按键）。在 [minZoom, displayZoom] 区间内
    /// 随尝试序号收敛——attempt 0 用 displayZoom(4.4)，后续逐步朝 minZoom 放大，
    /// 使每次重试都换一个不同的、未被证明失败的缩放档位。
    /// 详见 .kiro/specs/teleport-wrong-zoom-no-teleport-button-fix/design.md §2.1。
    /// 纯函数：无 UI / Mat / logger 依赖，便于 PBT 撒输入。
    /// </summary>
    /// <param name="attempt">尝试序号（0 起，对应 Tp 的 retryTimes/i）</param>
    /// <param name="displayZoom">传送点显示缩放（DisplayTpPointZoomLevel=4.4）</param>
    /// <param name="minZoom">最放大可点击下限（TpConfig.MinZoomLevel，默认 2.0）</param>
    /// <returns>夹在 [minZoom, displayZoom] 的目标缩放</returns>
    public static double ComputeClickZoomCandidate(int attempt, double displayZoom, double minZoom)
    {
        // 防御：保证 lo <= hi（displayZoom/minZoom 顺序异常时不抛）
        double hi = Math.Max(displayZoom, minZoom);
        double lo = Math.Min(displayZoom, minZoom);
        if (attempt <= 0) return hi;            // 第 0 次：传送点显示缩放
        // 总尝试数固定 3（Tp 的 for i<3）→ 候选点 hi, 中点, lo
        const int totalAttempts = 3;
        int clamped = Math.Min(attempt, totalAttempts - 1);
        double t = (double)clamped / (totalAttempts - 1); // attempt1→0.5, attempt2→1.0
        return hi - (hi - lo) * t;              // 朝 lo（更放大）线性收敛
    }

    /// <summary>
    ///     判定最终点击前是否需要把缩放收敛回传送点可渲染档位。
    ///     普通传送点图标仅在缩放 ≤ displayZoom(4.4) 时渲染；若点击前缩放停在更高档
    ///     （典型 5.5，来自异常/重试路径未完全降档），点击会落在不渲染图标的空位 → 传送失败。
    ///     纯函数：无 UI / Mat / logger / 状态依赖，同输入恒同输出，便于 PBT 撒输入。
    ///     详见 .kiro/specs/teleport-final-click-zoom-not-collapsed-click-miss-fix/design.md 组件 A。
    /// </summary>
    /// <param name="currentZoom">点击前由 GetBigMapZoomLevel 读取的当前大地图缩放级别。</param>
    /// <param name="displayZoom">传送点显示缩放上界（DisplayTpPointZoomLevel = 4.4）。</param>
    /// <param name="precisionThreshold">缩放比较容差（_tpConfig.PrecisionThreshold）。</param>
    /// <returns>currentZoom 高于 displayZoom + precisionThreshold 时返回 true（需降档），否则 false。</returns>
    public static bool ShouldCollapseZoomBeforeClick(double currentZoom, double displayZoom, double precisionThreshold)
    {
        return currentZoom > displayZoom + precisionThreshold;
    }
}
