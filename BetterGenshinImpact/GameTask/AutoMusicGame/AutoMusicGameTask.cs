using BetterGenshinImpact.Core.Simulator;
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

public class AutoMusicGameTask(AutoMusicGameParam taskParam) : ISoloTask
{
    public string Name => "自动音游";

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

    public Task Start(CancellationToken ct)
    {
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
            taskList.Add(taskFactory.StartNew(() => DoWhitePressWin32(ct, keyValuePair.Key, new Point(x, y))));
        }

        Task.WaitAll([.. taskList]);
        return Task.CompletedTask;
    }

    private void DoWhitePressWin32(CancellationToken ct, User32.VK key, Point point)
    {
        while (!ct.IsCancellationRequested)
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
                while (!ct.IsCancellationRequested)
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
        Simulation.SendInput.Keyboard.KeyUp(key);
    }

    private void KeyDown(User32.VK key)
    {
        Simulation.SendInput.Keyboard.KeyDown(key);
    }

    private void Init()
    {
        LogScreenResolution();
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
