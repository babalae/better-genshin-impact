using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoFight.Config;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoTrackPath.Model;
using BetterGenshinImpact.GameTask.Common.Map;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.Enum;
using BetterGenshinImpact.GameTask.QuickTeleport.Assets;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.View.Drawable;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

public class AutoTrackPathTask
{
    private readonly AutoTrackPathParam _taskParam;
    private readonly Random _rd = new Random();

    private readonly List<GiWorldPosition> _tpPositions;

    private readonly Dictionary<string, double[]> _countryPositions = new()
    {
        { "蒙德", [-876, 2278] },
        { "璃月", [270, -666] },
        { "稻妻", [-4400, -3050] },
        { "须弥", [2877, -374] },
        { "枫丹", [4515, 3631] },
    };

    public AutoTrackPathTask(AutoTrackPathParam taskParam)
    {
        _taskParam = taskParam;
        var json = File.ReadAllText(Global.Absolute(@"GameTask\AutoTrackPath\Assets\tp.json"));
        _tpPositions = JsonSerializer.Deserialize<List<GiWorldPosition>>(json, ConfigService.JsonOptions) ?? throw new Exception("tp.json deserialize failed"); ;
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

            Tp(_tpPositions[260].X, _tpPositions[260].Y);
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
            TaskTriggerDispatcher.Instance().SetCacheCaptureMode(DispatcherCaptureModeEnum.OnlyTrigger);
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

    public void DoTask()
    {
        // 解析路线，第一个点为起点

        // 找到起点最近的传送点位置

        // 初始化全地图特征

        // --- 地图传送模块 ---

        // M 打开地图识别当前位置，中心点为当前位置

        // 计算传送点位置离哪个地图切换后的中心点最近，切换到该地图

        // 快速移动到目标传送点所在的区域

        // 计算坐标后点击

        // 触发一次快速传送功能

        // --- 地图传送模块 ---

        // 横向移动偏移量校准，移动指定偏移、按下W后识别朝向

        // 针对点位进行直线追踪
    }

    public void Tp(string name)
    {
        // 通过大地图传送到指定传送点
    }

    public void Tp(double tpX, double tpY)
    {
        // 通过大地图传送到指定坐标最近的传送点，然后移动到指定坐标

        // 获取最近的传送点位置
        var (x, y) = GetRecentlyTpPoint(tpX, tpY);
        Logger.LogInformation("({TpX},{TpY}) 最近的传送点位置 ({X},{Y})", tpX, tpY, x, y);

        // M 打开地图识别当前位置，中心点为当前位置
        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_M);

        Sleep(1000);

        // 计算传送点位置离哪个地图切换后的中心点最近，切换到该地图
        SwitchRecentlyCountryMap(x, y);

        // 移动地图到指定传送点位置
        Debug.WriteLine("移动地图到指定传送点位置");
        MoveMapTo(x, y);

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
        using var ra = GetRectAreaFromDispatcher();
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
        using var ra = GetRectAreaFromDispatcher();
        using var mapScaleButtonRa = ra.Find(QuickTeleportAssets.Instance.MapScaleButtonRo);
        if (mapScaleButtonRa.IsExist())
        {
            var rect = EntireMap.Instance.GetBigMapPositionByFeatureMatch(ra.SrcGreyMat);
            Debug.WriteLine("识别大地图在全地图位置矩形：" + rect);
            return MapCoordinate.Main2048ToGame(rect);
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
            var ra = GetRectAreaFromDispatcher();
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

    public void TpByF1(string name)
    {
        // 传送到指定传送点
    }
}
