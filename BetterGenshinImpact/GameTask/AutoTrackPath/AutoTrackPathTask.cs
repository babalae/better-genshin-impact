using System;
using System.Diagnostics;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Common.Map;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.Enum;
using BetterGenshinImpact.GameTask.QuickTeleport.Assets;
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

    public AutoTrackPathTask(AutoTrackPathParam taskParam)
    {
        _taskParam = taskParam;
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

            Tp(4726.575195, 1852.823975);
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

    public void Tp(double x, double y)
    {
        // 通过大地图传送到指定坐标最近的传送点，然后移动到指定坐标

        // M 打开地图识别当前位置，中心点为当前位置
        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_M);

        Sleep(1000);

        // 计算传送点位置离哪个地图切换后的中心点最近，切换到该地图
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
        var diffMapX = newBigMapCenterPoint.X - bigMapCenterPoint.X;
        var diffMapY = newBigMapCenterPoint.Y - bigMapCenterPoint.Y;
        Debug.WriteLine($"每100移动的地图距离：({diffMapX},{diffMapY})");

        if (diffMapX > 0 && diffMapY > 0)
        {
            // // 计算需要移动的次数
            var moveCount = (int)(xOffset / diffMapX); // 向下取整 本来还要加1的，但是已经移动了一次了
            Debug.WriteLine("X需要移动的次数：" + moveCount);
            for (var i = 0; i < moveCount; i++)
            {
                MouseMoveMapX(diffMouseX);
            }

            moveCount = (int)(yOffset / diffMapY); // 向下取整 本来还要加1的，但是已经移动了一次了
            Debug.WriteLine("Y需要移动的次数：" + moveCount);
            for (var i = 0; i < moveCount; i++)
            {
                MouseMoveMapY(diffMouseY);
            }
        }

        // 快速移动到目标传送点所在的区域

        // 计算坐标后点击

        // 触发一次快速传送功能
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

    public Point GetPositionFromBigMap()
    {
        // 判断是否在地图界面
        using var ra = GetRectAreaFromDispatcher();
        using var mapScaleButtonRa = ra.Find(QuickTeleportAssets.Instance.MapScaleButtonRo);
        if (mapScaleButtonRa.IsExist())
        {
            var bigMapRect = EntireMap.Instance.GetBigMapPositionByFeatureMatch(ra.SrcGreyMat);
            // 中心点
            var bigMapCenterPoint = bigMapRect.GetCenterPoint();
            Debug.WriteLine("识别大地图中心点：" + bigMapCenterPoint);
            var gamePoint = MapCoordinate.Main1024ToGame(bigMapCenterPoint);
            Debug.WriteLine("转换到游戏坐标：" + gamePoint);
            return gamePoint;
        }
        else
        {
            throw new InvalidOperationException("当前不在地图界面");
        }
    }

    public void TpByF1(string name)
    {
        // 传送到指定传送点
    }
}
