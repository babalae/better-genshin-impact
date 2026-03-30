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
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoPathing
{
    /// <summary>
    /// Serves as the real-time core pathing engine to correlate game coordinates and states.
    /// 提供实时核心路径引擎，用于关联游戏坐标和状态。
    /// </summary>
    public class PathingNavigator
    {
        private readonly CancellationToken _ct;
        private readonly Func<ImageRegion?, Task> _resolveAnomaliesAction;

        private Point2f _prePosition;
        private DateTime _preTime;

        /// <summary>
        /// Indicates whether execution logic was suspended. Handles temporal disconnects.
        /// 指示执行逻辑是否已挂起。用于处理时间性连接断开现象。
        /// </summary>
        public bool GetPositionAndTimeSuspendFlag { get; set; } = false;
        
        /// <summary>
        /// Gets or sets the current tracked sequence of waypoints.
        /// 获取或设置目前处于追踪序列中的点位列表及索引。
        /// </summary>
        public (int, List<WaypointForTrack>) CurWaypoints { get; set; }
        
        /// <summary>
        /// Gets or sets the active instantaneous target waypoint.
        /// 获取或设置当前瞬时活跃目标路点。
        /// </summary>
        public (int, WaypointForTrack) CurWaypoint { get; set; }
        
        private (int, List<WaypointForTrack>) _recordWaypoints { get; set; }
        private (int, WaypointForTrack) _recordWaypoint { get; set; }
        
        /// <summary>
        /// Gets flags dictating whether extraneous non-pathing operations are ignored.
        /// 指示是否需要跳过行径（寻路）路线以外产生的任何额外操作。
        /// </summary>
        public bool SkipOtherOperations { get; private set; } = false;

        /// <summary>
        /// Initializes a new instance of the PathingNavigator.
        /// 初始化 PathingNavigator 类的新实例。
        /// </summary>
        /// <param name="ct">Asynchronous cancellation token. 异步取消令牌。</param>
        /// <param name="resolveAnomaliesAction">External delegate treating immediate popup obstructions. 用于处理立刻弹出的阻断的外部处置委托。</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="resolveAnomaliesAction"/> is null. 当 <paramref name="resolveAnomaliesAction"/> 为 null 时抛出。</exception>
        public PathingNavigator(CancellationToken ct, Func<ImageRegion?, Task> resolveAnomaliesAction)
        {
            _ct = ct;
            _resolveAnomaliesAction = resolveAnomaliesAction ?? throw new ArgumentNullException(nameof(resolveAnomaliesAction));
        }

        /// <summary>
        /// Defuses global operational skips when contextual trajectory catches up.
        /// 当上下文路径捕捉到当前进度时，消除全局操作的跳过逻辑。
        /// </summary>
        public void TryCloseSkipOtherOperations()
        {
            if (_recordWaypoints == CurWaypoints && CurWaypoint.Item1 < _recordWaypoint.Item1)
            {
                return;
            }

            if (SkipOtherOperations)
            {
                Logger?.LogWarning("已到达上次点位，地图追踪功能恢复");
            }

            SkipOtherOperations = false;
        }

        /// <summary>
        /// Instigates brute-force traversal skipping all interim operations until recovered cache index is met.
        /// 暴力激活寻路，直到捕获重定位缓存索引前，跳过路途中所有中间操作。
        /// </summary>
        public void StartSkipOtherOperations()
        {
            Logger?.LogWarning("记录恢复点位，地图追踪将到达上次点位之前将跳过走路之外的操作");
            SkipOtherOperations = true;
            _recordWaypoints = CurWaypoints;
            _recordWaypoint = CurWaypoint;
        }

        /// <summary>
        /// Gracefully awaits the map UI un-rendering boundary phase.
        /// 优雅地等待直到游戏地图UI被移除出当前帧画面。
        /// </summary>
        /// <param name="maxAttempts">Polling count ceilings preventing infinite spins. 防止死结无休止轮询的最大限制次数。</param>
        /// <param name="delayMs">Interval slice in milliseconds. 轮询停滞切片间隔，单位：毫秒。</param>
        public async Task WaitForCloseMap(int maxAttempts, int delayMs)
        {
            if (maxAttempts < 0) maxAttempts = 0;
            if (delayMs < 0) delayMs = 0;

            await Delay(delayMs, _ct).ConfigureAwait(false);
            for (var i = 0; i < maxAttempts; i++)
            {
                using var capture = CaptureToRectArea();
                if (capture != null && Bv.IsInMainUi(capture))
                {
                    return;
                }

                await Delay(delayMs, _ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Wraps <see cref="GetPositionAndTime"/> to strictly deduce current game coordinates.
        /// 将包裹的函数化简为仅返回当前游戏坐标。
        /// </summary>
        /// <param name="imageRegion">The underlying tracked screen buffer. 追踪底层屏幕的缓冲区空间。</param>
        /// <param name="waypoint">Target localization node details. 目的地位置局部细节节点。</param>
        /// <returns>Decoded vector point mapping. 解码还原的点位坐标。</returns>
        public async Task<Point2f> GetPosition(ImageRegion imageRegion, WaypointForTrack waypoint)
        {
            return (await GetPositionAndTime(imageRegion, waypoint).ConfigureAwait(false)).point;
        }

        /// <summary>
        /// Determines exact location incorporating potential anomaly detection and map-reopening routines.
        /// 集成异常检测和地图重定位程序的精确游戏坐标解析组合体。
        /// </summary>
        /// <param name="imageRegion">Screen capture context bounding map zones. 截图捕捉包含地图的边缘上下文场景。</param>
        /// <param name="waypoint">The tracking node to compare delta logic against. 供增量比较追踪逻辑所需的节点。</param>
        /// <returns>Coordinate and lag times metadata tuple. 附带耗时元数据的坐标返回值。</returns>
        /// <exception cref="ArgumentNullException">Thrown when parameter <paramref name="imageRegion"/> or <paramref name="waypoint"/> is null. 当传递的参数为 null 时抛出错误。</exception>
        public async Task<(Point2f point, int additionalTimeInMs)> GetPositionAndTime(ImageRegion imageRegion, WaypointForTrack waypoint)
        {
            ArgumentNullException.ThrowIfNull(imageRegion);
            ArgumentNullException.ThrowIfNull(waypoint);
            
            var position = Navigation.GetPosition(imageRegion, waypoint.MapName, waypoint.MapMatchMethod);
            int time = 0;

            if (position.X == 0f && position.Y == 0f)
            {
                if (!Bv.IsInMainUi(imageRegion))
                {
                    Logger?.LogDebug("小地图位置定位失败，且当前不是主界面，进入异常处理");
                    await _resolveAnomaliesAction(imageRegion).ConfigureAwait(false);
                }
            }

            var distance = Navigation.GetDistance(waypoint, position);
            
            if (position.X == 0f && position.Y == 0f && GetPositionAndTimeSuspendFlag)
            {
                GetPositionAndTimeSuspendFlag = false;
                throw new RetryNoCountException("可能暂停导致路径过远，重试一次此路线！");
            }

            if (GetPositionAndTimeSuspendFlag && !(position.X == 0f && position.Y == 0f))
            {
                GetPositionAndTimeSuspendFlag = false;
            }

            bool unrecognized = waypoint.Misidentification?.Type?.Contains("unrecognized") ?? false;
            bool pathTooFar = waypoint.Misidentification?.Type?.Contains("pathTooFar") ?? false;

            if ((position.X == 0f && position.Y == 0f && unrecognized) || (distance > 500.0 && pathTooFar))
            {
                if (waypoint.Misidentification?.HandlingMode == "previousDetectedPoint")
                {
                    if (_prePosition != default)
                    {
                        position = _prePosition;
                        Logger?.LogInformation("未识别到具体路径，取上次点位");
                    }
                }
                else if (waypoint.Misidentification?.HandlingMode == "mapRecognition")
                {
                    DateTime start = DateTime.UtcNow;
                    var tpTask = new TpTask(_ct);
                    await tpTask.OpenBigMapUi().ConfigureAwait(false);
                    try
                    {
                        var mapBase = MapManager.GetMap(waypoint.MapName, waypoint.MapMatchMethod);
                        if (mapBase != null)
                        {
                            position = mapBase.ConvertGenshinMapCoordinatesToImageCoordinates(tpTask.GetPositionFromBigMap(waypoint.MapName));
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger?.LogInformation($"地图中心点识别失败！异常: {ex.Message}");
                    }

                    Simulation.SendInput?.Keyboard?.KeyPress(User32.VK.VK_ESCAPE);
                    await WaitForCloseMap(10, 200).ConfigureAwait(false);
                    DateTime end = DateTime.UtcNow;
                    time = (int)(end - start).TotalMilliseconds;
                    Logger?.LogInformation($"未识别到具体路径，打开地图计算中心点({position.X},{position.Y})");
                }
            }
            else
            {
                _prePosition = position;
                _preTime = DateTime.UtcNow;
            }

            return (position, time);
        }

        /// <summary>
        /// Calculates linearly interpolated cartesian points based purely on intermediate elapsed duration ratios.
        /// 基于纯粹的经过耗时比率在两点直线向量中间进行平面差值线性演算。
        /// </summary>
        /// <param name="startPoint">Origin position vector. 原点矢量位置。</param>
        /// <param name="endPoint">Target objective coordinate. 标靶定位终结坐标。</param>
        /// <param name="startTime">Genesis point snapshot timestamp. 本源起始事件记录戳。</param>
        /// <param name="midTime">Current mid-execution temporal snapshot. 当前运算中位节点时钟戳。</param>
        /// <param name="endTime">Theoretical conclusion timing mark. 预估收束末期结束时间。</param>
        /// <returns>Interpolated synthesized dynamic location. 线性合成推导的即时坐标向量。</returns>
        public static Point2f InterpolatePointByTime(
            Point2f startPoint,
            Point2f endPoint,
            DateTime startTime,
            DateTime midTime,
            DateTime endTime)
        {
            double totalMillis = (endTime - startTime).TotalMilliseconds;
            double midMillis = (midTime - startTime).TotalMilliseconds;

            if (totalMillis <= 0.0) return startPoint;
            if (midMillis <= 0.0) return startPoint;

            float t = Math.Clamp((float)(midMillis / totalMillis), 0f, 1f);
            float x = startPoint.X + (endPoint.X - startPoint.X) * t;
            float y = startPoint.Y + (endPoint.Y - startPoint.Y) * t;

            return new Point2f(x, y);
        }
    }
}
