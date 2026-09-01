using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoTrackPath.Model;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

/// <summary>
/// 传送任务分发器：根据 UseOfficialTeleport 配置，在公版传送和快速拖动传送之间切换。
/// 公版传送（TpTaskOfficial）：来自上游 fix(tp): optimize map teleport targeting (#3258)。
/// 快速拖动传送（TpTaskFastDrag）：茶包版快速拖动模式。
/// 切换开关：设置 → 大地图传送设置 → 使用公版传送。
/// </summary>
public class TpTask
{
    private readonly CancellationToken _ct;
    private readonly TpTaskOfficial _official;
    private readonly TpTaskFastDrag _fastDrag;
    private readonly TpConfig _tpConfig = TaskContext.Instance().Config.TpConfig;

    public TpTask(CancellationToken ct)
    {
        _ct = ct;
        _official = new TpTaskOfficial(ct);
        _fastDrag = new TpTaskFastDrag(ct);
    }

    private bool UseOfficial => _tpConfig.UseOfficialTeleport;

    // ===== 公共入口方法 =====

    public async Task TpToStatueOfTheSeven(bool requireLoadingScreen = false)
    {
        if (UseOfficial)
        {
            await _official.TpToStatueOfTheSeven();
        }
        else
        {
            await _fastDrag.TpToStatueOfTheSeven(requireLoadingScreen);
        }
    }

    public async Task OpenBigMapUi(int retryCount = 3, string? mapName = null)
    {
        if (UseOfficial)
        {
            await _official.OpenBigMapUi(retryCount, mapName);
        }
        else
        {
            await _fastDrag.OpenBigMapUi(retryCount, mapName);
        }
    }

    public async Task CheckInBigMapUi(int retryCount = 0, string? mapName = null)
    {
        if (UseOfficial)
        {
            await _official.CheckInBigMapUi(mapName);
        }
        else
        {
            await _fastDrag.CheckInBigMapUi(retryCount, mapName);
        }
    }

    public async Task<(double, double)> Tp(double tpX, double tpY, string mapName = "Teyvat", bool force = false, bool requireLoadingScreen = false, string? fastSyncId = null)
    {
        // 传送冻结 CD 补偿：记录开始时刻 → 转发 → 成功后单机补偿（联机不补偿）。
        // 语义与内联实现逐字节一致，详见 TeleportCdCompensation。
        var cdCompensation = new TeleportCdCompensation();
        cdCompensation.Start();
        (double, double) result;
        if (UseOfficial)
        {
            result = await _official.Tp(tpX, tpY, mapName, force);
        }
        else
        {
            result = await _fastDrag.Tp(tpX, tpY, mapName, force, requireLoadingScreen, fastSyncId);
        }
        cdCompensation.CompensateIfSolo();
        return result;
    }

    public async Task MoveMapTo(double x, double y, string mapName, double finalZoomLevel = 2, string? country = null, int retryTimes = 0, bool enableEarlyStop = true)
    {
        if (UseOfficial)
        {
            await _official.MoveMapTo(x, y, mapName, finalZoomLevel);
        }
        else
        {
            await _fastDrag.MoveMapTo(x, y, mapName, finalZoomLevel, country, retryTimes, enableEarlyStop);
        }
    }

    /// <summary>
    /// 点击大地图上的指定坐标。
    /// </summary>
    /// <param name="x">目标 x 坐标。</param>
    /// <param name="y">目标 y 坐标。</param>
    /// <param name="mapName">大地图名称。</param>
    public async Task ClickMapPoint(double x, double y, string mapName)
    {
        if (UseOfficial)
        {
            await _official.ClickMapPoint(x, y, mapName);
        }
        else
        {
            await _fastDrag.MoveMapTo(x, y, mapName);
            using var clickCapture = CaptureToRectArea();
            clickCapture.ClickTo(clickCapture.Width / 2, clickCapture.Height / 2);
        }
    }

    public async Task MouseClickAndMove(int x1, int y1, int x2, int y2)
    {
        if (UseOfficial)
        {
            await _official.MouseClickAndMove(x1, y1, x2, y2);
        }
        else
        {
            await _fastDrag.MouseClickAndMove(x1, y1, x2, y2);
        }
    }

    public async Task AdjustMapZoomLevel(double zoomLevel, double targetZoomLevel)
    {
        if (UseOfficial)
        {
            await _official.AdjustMapZoomLevel(zoomLevel, targetZoomLevel);
        }
        else
        {
            await _fastDrag.AdjustMapZoomLevel(zoomLevel, targetZoomLevel);
        }
    }

    public Point2f GetPositionFromBigMap(string mapName, bool usePrior = true)
    {
        if (UseOfficial)
        {
            return _official.GetPositionFromBigMap(mapName);
        }
        else
        {
            return _fastDrag.GetPositionFromBigMap(mapName, usePrior);
        }
    }

    public Point2f? GetPositionFromBigMapNullable(string mapName, bool usePrior = true)
    {
        if (UseOfficial)
        {
            return _official.GetPositionFromBigMapNullable(mapName);
        }
        else
        {
            return _fastDrag.GetPositionFromBigMapNullable(mapName, usePrior);
        }
    }

    public Rect GetBigMapRect(string mapName)
    {
        if (UseOfficial)
        {
            return _official.GetBigMapRect(mapName);
        }
        else
        {
            return _fastDrag.GetBigMapRect(mapName);
        }
    }

    public Point2f GetBigMapCenterPoint(string mapName, bool usePrior = true)
    {
        if (UseOfficial)
        {
            return _official.GetBigMapCenterPoint(mapName);
        }
        else
        {
            return _fastDrag.GetBigMapCenterPoint(mapName, usePrior);
        }
    }

    public List<GiTpPosition> GetNearestNTpPoints(double x, double y, string mapName, int n = 1)
    {
        if (UseOfficial)
        {
            return _official.GetNearestNTpPoints(x, y, mapName, n);
        }
        else
        {
            return _fastDrag.GetNearestNTpPoints(x, y, mapName, n);
        }
    }

    public async Task<bool> SwitchRecentlyCountryMap(double x, double y, string? forceCountry = null)
    {
        if (UseOfficial)
        {
            return await _official.SwitchRecentlyCountryMap(x, y, forceCountry);
        }
        else
        {
            return await _fastDrag.SwitchRecentlyCountryMap(x, y, forceCountry);
        }
    }

    internal async Task SwitchArea(string areaName)
    {
        if (UseOfficial)
        {
            await _official.SwitchArea(areaName);
        }
        else
        {
            await _fastDrag.SwitchArea(areaName);
        }
    }

    public double GetBigMapZoomLevel(ImageRegion region)
    {
        if (UseOfficial)
        {
            return _official.GetBigMapZoomLevel(region);
        }
        else
        {
            return _fastDrag.GetBigMapZoomLevel(region);
        }
    }

    public static double ComputeClickZoomCandidate(int attempt, double displayZoom, double minZoom)
    {
        return TpTaskFastDrag.ComputeClickZoomCandidate(attempt, displayZoom, minZoom);
    }

    public static bool ShouldCollapseZoomBeforeClick(double currentZoom, double displayZoom, double precisionThreshold)
    {
        return TpTaskFastDrag.ShouldCollapseZoomBeforeClick(currentZoom, displayZoom, precisionThreshold);
    }
}