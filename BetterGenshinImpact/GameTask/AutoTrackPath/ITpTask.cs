using System.Collections.Generic;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoTrackPath.Model;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

/// <summary>
/// 传送任务：大地图传送，以及打开大地图、切换区域、移动与缩放地图、识别地图位置等大地图操作。
/// 调用方通过 <see cref="ITpTaskFactory"/> 创建，每个实例绑定创建时的运行环境、配置和取消令牌。
/// </summary>
/// <remarks>
/// 内置实现为 <see cref="TpTask"/>，插件可以提供完整的另一套实现。
/// 同一实例上的调用之间会共享状态（例如切换区域后记住的国家中心、缩放校准），调用方按顺序调用，不要并发。
/// 实现内部的键鼠操作与截图之间需要按项目约定留出延时（游戏响应约 30～60 ms，30 fps 下输入过快会丢失）。
/// </remarks>
public interface ITpTask
{
    /// <summary>
    /// 传送到配置中指定的七天神像（或最近的七天神像），按配置移动到神像旁并等待回血。
    /// </summary>
    Task TpToStatueOfTheSeven();

    /// <summary>
    /// 释放所有按键，并打开大地图界面；已经在大地图时直接返回。失败时抛出异常。
    /// </summary>
    /// <param name="retryCount">重试次数。</param>
    /// <param name="mapName">要打开的地图名称，部分地图打开较慢；为 null 时按提瓦特处理。</param>
    Task OpenBigMapUi(int retryCount = 3, string? mapName = null);

    /// <summary>
    /// 确认处于大地图界面，不在时打开；打开失败时回到主界面再试一次，仍失败时抛出异常。
    /// </summary>
    /// <param name="mapName">要打开的地图名称；为 null 时按提瓦特处理。</param>
    Task CheckInBigMapUi(string? mapName = null);

    /// <summary>
    /// 通过大地图传送到离 (tpX, tpY) 最近的传送点。内部包含超时与重试，结束时处于主界面。
    /// </summary>
    /// <param name="tpX">目标 x 坐标（原神坐标系）。</param>
    /// <param name="tpY">目标 y 坐标（原神坐标系）。</param>
    /// <param name="mapName">地图名称。</param>
    /// <param name="force">强制以 (tpX, tpY) 作为点击目标，而不是取最近的传送点。</param>
    /// <returns>实际传送的坐标。</returns>
    Task<(double, double)> Tp(double tpX, double tpY, string mapName = "Teyvat", bool force = false);

    /// <summary>
    /// 移动大地图，使指定坐标进入可点击区域。可能移动不准，调用方可以重试。
    /// </summary>
    /// <param name="x">目标 x 坐标。</param>
    /// <param name="y">目标 y 坐标。</param>
    /// <param name="mapName">地图名称。</param>
    /// <param name="finalZoomLevel">到达目标点的最小缩放等级，只在启用地图缩放时生效。</param>
    Task MoveMapTo(double x, double y, string mapName, double finalZoomLevel = 2);

    /// <summary>
    /// 把目标移动到可点击区域后点击大地图上的指定坐标。目标未移动到可点击区域时抛出异常。
    /// </summary>
    /// <param name="x">目标 x 坐标。</param>
    /// <param name="y">目标 y 坐标。</param>
    /// <param name="mapName">地图名称。</param>
    Task ClickMapPoint(double x, double y, string mapName);

    /// <summary>
    /// 按住左键从 (x1, y1) 拖到 (x2, y2)，坐标为 1080P 下的游戏区域坐标。
    /// </summary>
    Task MouseClickAndMove(int x1, int y1, int x2, int y2);

    /// <summary>
    /// 把大地图缩放等级从 zoomLevel 调到 targetZoomLevel。
    /// 1.0 为地图最大（缩放条最上方），6.0 为地图最小（最下方）。
    /// </summary>
    /// <param name="zoomLevel">当前缩放等级；无效值时重新识别。</param>
    /// <param name="targetZoomLevel">目标缩放等级。</param>
    Task AdjustMapZoomLevel(double zoomLevel, double targetZoomLevel);

    /// <summary>
    /// 识别大地图中心坐标，失败时抛出异常。
    /// </summary>
    /// <param name="mapName">地图名称。</param>
    Point2f GetPositionFromBigMap(string mapName);

    /// <summary>
    /// 识别大地图中心坐标，失败时返回 null。
    /// </summary>
    /// <param name="mapName">地图名称。</param>
    Point2f? GetPositionFromBigMapNullable(string mapName);

    /// <summary>
    /// 识别当前大地图视野在整个地图中的范围（原神坐标系），多次重试失败后抛出异常。
    /// </summary>
    /// <param name="mapName">地图名称。</param>
    Rect GetBigMapRect(string mapName);

    /// <summary>
    /// 识别大地图中心坐标，失败时抛出异常。
    /// </summary>
    /// <param name="mapName">地图名称。</param>
    Point2f GetBigMapCenterPoint(string mapName);

    /// <summary>
    /// 取离 (x, y) 最近的 n 个传送点。
    /// </summary>
    /// <param name="x">x 坐标。</param>
    /// <param name="y">y 坐标。</param>
    /// <param name="mapName">地图名称。</param>
    /// <param name="n">数量，至少为 1。</param>
    List<GiTpPosition> GetNearestNTpPoints(double x, double y, string mapName, int n = 1);

    /// <summary>
    /// 按距离切换到离 (x, y) 最近的国家；当前视野已经很近时不切换。
    /// </summary>
    /// <param name="x">目标 x 坐标。</param>
    /// <param name="y">目标 y 坐标。</param>
    /// <param name="forceCountry">需要切换时使用的国家，为 null 时按距离选择。</param>
    /// <returns>是否切换了区域。</returns>
    Task<bool> SwitchRecentlyCountryMap(double x, double y, string? forceCountry = null);

    /// <summary>
    /// 在大地图中切换区域（国家、独立地图、尘歌壶等），失败时抛出异常。
    /// </summary>
    /// <param name="areaName">区域中文名，如“蒙德”“层岩巨渊”“尘歌壶”。</param>
    Task SwitchArea(string areaName);

    /// <summary>
    /// 从大地图截图识别缩放等级，1.0 到 6.0。
    /// </summary>
    /// <param name="region">大地图截图。</param>
    double GetBigMapZoomLevel(ImageRegion region);
}
