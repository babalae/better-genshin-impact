using System;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Model.Enum;
using BetterGenshinImpact.View.Drawable;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

public class AutoTrackPathTask
{
    private readonly AutoTrackPathParam _taskParam;

    public AutoTrackPathTask(AutoTrackPathParam taskParam)
    {
        _taskParam = taskParam;
    }

    public async void Start()
    {
        var hasLock = false;
        try
        {
            AutoFightAssets.DestroyInstance();
            hasLock = await TaskSemaphore.WaitAsync(0);
            if (!hasLock)
            {
                Logger.LogError("启动自动路线功能失败：当前存在正在运行中的独立任务，请不要重复执行任务！");
                return;
            }

            Init();
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

        // 计算传送点位置离哪个地图切换后的中心点最近，切换到该地图

        // 移动部分内容测试移动偏移

        // 快速移动到目标传送点所在的区域

        // 计算坐标后点击

        // 触发一次快速传送功能
    }

    public void TpByF1(string name)
    {
        // 传送到指定传送点
    }
}
