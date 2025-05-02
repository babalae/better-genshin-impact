using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoTrackPath.Model;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Map;
using BetterGenshinImpact.GameTask.Model.Area;

using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Extensions;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.View.Drawable;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

/// <summary>
/// 坐标计算原则
/// 1. 所有非矩形点位坐标，优先转换为游戏内原神坐标系
/// 2. 所有涉及矩形运算的，优先转换为全地图坐标系
/// 3. 所有涉及小地图视角角度运算的，优先转换为warpPolar所使用的度数标准
/// </summary>
[Obsolete]
public class AutoTrackPathTask
{
    private readonly AutoTrackPathParam _taskParam;

    private GiPath _way;

    // 视角偏移移动单位
    private const int CharMovingUnit = 500;

    private CancellationToken _ct;

    public AutoTrackPathTask(AutoTrackPathParam taskParam)
    {
        _taskParam = taskParam;

        var wayJson = File.ReadAllText(Global.Absolute(@"log\way\way2.json"));
        _way = JsonSerializer.Deserialize<GiPath>(wayJson, ConfigService.JsonOptions) ?? throw new Exception("way json deserialize failed");
    }

    public async void Start()
    {
        var hasLock = false;
        try
        {
            hasLock = await TaskSemaphore.WaitAsync(0);
            if (!hasLock)
            {
                Logger.LogError("启动自动路线功能失败：当前存在正在运行中的独立任务，请不要重复执行任务！");
                return;
            }

            _ct = CancellationContext.Instance.Cts.Token;

            Init();

            // Tp(_tpPositions[260].X, _tpPositions[260].Y);

            await DoTask();
        }
        catch (NormalEndException)
        {
            Logger.LogInformation("手动中断自动路线");
        }
        catch (Exception e)
        {
            Logger.LogError(e.Message);
            Logger.LogDebug(e.StackTrace);
        }
        finally
        {
            VisionContext.Instance().DrawContent.ClearAll();
            Logger.LogInformation("→ {Text}", "自动路线结束");

            if (hasLock)
            {
                TaskSemaphore.Release();
            }
        }
    }

    private void Init()
    {
        SystemControl.ActivateWindow();
        Logger.LogInformation("→ {Text}", "自动路线，启动！");
    }

    public async Task DoTask()
    {
        // 1. 传送到最近的传送点
        var first = _way.WayPointList[0]; // 解析路线，第一个点为起点
        await new TpTask(_ct).Tp(first.Pt.X, first.Pt.Y);

        // 2. 等待传送完成
        Sleep(1000);
        NewRetry.Do((Action)(() =>
        {
            var ra = TaskControl.CaptureToRectArea();
            var miniMapMat = GetMiniMapMat(ra) ?? throw new RetryException("等待传送完成");
        }), TimeSpan.FromSeconds(1), 100);
        Logger.LogInformation("传送完成");
        Sleep(1000);

        // 3. 横向移动偏移量校准，移动指定偏移、按下W后识别朝向
        var angleOffset = GetOffsetAngle();
        if (angleOffset == 0)
        {
            throw new InvalidOperationException("横向移动偏移量校准失败");
        }

        // 4. 针对点位进行直线追踪

        var trackCts = new CancellationTokenSource();
        _ct.Register(trackCts.Cancel);
        var trackTask = Track(_way.WayPointList, angleOffset, trackCts);
        trackTask.Start();
        var refreshStatusTask = RefreshStatus(trackCts.Token);
        refreshStatusTask.Start();
        var jumpTask = Jump(trackCts.Token);
        jumpTask.Start();
        await Task.WhenAll(trackTask, refreshStatusTask, jumpTask);
    }

    private MotionStatus _motionStatus = MotionStatus.Normal;

    public Task Jump(CancellationToken trackCt)
    {
        return new Task(() =>
        {
            while (!_ct.IsCancellationRequested && !trackCt.IsCancellationRequested)
            {
                if (_motionStatus == MotionStatus.Normal)
                {
                    MovementControl.Instance.SpacePress();
                    Sleep(300);
                    if (_motionStatus == MotionStatus.Normal)
                    {
                        MovementControl.Instance.SpacePress();
                        Sleep(3500);
                    }
                    else
                    {
                        Sleep(1600);
                    }
                }
                else
                {
                    Sleep(1600);
                }
            }
        });
    }

    private double _targetAngle = 0;

    public Task RefreshStatus(CancellationToken trackCt)
    {
        return new Task(() =>
        {
            while (!_ct.IsCancellationRequested && !trackCt.IsCancellationRequested)
            {
                using var ra = CaptureToRectArea();
                _motionStatus = Bv.GetMotionStatus(ra);

                // var miniMapMat = GetMiniMapMat(ra);
                // if (miniMapMat == null)
                // {
                //     throw new InvalidOperationException("当前不在主界面");
                // }
                //
                // var angle = CharacterOrientation.Compute(miniMapMat);
                // CameraOrientation.DrawDirection(ra, angle, "avatar", new Pen(Color.Blue, 1));
                // Debug.WriteLine($"当前人物图像坐标系角度：{angle}");

                // var moveAngle = (int)(_targetAngle - angle);
                // Debug.WriteLine($"旋转到目标角度：{_targetAngle}，鼠标平移{moveAngle}单位");
                // Simulation.SendInput.Mouse.MoveMouseBy(moveAngle, 0);
                Sleep(60);
            }
        });
    }

    public Task Track(List<GiPathPoint> pList, int angleOffsetUnit, CancellationTokenSource trackCts)
    {
        return new Task(() =>
        {
            var currIndex = 0;
            while (!_ct.IsCancellationRequested)
            {
                var ra = CaptureToRectArea();
                var miniMapMat = GetMiniMapMat(ra) ?? throw new InvalidOperationException("当前不在主界面");

                // 注意游戏坐标系的角度是顺时针的
                var currMapImageAvatarPos = MapManager.GetMap(MapTypes.Teyvat).GetMiniMapPosition(miniMapMat);
                if (currMapImageAvatarPos.IsEmpty())
                {
                    Debug.WriteLine("识别小地图位置失败");
                    continue;
                }
                var (nextMapImagePathPoint, nextPointIndex) = GetNextPoint(currMapImageAvatarPos, pList, currIndex); // 动态计算下个点位
                var nextMapImagePathPos = nextMapImagePathPoint.MatchPt;
                Logger.LogInformation("下个点位[{Index}]：{nextMapImagePathPos}", nextPointIndex, nextMapImagePathPos);

                var angle = CharacterOrientation.Compute(miniMapMat);
                CameraOrientation.DrawDirection(ra, angle, "avatar", new Pen(Color.Blue, 1));
                Debug.WriteLine($"当前人物图像坐标系角度：{angle}，位置：{currMapImageAvatarPos}");

                var nextAngle = Math.Round(Math.Atan2(nextMapImagePathPos.Y - currMapImageAvatarPos.Y, nextMapImagePathPos.X - currMapImageAvatarPos.X) * 180 / Math.PI);
                var nextDistance = MathHelper.Distance(nextMapImagePathPos, currMapImageAvatarPos);
                Debug.WriteLine($"当前目标点图像坐标系角度：{nextAngle}，距离：{nextDistance}");
                CameraOrientation.DrawDirection(ra, nextAngle, "target", new Pen(Color.Red, 1));

                if (nextDistance < 10)
                {
                    Logger.LogInformation("到达目标点位");
                    currIndex = nextPointIndex;
                    MovementControl.Instance.WUp();
                    if (currIndex == pList.Count - 1)
                    {
                        Logger.LogInformation("到达终点");
                        trackCts.Cancel();
                        break;
                    }
                }

                // 转换为鼠标移动单位
                _targetAngle = nextAngle;
                var moveAngle = (int)(nextAngle - angle);
                moveAngle = (int)(moveAngle * 1d / angleOffsetUnit * CharMovingUnit);
                Debug.WriteLine($"旋转到目标角度：{nextAngle}，鼠标平移{moveAngle}单位");
                Simulation.SendInput.Mouse.MoveMouseBy(moveAngle, 0);
                Sleep(100);

                miniMapMat = GetMiniMapMat(ra);
                if (miniMapMat == null)
                {
                    throw new InvalidOperationException("当前不在主界面");
                }
                angle = CharacterOrientation.Compute(miniMapMat);
                CameraOrientation.DrawDirection(ra, angle, "avatar", new Pen(Color.Blue, 1));

                Sleep(100);
                MovementControl.Instance.WDown();

                Sleep(50);

                // MovementControl.Instance.WDown();
                // Sleep(80);
            }
        });
    }

    /// <summary>
    ///  地图图像点位
    ///  寻找后面20个点位中，下一个最近点位，关键点必须走到
    /// </summary>
    /// <param name="currPoint"></param>
    /// <param name="pList"></param>
    /// <param name="currIndex"></param>
    /// <returns></returns>
    public (GiPathPoint, int) GetNextPoint(Point2f currPoint, List<GiPathPoint> pList, int currIndex)
    {
        var nextNum = Math.Min(currIndex + 20, pList.Count - 1); // 最多找最近20个点
        var minDistance = double.MaxValue;
        var minDistancePoint = pList[currIndex];
        var minDistanceIndex = currIndex;
        // var minDistanceButGt = double.MaxValue;
        // var minDistancePointButGt = pList[currIndex];
        // var minDistanceIndexButGt = currIndex;
        for (var i = currIndex; i < nextNum; i++)
        {
            var nextPoint = pList[i + 1];
            var nextMapImagePos = nextPoint.MatchPt;
            var distance = MathHelper.Distance(nextMapImagePos, currPoint);
            if (distance < minDistance)
            {
                minDistance = distance;
                minDistancePoint = nextPoint;
                minDistanceIndex = i + 1;
                // if (distance > 5)
                // {
                //     minDistanceButGt = distance;
                //     minDistancePointButGt = nextPoint;
                //     minDistanceIndexButGt = i;
                // }
            }

            if (GiPathPoint.IsKeyPoint(nextPoint))
            {
                break;
            }
        }

        // return minDistanceButGt >= double.MaxValue ? (minDistancePointButGt, minDistanceIndexButGt) : (minDistancePoint, minDistanceIndex);
        return (minDistancePoint, minDistanceIndex);
    }

    public int GetOffsetAngle()
    {
        var angle1 = GetCharacterOrientationAngle();
        Simulation.SendInput.Mouse.MoveMouseBy(CharMovingUnit, 0);
        Sleep(500);
        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
        Sleep(100);
        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
        // Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_W).Sleep(100).KeyUp(User32.VK.VK_W);
        Sleep(1000);
        var angle2 = GetCharacterOrientationAngle();
        var angleOffset = angle2 - angle1;
        Logger.LogInformation("横向移动偏移量校准：鼠标平移{CharMovingUnit}单位，角度转动{AngleOffset}", CharMovingUnit, angleOffset);
        return angleOffset;
    }

    public Mat? GetMiniMapMat(ImageRegion ra)
    {
        var paimon = ra.Find(ElementAssets.Instance.PaimonMenuRo);
        if (paimon.IsExist())
        {
            return new Mat(ra.SrcMat, new Rect(paimon.X + 24, paimon.Y - 15, 210, 210));
        }

        return null;
    }

    public int GetCharacterOrientationAngle()
    {
        var ra = CaptureToRectArea();
        var miniMapMat = GetMiniMapMat(ra) ?? throw new InvalidOperationException("当前不在主界面");
        var angle = CharacterOrientation.Compute(miniMapMat);
        Logger.LogInformation("当前角度：{Angle}", angle);
        // CameraOrientation.DrawDirection(ra, angle);
        return angle;
    }
}
