using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.View.Drawable;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoMusicGame;

public class AutoMusicGameTask(AutoMusicGameParam taskParam)
{
    private readonly ConcurrentDictionary<User32.VK, int> _keyX = new()
    {
        [User32.VK.VK_A] = 417,
        [User32.VK.VK_S] = 632,
        [User32.VK.VK_D] = 846,
        [User32.VK.VK_J] = 1065,
        [User32.VK.VK_K] = 1282,
        [User32.VK.VK_L] = 1500
    };

    private readonly int _keyY = 916;

    private readonly IntPtr _hWnd = TaskContext.Instance().GameHandle;

    public async void Start()
    {
        var hasLock = false;
        try
        {
            hasLock = await TaskSemaphore.WaitAsync(0);
            if (!hasLock)
            {
                Logger.LogError("启动自动战斗功能失败：当前存在正在运行中的独立任务，请不要重复执行任务！");
                return;
            }

            Init();

            var assetScale = TaskContext.Instance().SystemInfo.AssetScale;
            var taskFactory = new TaskFactory();
            var taskList = new List<Task>();

            // 计算按键位置
            var gameCaptureRegion = CaptureToRectArea();

            foreach (var keyValuePair in _keyX)
            {
                var (x, y) = gameCaptureRegion.ConvertPositionToGameCaptureRegion((int)(keyValuePair.Value * assetScale), (int)(_keyY * assetScale));
                // 添加任务
                taskList.Add(taskFactory.StartNew(() => DoWhitePressWin32(taskParam.Cts, keyValuePair.Key, new Point(x, y))));
            }

            Task.WaitAll([.. taskList]);
        }
        catch (NormalEndException)
        {
            Logger.LogInformation("手动中断自动活动音游");
        }
        catch (Exception e)
        {
            Logger.LogError(e.Message);
            Logger.LogDebug(e.StackTrace);
        }
        finally
        {
            VisionContext.Instance().DrawContent.ClearAll();
            TaskTriggerDispatcher.Instance().StartTimer();
            TaskSettingsPageViewModel.SetSwitchAutoFightButtonText(false);
            Logger.LogInformation("→ {Text}", "自动活动音游结束");

            if (hasLock)
            {
                TaskSemaphore.Release();
            }
        }
    }

    private void DoWhitePressWin32(CancellationTokenSource cts, User32.VK key, Point point)
    {
        while (!cts.Token.IsCancellationRequested)
        {
            Thread.Sleep(10);
            // Stopwatch sw = new();
            // sw.Start();
            var hdc = User32.GetDC(_hWnd);
            var c = Gdi32.GetPixel(hdc, point.X, point.Y);
            Gdi32.DeleteDC(hdc);

            if (c.B < 220)
            {
                KeyDown(key);
                while (!cts.Token.IsCancellationRequested)
                {
                    Thread.Sleep(10);
                    hdc = User32.GetDC(_hWnd);
                    c = Gdi32.GetPixel(hdc, point.X, point.Y);
                    Gdi32.DeleteDC(hdc);
                    if (c.B >= 220)
                    {
                        break;
                    }
                }
                KeyUp(key);
            }

            // sw.Stop();
            // Debug.WriteLine($"GetPixel 耗时：{sw.ElapsedMilliseconds} （{point.X},{point.Y}）颜色{c.R},{c.G},{c.B}");
        }
    }

    private void KeyUp(User32.VK key)
    {
        Simulation.SendInputEx.Keyboard.KeyUp(key);
    }

    private void KeyDown(User32.VK key)
    {
        Simulation.SendInputEx.Keyboard.KeyDown(key);
    }

    private void Init()
    {
        LogScreenResolution();
        Logger.LogInformation("→ {Text}", "活动音游，启动！");
        SystemControl.ActivateWindow();
        TaskTriggerDispatcher.Instance().StopTimer();
        Sleep(TaskContext.Instance().Config.TriggerInterval * 5, taskParam.Cts); // 等待缓存图像
    }

    private void LogScreenResolution()
    {
        var gameScreenSize = SystemControl.GetGameScreenRect(TaskContext.Instance().GameHandle);
        if (gameScreenSize.Width * 9 != gameScreenSize.Height * 16)
        {
            Logger.LogWarning("游戏窗口分辨率不是 16:9 ！当前分辨率为 {Width}x{Height} , 非 16:9 分辨率的游戏可能无法正常使用自动活动音游功能 !", gameScreenSize.Width, gameScreenSize.Height);
        }
    }
}
