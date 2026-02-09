using BetterGenshinImpact.Helpers;
﻿using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Model.Area;
using Fischless.GameCapture;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask.Common;

public class TaskControl
{
    public static ILogger Logger { get; } = App.GetLogger<TaskControl>();

    public static readonly SemaphoreSlim TaskSemaphore = new(1, 1);


    public static void CheckAndSleep(int millisecondsTimeout)
    {
        TrySuspend();
        CheckAndActivateGameWindow();

        Thread.Sleep(millisecondsTimeout);
    }

    public static void Sleep(int millisecondsTimeout)
    {
        NewRetry.Do(() =>
        {
            TrySuspend();
            CheckAndActivateGameWindow();
        }, TimeSpan.FromSeconds(1), 100);
        Thread.Sleep(millisecondsTimeout);
    }

    private static bool IsKeyPressed(User32.VK key)
    {
        // 获取按键状态
        var state = User32.GetAsyncKeyState((int)key);

        // 检查高位是否为 1（表示按键被按下）
        return (state & 0x8000) != 0;
    }

    public static void TrySuspend()
    {
        
        var first = true;
        //此处为了记录最开始的暂停状态
        var isSuspend = RunnerContext.Instance.IsSuspend;
        while (RunnerContext.Instance.IsSuspend)
        {
            if (first)
            {
                RunnerContext.Instance.StopAutoPick();
                //使快捷键本身释放
                Thread.Sleep(300);
                foreach (User32.VK key in Enum.GetValues(typeof(User32.VK)))
                {
                    // 检查键是否被按下
                    if (IsKeyPressed(key)) // 强制转换 VK 枚举为 int
                    {
                        Logger.LogWarning($"{Lang.S["Gen_10289_b90d02"]});
                        Simulation.SendInput.Keyboard.KeyUp(key);
                    }
                }

                Logger.LogWarning(Lang.S["GameTask_11482_ebb4f9"]);
                foreach (var item in RunnerContext.Instance.SuspendableDictionary)
                {
                    item.Value.Suspend();
                }

                first = false;
            }

            Thread.Sleep(1000);
        }

        //从暂停中解除
        if (isSuspend)
        {
            Logger.LogWarning(Lang.S["GameTask_11481_c68332"]);
            RunnerContext.Instance.ResumeAutoPick();
            foreach (var item in RunnerContext.Instance.SuspendableDictionary)
            {
                item.Value.Resume();
            }
        }
    }

    private static void CheckAndActivateGameWindow()
    {
        if (!TaskContext.Instance().Config.OtherConfig.RestoreFocusOnLostEnabled)
        {
            if (!SystemControl.IsGenshinImpactActiveByProcess())
            {
                var name = SystemControl.GetActiveByProcess();
                Logger.LogWarning($"{Lang.S["GameTask_10897_f60dcf"]});
                throw new RetryException(Lang.S["GameTask_10896_27f7d3"]);
            }
        }

        var count = 0;
        //未激活则尝试恢复窗口
        while (!SystemControl.IsGenshinImpactActiveByProcess())
        {
            if (count >= 10 && count % 10 == 0)
            {
                Logger.LogInformation(Lang.S["GameTask_11480_733796"]);
                SystemControl.MinimizeAndActivateWindow(TaskContext.Instance().GameHandle);
            }
            else
            {
                Logger.LogInformation(Lang.S["GameTask_11479_c4d2eb"]);
                SystemControl.FocusWindow(TaskContext.Instance().GameHandle);
            }

            count++;
            Thread.Sleep(1000);
        }
    }

    public static void Sleep(int millisecondsTimeout, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            throw new NormalEndException(Lang.S["GameTask_11478_7139b7"]);
        }

        if (millisecondsTimeout <= 0)
        {
            return;
        }

        NewRetry.Do(() =>
        {
            if (ct.IsCancellationRequested)
            {
                throw new NormalEndException(Lang.S["GameTask_11478_7139b7"]);
            }

            TrySuspend();
            CheckAndActivateGameWindow();
        }, TimeSpan.FromSeconds(1), 100);
        Thread.Sleep(millisecondsTimeout);
        if (ct.IsCancellationRequested)
        {
            throw new NormalEndException(Lang.S["GameTask_11478_7139b7"]);
        }
    }

    public static async Task Delay(int millisecondsTimeout, CancellationToken ct)
    {
        if (ct is { IsCancellationRequested: true })
        {
            throw new NormalEndException(Lang.S["GameTask_11478_7139b7"]);
        }

        if (millisecondsTimeout <= 0)
        {
            return;
        }

        NewRetry.Do(() =>
        {
            if (ct is { IsCancellationRequested: true })
            {
                throw new NormalEndException(Lang.S["GameTask_11478_7139b7"]);
            }

            TrySuspend();
            CheckAndActivateGameWindow();
        }, TimeSpan.FromSeconds(1), 100);
        await Task.Delay(millisecondsTimeout, ct);
        if (ct is { IsCancellationRequested: true })
        {
            throw new NormalEndException(Lang.S["GameTask_11478_7139b7"]);
        }
    }

    public static Mat CaptureGameImage(IGameCapture? gameCapture)
    {
        var image = gameCapture?.Capture();
        if (image == null)
        {
            Logger.LogWarning(Lang.S["GameTask_10321_65d26e"]);
            // 重试3次
            for (var i = 0; i < 3; i++)
            {
                image = gameCapture?.Capture();
                if (image != null)
                {
                    return image;
                }

                Sleep(30);
            }

            throw new Exception(Lang.S["GameTask_11477_c6c35f"]);
        }
        else
        {
            return image;
        }
    }

    public static Mat? CaptureGameImageNoRetry(IGameCapture? gameCapture)
    {
        return gameCapture?.Capture();
    }

    /// <summary>
    /// 自动判断当前运行上下文中截图方式，并选择合适的截图方式返回
    /// </summary>
    /// <returns></returns>
    public static ImageRegion CaptureToRectArea(bool forceNew = false)
    {
        var image = CaptureGameImage(TaskTriggerDispatcher.GlobalGameCapture);
        var content = new CaptureContent(image, 0, 0);
        return content.CaptureRectArea;
    }
}