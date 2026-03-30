using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Common.Map;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Strategy;

/// <summary>
/// Strategy for executing teleport waypoint navigation tasks. Ensures proper coordinate translation and expedition reward polling without corrupting game state.
/// 执行传送点导航任务的策略。在游戏路点间传送，并支持探险派遣奖励收集，确保坐标转换精度与游戏状态完整性。
/// </summary>
public class TeleportWaypointStrategy : IWaypointStrategy
{
    /// <summary>
    /// Minimum interval in minutes for expedition reward collection.
    /// 派遣奖励收集的最小间隔时间（分钟）。避免过度频繁的图像识别带来的性能开销与精度不稳。
    /// </summary>
    private const int ExpeditionRewardMinIntervalMinutes = 5;

    /// <summary>
    /// OCR region X-coordinate for expedition reward detection.
    /// 派遣奖励检测的OCR区域X坐标。基于分辨率无关设计的标准化偏移量。
    /// </summary>
    private const int OcrRegionX = 60;

    /// <summary>
    /// OCR region Y-coordinate for expedition reward detection.
    /// 派遣奖励检测的OCR区域Y坐标。定位派遣提示文案的顶部偏移。
    /// </summary>
    private const int OcrRegionY = 20;

    /// <summary>
    /// OCR region width for expedition reward detection.
    /// 派遣奖励检测的OCR区域宽度。限定识别范围以消除环境干扰，确保稳定性。
    /// </summary>
    private const int OcrRegionWidth = 160;

    /// <summary>
    /// OCR region height for expedition reward detection.
    /// 派遣奖励检测的OCR区域高度。覆盖可能出现的完整文本高度。
    /// </summary>
    private const int OcrRegionHeight = 260;

    /// <summary>
    /// Delay after teleportation completion in milliseconds.
    /// 传送完成后的延迟时间（毫秒）。用于消除不同设备场景加载不同步带来的物理状态突变风险。
    /// </summary>
    private const int PostTeleportDelayMs = 500;

    /// <summary>
    /// Delay before teleportation when not following specific action types in milliseconds.
    /// 传送前的延迟时间，仅在非特殊动作类型时应用（毫秒）。缓冲UI切换动效。
    /// </summary>
    private const int PreTeleportDelayMs = 1000;

    /// <summary>
    /// Text indicator for expedition reward availability.
    /// 派遣奖励可用的文本标识符。用于OCR模式匹配。
    /// </summary>
    private const string ExpeditionRewardIndicator = "探索派遣奖励";

    /// <summary>
    /// Executes the teleport waypoint navigation strategy, positioning the entity precisely in world space and polling asynchronous map tasks.
    /// 执行传送点导航策略。将角色传送到指定全局地图位置，并处理异步的派遣奖励回调逻辑。
    /// </summary>
    /// <param name="executor">The path executor context managing the navigation lifecycle. 路径执行器上下文，包含导航生命周期与状态配置。</param>
    /// <param name="waypoint">The target waypoint containing destination coordinates. 要传送的目标路点，包含终点坐标数据。</param>
    /// <param name="waypointsList">The full topological waypoint routing dataset. 完整的拓扑路点路由数据集，用于前置状态预测。</param>
    /// <returns>Always false since teleportation breaks continuous topology and cannot be naively resumed. 始终返回false，因为传送属于物理拓扑断点操作，无法被普通位移机制恢复。</returns>
    public async Task<bool> ExecuteAsync(PathExecutor executor, WaypointForTrack waypoint, List<List<WaypointForTrack>> waypointsList)
    {
        if (executor.CurWaypoints.Item1 > 0)
        {
            var prevWaypoints = waypointsList[executor.CurWaypoints.Item1 - 1];
            if (prevWaypoints.Count > 0)
            {
                var prevWaypoint = prevWaypoints[prevWaypoints.Count - 1];
                if (!ShouldSkipPreTeleportDelay(prevWaypoint))
                {
                    await Delay(PreTeleportDelayMs, executor.ct);
                }
            }
        }

        await HandleTeleportWaypoint(executor, waypoint);
        return false;
    }

    /// <summary>
    /// Evaluates previous topological node to determine if kinematic suspension (delay) can be safely bypassed.
    /// 评估前一个拓扑节点，以绝对规则判断是否可以安全跳过运动学阻滞（传送前延迟）。
    /// </summary>
    /// <param name="prevWaypoint">The preceding waypoint data structure. 前置路点数据结构。</param>
    /// <returns>True if the transition is mechanically stable without delay. 若状态机构切换物理稳定无需延迟，则返回 true。</returns>
    private static bool ShouldSkipPreTeleportDelay(WaypointForTrack prevWaypoint)
    {
        if (prevWaypoint == null) return false;

        return prevWaypoint.Type == WaypointType.Teleport.Code
            || prevWaypoint.Action == ActionEnum.Fight.Code
            || prevWaypoint.Action == ActionEnum.NahidaCollect.Code
            || prevWaypoint.Action == ActionEnum.PickAround.Code;
    }

    /// <summary>
    /// Orchestrates the destination teleportation sequence and applies subsequent coordinate transformation bounds safely.
    /// 编排目标传送序列，并安全地应用后续的大地图逆向坐标变换边界限定。
    /// </summary>
    /// <param name="executor">The current navigation scope instance. 当前导航作用域实例。</param>
    /// <param name="waypoint">The localized target location. 锚定的目标位置。</param>
    private async Task HandleTeleportWaypoint(PathExecutor executor, WaypointForTrack waypoint)
    {
        bool forceTp = waypoint.Action == ActionEnum.ForceTp.Code;
        var tpTask = new TpTask(executor.ct);
        
        await TryGetExpeditionRewardsDispatch(executor, tpTask);
        
        // Ensure floating point coordinate math handles extreme bounds properly
        // 浮点数坐标计算，规避极端边界下转换崩溃
        var tpResult = await tpTask.Tp(waypoint.GameX, waypoint.GameY, waypoint.MapName, forceTp);
        double tpX = tpResult.Item1;
        double tpY = tpResult.Item2;
        
        var mapInstance = MapManager.GetMap(waypoint.MapName, waypoint.MapMatchMethod);
        if (mapInstance != null)
        {
            var imgCoord = mapInstance.ConvertGenshinMapCoordinatesToImageCoordinates(new Point2f((float)tpX, (float)tpY));
            Navigation.SetPrevPosition(imgCoord.X, imgCoord.Y);
        }
        
        await Delay(PostTeleportDelayMs, executor.ct);
    }

    /// <summary>
    /// Polls global UI state to heuristically collect expedition rewards without destabilizing the current thread.
    /// 轮询全局界面状态，启发式地收集派遣奖励，同时保证当前运行线程的物理稳定性不被破坏。
    /// </summary>
    /// <param name="executor">The overarching task executor carrying session limits. 包含会话限制的整体任务执行器。</param>
    /// <param name="tpTask">The active teleport UI manipulation context. 活跃的传送UI操控上下文。</param>
    /// <returns>True if a high-level UI transition occurred. 如果触发了高级UI切换则返回true。</returns>
    private async Task<bool> TryGetExpeditionRewardsDispatch(PathExecutor executor, TpTask tpTask)
    {
        if (executor._combatScenes?.CurrentMultiGameStatus?.IsInMultiGame == true)
        {
            return false;
        }

        var timeSinceLastReward = (DateTime.UtcNow - executor._lastGetExpeditionRewardsTime).TotalMinutes;
        if (timeSinceLastReward < ExpeditionRewardMinIntervalMinutes)
        {
            return false;
        }

        await tpTask.OpenBigMapUi();
        
        bool changeBigMap = false;
        string? adventurersGuildCountry = TaskContext.Instance().Config?.OtherConfig?.AutoFetchDispatchAdventurersGuildCountry;
        
        if (!RunnerContext.Instance.isAutoFetchDispatch 
            && !string.IsNullOrEmpty(adventurersGuildCountry) 
            && adventurersGuildCountry != "无")
        {
            if (TryDetectExpeditionRewards())
            {
                changeBigMap = true;
                Logger.LogInformation("开始自动领取派遣任务！");
                try
                {
                    RunnerContext.Instance.isAutoFetchDispatch = true;
                    await RunnerContext.Instance.StopAutoPickRunTask(
                        async () => await new GoToAdventurersGuildTask().Start(adventurersGuildCountry, executor.ct, null, true),
                        5);
                    Logger.LogInformation("自动领取派遣结束，回归原任务！");
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "异常发生在自动派遣领取过程中，继续执行主任务");
                }
                finally
                {
                    RunnerContext.Instance.isAutoFetchDispatch = false;
                    executor._lastGetExpeditionRewardsTime = DateTime.UtcNow;
                }
            }
        }

        return changeBigMap;
    }

    /// <summary>
    /// Excises a deterministic rect sub-region from the active rendering buffer to optically read expedition state.
    /// 从当前渲染缓冲中截取确定性的矩形子区域，结合光学识别读取派遣驻留状态，防止越界或内存泄漏。
    /// </summary>
    /// <returns>True if the string pattern matches precisely. 字符串特征精准匹配时返回true。</returns>
    private bool TryDetectExpeditionRewards()
    {
        try
        {
            var rectArea = CaptureToRectArea();
            if (rectArea == null || rectArea.SrcMat == null || rectArea.SrcMat.IsDisposed)
            {
                return false;
            }

            var ocrRect = new Rect(OcrRegionX, OcrRegionY, OcrRegionWidth, OcrRegionHeight);
            
            using (var mat = new Mat(rectArea.SrcMat, ocrRect))
            {
                string ocrText = OcrFactory.Paddle.Ocr(mat);
                return !string.IsNullOrEmpty(ocrText) && ocrText.Contains(ExpeditionRewardIndicator);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "派遣奖励检测过程中出现异常");
            return false;
        }
    }
}
