using BetterGenshinImpact.Core.BgiVision;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing
{
    public class PathingNavigator
    {
        private readonly CancellationToken _ct;
        private readonly Func<ImageRegion?, Task> _resolveAnomaliesAction;

        private Point2f prePosition;
        private DateTime preTime;
        private int maxAutoPositionTime = 10000;
        public bool GetPositionAndTimeSuspendFlag = false;
        
        // 记录当前相关点位数组
        public (int, System.Collections.Generic.List<WaypointForTrack>) CurWaypoints { get; set; }
        // 记录当前点位
        public (int, WaypointForTrack) CurWaypoint { get; set; }
        // 记录恢复点位数组
        private (int, System.Collections.Generic.List<WaypointForTrack>) RecordWaypoints { get; set; }
        // 记录恢复点位
        private (int, WaypointForTrack) RecordWaypoint { get; set; }
        
        // 跳过除走路径以外的操作
        public bool SkipOtherOperations { get; private set; } = false;

        public PathingNavigator(CancellationToken ct, Func<ImageRegion?, Task> resolveAnomaliesAction)
        {
            _ct = ct;
            _resolveAnomaliesAction = resolveAnomaliesAction;
        }

        public void TryCloseSkipOtherOperations()
        {
            if (RecordWaypoints == CurWaypoints && CurWaypoint.Item1 < RecordWaypoint.Item1)
            {
                return;
            }

            if (SkipOtherOperations)
            {
                Logger.LogWarning("已到达上次点位，地图追踪功能恢复");
            }

            SkipOtherOperations = false;
        }

        public void StartSkipOtherOperations()
        {
            Logger.LogWarning("记录恢复点位，地图追踪将到达上次点位之前将跳过走路之外的操作");
            SkipOtherOperations = true;
            RecordWaypoints = CurWaypoints;
            RecordWaypoint = CurWaypoint;
        }

        public async Task WaitForCloseMap(int maxAttempts, int delayMs)
        {
            await Delay(delayMs, _ct);
            for (var i = 0; i < maxAttempts; i++)
            {
                using var capture = CaptureToRectArea();
                if (Bv.IsInMainUi(capture))
                {
                    return;
                }

                await Delay(delayMs, _ct);
            }
        }

        public async Task<Point2f> GetPosition(ImageRegion imageRegion, WaypointForTrack waypoint)
        {
            return (await GetPositionAndTime(imageRegion, waypoint)).point;
        }

        public async Task<(Point2f point, int additionalTimeInMs)> GetPositionAndTime(ImageRegion imageRegion, WaypointForTrack waypoint)
        {
            var position = Navigation.GetPosition(imageRegion, waypoint.MapName, waypoint.MapMatchMethod);
            int time = 0;
            if (position == new Point2f())
            {
                if (!Bv.IsInMainUi(imageRegion))
                {
                    Logger.LogDebug("小地图位置定位失败，且当前不是主界面，进入异常处理");
                    await _resolveAnomaliesAction(imageRegion);
                }
            }

            var distance = Navigation.GetDistance(waypoint, position);
            //中途暂停过，地图未识别到
            if (position is { X: 0, Y: 0 } && GetPositionAndTimeSuspendFlag)
            {
                GetPositionAndTimeSuspendFlag = false;
                throw new RetryNoCountException("可能暂停导致路径过远，重试一次此路线！");
            }

            // 恢复后首次定位成功即清理标志，避免后续偶发未识别被误判为暂停影响。
            if (GetPositionAndTimeSuspendFlag && position is not { X: 0, Y: 0 })
            {
                GetPositionAndTimeSuspendFlag = false;
            }

            //何时处理   pathTooFar  路径过远  unrecognized 未识别
            if ((position is { X: 0, Y: 0 } && waypoint.Misidentification.Type.Contains("unrecognized")) || (distance > 500 && waypoint.Misidentification.Type.Contains("pathTooFar")))
            {
                if (waypoint.Misidentification.HandlingMode == "previousDetectedPoint")
                {
                    if (prePosition != default)
                    {
                        position = prePosition;
                        Logger.LogInformation(@$"未识别到具体路径，取上次点位");
                    }
                }
                else if (waypoint.Misidentification.HandlingMode == "mapRecognition")
                {
                    //大地图识别坐标
                    DateTime start = DateTime.Now;
                    TpTask tpTask = new TpTask(_ct);
                    await tpTask.OpenBigMapUi();
                    try
                    {
                        position = MapManager.GetMap(waypoint.MapName, waypoint.MapMatchMethod).ConvertGenshinMapCoordinatesToImageCoordinates(tpTask.GetPositionFromBigMap(waypoint.MapName));
                    }
                    catch (Exception)
                    {
                        Logger.LogInformation(@$"地图中心点识别失败！");
                    }

                    Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
                    await WaitForCloseMap(10, 200);
                    DateTime end = DateTime.Now;
                    time = (int)(end - start).TotalMilliseconds;
                    Logger.LogInformation(@$"未识别到具体路径，打开地图计算中心点({position.X},{position.Y})");
                }
            }
            else
            {
                prePosition = position;
                preTime = DateTime.Now;
            }

            return (position, time);
        }

        public static Point2f InterpolatePointByTime(
            Point2f startPoint,
            Point2f endPoint,
            DateTime startTime,
            DateTime midTime,
            DateTime endTime)
        {
            double totalMillis = (endTime - startTime).TotalMilliseconds;
            double midMillis = (midTime - startTime).TotalMilliseconds;

            if (totalMillis == 0)
                return startPoint;

            float t = (float)(midMillis / totalMillis);
            if (t > 1.0f)
            {
                t = 1.0f;
            }
            float x = startPoint.X + (endPoint.X - startPoint.X) * t;
            float y = startPoint.Y + (endPoint.Y - startPoint.Y) * t;

            return new Point2f(x, y);
        }
    }
}
