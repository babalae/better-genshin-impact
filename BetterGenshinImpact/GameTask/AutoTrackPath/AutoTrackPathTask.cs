using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoTrackPath.Model;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Map;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.Enum;
using BetterGenshinImpact.GameTask.QuickTeleport.Assets;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.View.Drawable;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using static Vanara.PInvoke.Gdi32;
using Point = OpenCvSharp.Point;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

/// <summary>
/// 坐标计算原则
/// 1. 所有非矩形点位坐标，优先转换为游戏内原神坐标系
/// 2. 所有涉及矩形运算的，优先转换为全地图坐标系
/// 3. 所有涉及小地图视角角度运算的，优先转换为warpPolar所使用的度数标准
/// </summary>
public class AutoTrackPathTask
{
    private readonly AutoTrackPathParam _taskParam;
    private readonly Random _rd = new Random();

    private readonly List<GiWorldPosition> _tpPositions;

    private readonly Dictionary<string, double[]> _countryPositions = MapAssets.Instance.CountryPositions;

    private GiPath _way;

    // 视角偏移移动单位
    private const int CharMovingUnit = 500;

    public AutoTrackPathTask(AutoTrackPathParam taskParam)
    {
        _taskParam = taskParam;
        _tpPositions = MapAssets.Instance.TpPositions;

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
            TaskTriggerDispatcher.Instance().SetCacheCaptureMode(DispatcherCaptureModeEnum.NormalTrigger);
            TaskSettingsPageViewModel.SetSwitchAutoFightButtonText(false);
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
        TaskTriggerDispatcher.Instance().SetCacheCaptureMode(DispatcherCaptureModeEnum.OnlyCacheCapture);
        Sleep(TaskContext.Instance().Config.TriggerInterval * 5, _taskParam.Cts); // 等待缓存图像
    }

    public void Stop()
    {
        _taskParam.Cts.Cancel();
    }

    public async Task DoTask()
    {
        // 1. 传送到最近的传送点
        var first = _way.WayPointList[0]; // 解析路线，第一个点为起点
        Tp(first.Pt.X, first.Pt.Y);

        // 2. 等待传送完成
        Sleep(1000);
        NewRetry.Do((Action)(() =>
        {
            var ra = TaskControl.CaptureToRectArea();
            var miniMapMat = GetMiniMapMat(ra);
            if (miniMapMat == null)
            {
                throw new RetryException("等待传送完成");
            }
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
        _taskParam.Cts.Token.Register(trackCts.Cancel);
        var trackTask = Track(_way.WayPointList, angleOffset, trackCts);
        trackTask.Start();
        var refreshStatusTask = RefreshStatus(trackCts);
        refreshStatusTask.Start();
        var jumpTask = Jump(trackCts);
        jumpTask.Start();
        await Task.WhenAll(trackTask, refreshStatusTask, jumpTask);
    }

    private MotionStatus _motionStatus = MotionStatus.Normal;

    public Task Jump(CancellationTokenSource trackCts)
    {
        return new Task(() =>
        {
            while (!_taskParam.Cts.Token.IsCancellationRequested && !trackCts.Token.IsCancellationRequested)
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

    public Task RefreshStatus(CancellationTokenSource trackCts)
    {
        return new Task(() =>
        {
            while (!_taskParam.Cts.Token.IsCancellationRequested && !trackCts.Token.IsCancellationRequested)
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
            while (!_taskParam.Cts.IsCancellationRequested)
            {
                var ra = CaptureToRectArea();
                var miniMapMat = GetMiniMapMat(ra);
                if (miniMapMat == null)
                {
                    throw new InvalidOperationException("当前不在主界面");
                }

                // 注意游戏坐标系的角度是顺时针的
                var miniMapRect = EntireMap.Instance.GetMiniMapPositionByFeatureMatch(miniMapMat);
                if (miniMapRect == Rect.Empty)
                {
                    Debug.WriteLine("识别小地图位置失败");
                    continue;
                }

                var currMapImageAvatarPos = miniMapRect.GetCenterPoint();
                var (nextMapImagePathPoint, nextPointIndex) = GetNextPoint(currMapImageAvatarPos, pList, currIndex); // 动态计算下个点位
                var nextMapImagePathPos = nextMapImagePathPoint.MatchRect.GetCenterPoint();
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
    public (GiPathPoint, int) GetNextPoint(Point currPoint, List<GiPathPoint> pList, int currIndex)
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
            var nextMapImagePos = nextPoint.MatchRect.GetCenterPoint();
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
        Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_W).Sleep(100).KeyUp(User32.VK.VK_W);
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
        var miniMapMat = GetMiniMapMat(ra);
        if (miniMapMat == null)
        {
            throw new InvalidOperationException("当前不在主界面");
        }

        var angle = CharacterOrientation.Compute(miniMapMat);
        Logger.LogInformation("当前角度：{Angle}", angle);
        // CameraOrientation.DrawDirection(ra, angle);
        return angle;
    }

    /// <summary>
    /// 通过大地图传送到指定坐标最近的传送点，然后移动到指定坐标
    /// </summary>
    /// <param name="tpX"></param>
    /// <param name="tpY"></param>
    public void Tp(double tpX, double tpY)
    {
        // 获取最近的传送点位置
        var (x, y) = GetRecentlyTpPoint(tpX, tpY);
        Logger.LogInformation("({TpX},{TpY}) 最近的传送点位置 ({X},{Y})", tpX, tpY, x, y);

        // M 打开地图识别当前位置，中心点为当前位置
        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_M);

        Sleep(1000);

        // 计算传送点位置离哪个地图切换后的中心点最近，切换到该地图
        SwitchRecentlyCountryMap(x, y);

        // 移动地图到指定传送点位置
        // Debug.WriteLine("移动地图到指定传送点位置");
        // MoveMapTo(x, y);

        // 计算坐标后点击
        var bigMapInAllMapRect = GetBigMapRect();
        while (!bigMapInAllMapRect.Contains((int)x, (int)y))
        {
            Debug.WriteLine($"({x},{y}) 不在 {bigMapInAllMapRect} 内，继续移动");
            Logger.LogInformation("传送点不在当前大地图范围内，继续移动");
            MoveMapTo(x, y);
            bigMapInAllMapRect = GetBigMapRect();
        }

        // Debug.WriteLine($"({x},{y}) 在 {bigMapInAllMapRect} 内，计算它在窗体内的位置");
        // 注意这个坐标的原点是中心区域某个点，所以要转换一下点击坐标（点击坐标是左上角为原点的坐标系），不能只是缩放
        var (picX, picY) = MapCoordinate.GameToMain2048(x, y);
        var picRect = MapCoordinate.GameToMain2048(bigMapInAllMapRect);
        Debug.WriteLine($"({picX},{picY}) 在 {picRect} 内，计算它在窗体内的位置");
        var captureRect = TaskContext.Instance().SystemInfo.ScaleMax1080PCaptureRect;
        var clickX = (int)((picX - picRect.X) / picRect.Width * captureRect.Width);
        var clickY = (int)((picY - picRect.Y) / picRect.Height * captureRect.Height);
        Logger.LogInformation("点击传送点：({X},{Y})", clickX, clickY);
        using var ra = CaptureToRectArea();
        ra.ClickTo(clickX, clickY);

        // 触发一次快速传送功能
    }

    /// <summary>
    /// 移动地图到指定传送点位置
    /// 可能会移动不对，所以可以重试此方法
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    public void MoveMapTo(double x, double y)
    {
        var bigMapCenterPoint = GetPositionFromBigMap();
        // 移动部分内容测试移动偏移
        var (xOffset, yOffset) = (x - bigMapCenterPoint.X, y - bigMapCenterPoint.Y);

        var diffMouseX = 100; // 每次移动的距离
        if (xOffset < 0)
        {
            diffMouseX = -diffMouseX;
        }

        var diffMouseY = 100; // 每次移动的距离
        if (yOffset < 0)
        {
            diffMouseY = -diffMouseY;
        }

        // 先移动到屏幕中心附近随机点位置，避免地图移动无效
        MouseMoveMapX(diffMouseX);
        MouseMoveMapY(diffMouseY);
        var newBigMapCenterPoint = GetPositionFromBigMap();
        var diffMapX = Math.Abs(newBigMapCenterPoint.X - bigMapCenterPoint.X);
        var diffMapY = Math.Abs(newBigMapCenterPoint.Y - bigMapCenterPoint.Y);
        Debug.WriteLine($"每100移动的地图距离：({diffMapX},{diffMapY})");

        // 快速移动到目标传送点所在的区域
        if (diffMapX > 10 && diffMapY > 10)
        {
            // // 计算需要移动的次数
            var moveCount = (int)Math.Abs(xOffset / diffMapX); // 向下取整 本来还要加1的，但是已经移动了一次了
            Debug.WriteLine("X需要移动的次数：" + moveCount);
            for (var i = 0; i < moveCount; i++)
            {
                MouseMoveMapX(diffMouseX);
            }

            moveCount = (int)Math.Abs(yOffset / diffMapY); // 向下取整 本来还要加1的，但是已经移动了一次了
            Debug.WriteLine("Y需要移动的次数：" + moveCount);
            for (var i = 0; i < moveCount; i++)
            {
                MouseMoveMapY(diffMouseY);
            }
        }
    }

    public void MouseMoveMapX(int dx)
    {
        var moveUnit = dx > 0 ? 20 : -20;
        GameCaptureRegion.GameRegionMove((rect, _) => (rect.Width / 2d + _rd.Next(-rect.Width / 6, rect.Width / 6), rect.Height / 2d + _rd.Next(-rect.Height / 6, rect.Height / 6)));
        Simulation.SendInput.Mouse.LeftButtonDown().Sleep(200);
        for (var i = 0; i < dx / moveUnit; i++)
        {
            Simulation.SendInput.Mouse.MoveMouseBy(moveUnit, 0).Sleep(60); // 60 保证没有惯性
        }

        Simulation.SendInput.Mouse.LeftButtonUp().Sleep(200);
    }

    public void MouseMoveMapY(int dy)
    {
        var moveUnit = dy > 0 ? 20 : -20;
        GameCaptureRegion.GameRegionMove((rect, _) => (rect.Width / 2d + _rd.Next(-rect.Width / 6, rect.Width / 6), rect.Height / 2d + _rd.Next(-rect.Height / 6, rect.Height / 6)));
        Simulation.SendInput.Mouse.LeftButtonDown().Sleep(200);
        // 原神地图在小范围内移动是无效的，所以先随便移动一下，所以肯定少移动一次
        for (var i = 0; i < dy / moveUnit; i++)
        {
            Simulation.SendInput.Mouse.MoveMouseBy(0, moveUnit).Sleep(60);
        }

        Simulation.SendInput.Mouse.LeftButtonUp().Sleep(200);
    }

    public static Point GetPositionFromBigMap()
    {
        var bigMapRect = GetBigMapRect();
        Debug.WriteLine("地图位置转换到游戏坐标：" + bigMapRect);
        var bigMapCenterPoint = bigMapRect.GetCenterPoint();
        Debug.WriteLine("地图中心坐标：" + bigMapCenterPoint);
        return bigMapCenterPoint;
    }

    public static Rect GetBigMapRect()
    {
        // 判断是否在地图界面
        using var ra = CaptureToRectArea();
        using var mapScaleButtonRa = ra.Find(QuickTeleportAssets.Instance.MapScaleButtonRo);
        if (mapScaleButtonRa.IsExist())
        {
            var rect = BigMap.Instance.GetBigMapPositionByFeatureMatch(ra.SrcGreyMat);
            Debug.WriteLine("识别大地图在全地图位置矩形：" + rect);
            const int s = 4 * 2; // 相对1024做4倍缩放
            return MapCoordinate.Main2048ToGame(new Rect(rect.X * s, rect.Y * s, rect.Width * s, rect.Height * s));
        }
        else
        {
            throw new InvalidOperationException("当前不在地图界面");
        }
    }

    /// <summary>
    /// 获取最近的传送点位置
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <returns></returns>
    public (int x, int y) GetRecentlyTpPoint(double x, double y)
    {
        var recentX = 0;
        var recentY = 0;
        var minDistance = double.MaxValue;
        foreach (var tpPosition in _tpPositions)
        {
            var distance = Math.Sqrt(Math.Pow(tpPosition.X - x, 2) + Math.Pow(tpPosition.Y - y, 2));
            if (distance < minDistance)
            {
                minDistance = distance;
                recentX = (int)Math.Round(tpPosition.X);
                recentY = (int)Math.Round(tpPosition.Y);
            }
        }

        return (recentX, recentY);
    }

    public void SwitchRecentlyCountryMap(double x, double y)
    {
        var bigMapCenterPoint = GetPositionFromBigMap();
        Logger.LogInformation("识别当前位置：{Pos}", bigMapCenterPoint);

        var minDistance = Math.Sqrt(Math.Pow(bigMapCenterPoint.X - x, 2) + Math.Pow(bigMapCenterPoint.Y - y, 2));
        var minCountry = "当前位置";
        foreach (var (country, position) in _countryPositions)
        {
            var distance = Math.Sqrt(Math.Pow(position[0] - x, 2) + Math.Pow(position[1] - y, 2));
            if (distance < minDistance)
            {
                minDistance = distance;
                minCountry = country;
            }
        }

        Logger.LogInformation("离目标传送点最近的区域是：{Country}", minCountry);
        if (minCountry != "当前位置")
        {
            GameCaptureRegion.GameRegionClick((rect, scale) => (rect.Width - 160 * scale, rect.Height - 60 * scale));
            Sleep(200, _taskParam.Cts);
            var ra = CaptureToRectArea();
            var list = ra.FindMulti(new RecognitionObject
            {
                RecognitionType = RecognitionTypes.Ocr,
                RegionOfInterest = new Rect(ra.Width / 2, 0, ra.Width / 2, ra.Height)
            });
            list.FirstOrDefault(r => r.Text.Contains(minCountry))?.Click();
            Logger.LogInformation("切换到区域：{Country}", minCountry);
            Sleep(500, _taskParam.Cts);
        }
    }

    public void Tp(string name)
    {
        // 通过大地图传送到指定传送点
    }

    public void TpByF1(string name)
    {
        // 传送到指定传送点
    }
}
