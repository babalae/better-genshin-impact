using BetterGenshinImpact.Core.Simulator;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Helpers;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoMusicGame;

public class AutoMusicGameTask(AutoMusicGameParam taskParam) : ISoloTask
{
    public string Name => "自动音游";


    // private readonly ConcurrentDictionary<User32.VK, int> _keyX = new()
    // {
    //     [User32.VK.VK_A] = 417,
    //     [User32.VK.VK_S] = 632,
    //     [User32.VK.VK_D] = 846,
    //     [User32.VK.VK_J] = 1065,
    //     [User32.VK.VK_K] = 1282,
    //     [User32.VK.VK_L] = 1500
    // };
    //
    // private readonly int _keyY = 916;


    private readonly ConcurrentDictionary<User32.VK, int> _keyX = new()
    {
        [User32.VK.VK_A] = 417,
        [User32.VK.VK_S] = 628,
        [User32.VK.VK_D] = 844,
        [User32.VK.VK_J] = 1061,
        [User32.VK.VK_K] = 1277,
        [User32.VK.VK_L] = 1493
    };

    private readonly int _keyY = 921;

    public async Task Start(CancellationToken ct)
    {
        Init();
        await StartWithOutInit(ct);
    }

    public async Task StartWithOutInit(CancellationToken ct)
    {
        try
        {
            Logger.LogInformation("开始自动演奏");
            var assetScale = TaskContext.Instance().SystemInfo.AssetScale;
            var keyPoints = new Dictionary<User32.VK, Point>();
            var pressedKeys = new HashSet<User32.VK>();

            // 用一次完整截图来计算判定点坐标。实际演奏阶段统一从全局截图器
            // 获取同一帧，并在这一帧上检查 A/S/D/J/K/L 六个判定点。
            using (var gameCaptureRegion = CaptureToRectArea())
            {
                foreach (var keyValuePair in _keyX)
                {
                    var (x, y) = gameCaptureRegion.ConvertPositionToGameCaptureRegion(
                        (int)(keyValuePair.Value * assetScale),
                        (int)(_keyY * assetScale));
                    keyPoints[keyValuePair.Key] = new Point(x, y);
                }
            }

            // 保持自动音游的取帧路径与全局截图器配置一致，
            // 避免 GDI GetDC/GetPixel 读取到滞后的 DWM 内容。
            while (!ct.IsCancellationRequested)
            {
                using var captureFrame = TaskTriggerDispatcher.GlobalGameCapture.Capture();
                if (captureFrame == null || captureFrame.Frame.Empty())
                {
                    await Task.Delay(5, ct);
                    continue;
                }

                var frame = captureFrame.Frame;
                foreach (var pair in keyPoints)
                {
                    var point = pair.Value;
                    if (point.X < 0 || point.X >= frame.Cols || point.Y < 0 || point.Y >= frame.Rows)
                    {
                        continue;
                    }

                    var blue = frame.At<Vec3b>(point.Y, point.X).Item0;

                    if (blue < 220)
                    {
                        if (pressedKeys.Add(pair.Key))
                        {
                            KeyDown(pair.Key);
                        }
                    }
                    else if (pressedKeys.Remove(pair.Key))
                    {
                        KeyUp(pair.Key);
                    }
                }

                await Task.Delay(5, ct);
            }
        }
        finally
        {
            Simulation.ReleaseAllKey();
            Logger.LogInformation("结束自动演奏");
        }
    }

    // private async Task DoWhitePressWin32(CancellationToken ct, User32.VK key, Point point)
    // {
    //     while (!ct.IsCancellationRequested)
    //     {
    //         await Task.Delay(5, ct);
    //         // Stopwatch sw = new();
    //         // sw.Start();
    //         var hdc = User32.GetDC(_hWnd);
    //         var c = Gdi32.GetPixel(hdc, point.X, point.Y);
    //         Gdi32.DeleteDC(hdc);

    //         if (c.B < 220)
    //         {
    //             KeyDown(key);
    //             while (!ct.IsCancellationRequested)
    //             {
    //                 await Task.Delay(5, ct);
    //                 hdc = User32.GetDC(_hWnd);
    //                 c = Gdi32.GetPixel(hdc, point.X, point.Y);
    //                 Gdi32.DeleteDC(hdc);
    //                 if (c.B >= 220)
    //                 {
    //                     break;
    //                 }
    //             }

    //             KeyUp(key);
    //         }

    //         // sw.Stop();
    //         // Debug.WriteLine($"GetPixel 耗时：{sw.ElapsedMilliseconds} （{point.X},{point.Y}）颜色{c.R},{c.G},{c.B}");
    //     }
    // }


    // private COLORREF GetPixel(int x, int y)
    // {
    //     var hdc = User32.GetDC(_hWnd);
    //     var c = Gdi32.GetPixel(hdc, x, y);
    //     Gdi32.DeleteDC(hdc);
    //     return c;
    // }


    private void KeyUp(User32.VK key)
    {
        Simulation.SendInput.Keyboard.KeyUp(key);
    }

    private void KeyDown(User32.VK key)
    {
        Simulation.SendInput.Keyboard.KeyDown(key);
    }

    public static void Init()
    {
        LogScreenResolution();
    }

    public static void LogScreenResolution()
    {
        AssertUtils.CheckGameResolution("自动音游");

        Logger.LogInformation("{Name}：回到游戏主界面时记得关闭自动音游任务！", "千音雅集");
        Logger.LogWarning("{Name}：默认的样式“轻漾涟漪”是{No}的！需要手动完成几首曲目获得{Money}千音币后兑换并使用胡桃样式“{Hutao}”！", "千音雅集", "不可用", 600, "疏影引蝶映梅红");
    }
}
