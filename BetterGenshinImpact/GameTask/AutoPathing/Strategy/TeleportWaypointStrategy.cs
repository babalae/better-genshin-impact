using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Map;
using OpenCvSharp;
using Microsoft.Extensions.Logging;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.Common.Job;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Strategy;

public class TeleportWaypointStrategy : IWaypointStrategy
{
    public async Task<bool> ExecuteAsync(PathExecutor executor, WaypointForTrack waypoint, List<List<WaypointForTrack>> waypointsList)
    {
        if (executor.CurWaypoints.Item1 > 0)
        {
            var prevWaypoints = waypointsList[executor.CurWaypoints.Item1 - 1];
            var prevWaypoint = prevWaypoints[prevWaypoints.Count - 1];
            if (prevWaypoint.Type == WaypointType.Teleport.Code
                || prevWaypoint.Action == ActionEnum.Fight.Code
                || prevWaypoint.Action == ActionEnum.NahidaCollect.Code
                || prevWaypoint.Action == ActionEnum.PickAround.Code)
            {
                // No delay
            }
            else
            {
                await BetterGenshinImpact.GameTask.Common.TaskControl.Delay(1000, executor.ct);
            }
        }
        await HandleTeleportWaypoint(executor, waypoint);
        return false;
    }

    private async Task HandleTeleportWaypoint(PathExecutor executor, WaypointForTrack waypoint)
    {
        var forceTp = waypoint.Action == ActionEnum.ForceTp.Code;
        TpTask tpTask = new TpTask(executor.ct);
        await TryGetExpeditionRewardsDispatch(executor, tpTask);
        var (tpX, tpY) = await tpTask.Tp(waypoint.GameX, waypoint.GameY, waypoint.MapName, forceTp);
        var (tprX, tprY) = MapManager.GetMap(waypoint.MapName, waypoint.MapMatchMethod)
            .ConvertGenshinMapCoordinatesToImageCoordinates(new Point2f((float)tpX, (float)tpY));
        Navigation.SetPrevPosition(tprX, tprY); // 通过上一个位置直接进行局部特征匹配
        await BetterGenshinImpact.GameTask.Common.TaskControl.Delay(500, executor.ct); // 多等一会
    }

    private async Task<bool> TryGetExpeditionRewardsDispatch(PathExecutor executor, TpTask tpTask)
    {
        // 最小5分钟间隔
        if (executor._combatScenes?.CurrentMultiGameStatus?.IsInMultiGame == true || (DateTime.UtcNow - executor._lastGetExpeditionRewardsTime).TotalMinutes < 5)
        {
            return false;
        }

        //打开大地图操作
        await tpTask.OpenBigMapUi();
        bool changeBigMap = false;
        string adventurersGuildCountry = TaskContext.Instance().Config.OtherConfig.AutoFetchDispatchAdventurersGuildCountry;
        if (!RunnerContext.Instance.isAutoFetchDispatch && adventurersGuildCountry != "无" && !string.IsNullOrEmpty(adventurersGuildCountry))
        {
            var ra1 = BetterGenshinImpact.GameTask.Common.TaskControl.CaptureToRectArea();
            var textRect = new Rect(60, 20, 160, 260);
            var textMat = new Mat(ra1.SrcMat, textRect);
            string text = OcrFactory.Paddle.Ocr(textMat);
            if (text.Contains("探索派遣奖励"))
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
                catch (Exception)
                {
                    Logger.LogInformation("未知原因，发生异常，尝试继续执行任务！");
                }
                finally
                {
                    RunnerContext.Instance.isAutoFetchDispatch = false;
                    executor._lastGetExpeditionRewardsTime = DateTime.UtcNow; // 无论成功与否都更新时间
                }
            }
        }

        return changeBigMap;
    }
}
