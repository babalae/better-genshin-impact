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

    private readonly IntPtr _hWnd = TaskContext.Instance().GameHandle;

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
            // var taskFactory = new TaskFactory();
            var taskList = new List<Task>();

            // 计算按键位置
            var gameCaptureRegion = CaptureToRectArea();

            foreach (var keyValuePair in _keyX)
            {
                var (x, y) = gameCaptureRegion.ConvertPositionToGameCaptureRegion((int)(keyValuePair.Value * assetScale), (int)(_keyY * assetScale));
                // 添加任务
                taskList.Add(Task.Run(async () => await DoWhitePressWin32(ct, keyValuePair.Key, new Point(x, y)), ct));
            }

            await Task.WhenAll(taskList);
        }
        finally
        {
            Simulation.ReleaseAllKey();
            Logger.LogInformation("结束自动演奏");
        }
    }

    private async Task DoWhitePressWin32(CancellationToken ct, User32.VK key, Point point)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(5, ct);
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
                    await Task.Delay(5, ct);
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

    // private async Task DoWhitePressWin32Default(CancellationToken ct, User32.VK key, Point point)
    // {
    //     while (!ct.IsCancellationRequested)
    //     {
    //         await Task.Delay(10, ct);
    //         var c = GetPixel(point.X, point.Y);
    //
    //         if (c.G < 220)
    //         {
    //             KeyDown(key);
    //             while (!ct.IsCancellationRequested)
    //             {
    //                 Thread.Sleep(10);
    //                 c = GetPixel(point.X, point.Y);
    //                 if (c.G >= 230 && c.G != 255)
    //                 {
    //                     if (point.X == 417)
    //                     {
    //                         Debug.WriteLine("打断颜色：" + c.R + "," + c.G + "," + c.B);
    //                     }
    //
    //                     break;
    //                 }
    //             }
    //
    //             KeyUp(key);
    //         }
    //     }
    // }

    // private async Task DoWhitePressWin32Default(CancellationToken ct, User32.VK key, Point point)
    // {
    //     while (!ct.IsCancellationRequested)
    //     {
    //         await Task.Delay(5, ct);
    //         var color = GetPixel(point.X, point.Y);
    //         int r = color.R, g = color.G, b = color.B;
    //
    //         if (r >= 140 && r <= 255 && g >= 100 && g <= 170 && b >= 230 && b <= 255)
    //         {
    //             // 按下按键
    //             KeyDown(key);
    //
    //             int z1 = 0;
    //             while (z1 < 3)
    //             {
    //                 await Task.Delay(5, ct);
    //                 color = GetPixel(point.X, point.Y);
    //                 int r1 = color.R, g1 = color.G, b1 = color.B;
    //                 var color2 = GetPixel(point.X + 2, point.Y + 2);
    //                 int r11 = color2.R, g11 = color2.G, b11 = color2.B;
    //
    //                 if ((r1 >= 140 && r1 <= 255 && g1 >= 100 && g1 <= 170) || (r11 >= 140 && r11 <= 255 && g11 >= 100 && g11 <= 170))
    //                 {
    //                     continue;
    //                 }
    //
    //                 z1++;
    //             }
    //
    //             Console.WriteLine($"{key} purple1 {r} {g} {b}");
    //
    //             int z2 = 0;
    //             while (z2 < 10)
    //             {
    //                 await Task.Delay(5, ct);
    //                 color = GetPixel(point.X, point.Y);
    //                 int r1 = color.R, g1 = color.G, b1 = color.B;
    //                 var color2 = GetPixel(point.X + 2, point.Y + 2);
    //                 int r11 = color2.R, g11 = color2.G, b11 = color2.B;
    //
    //                 if (g1 >= 100 && g1 <= 170 || g11 >= 100 && g11 <= 170)
    //                 {
    //                     continue;
    //                 }
    //
    //                 z2++;
    //             }
    //
    //             Console.WriteLine($"{key} purple - 紫键结束 {r} {g} {b}");
    //             KeyUp(key);
    //         }
    //
    //         if (r >= 230 && r <= 255 && g >= 170 && g <= 210 && b >= 50 && b <= 120)
    //         {
    //             KeyDown(key);
    //
    //             var color2 = GetPixel(point.X, point.Y);
    //             int r2 = color2.R, g2 = color2.G, b2 = color2.B;
    //
    //             
    //             while (g2 >= 170 && g2 <= 210 && b2 >= 50 && b2 <= 120)
    //             {
    //                 
    //                 await Task.Delay(5, ct);
    //                 color2 = GetPixel(point.X, point.Y);
    //                 r2 = color2.R;
    //                 g2 = color2.G;
    //                 b2 = color2.B;
    //             }
    //
    //             KeyUp(key);
    //         }
    //     }
    // }

    private COLORREF GetPixel(int x, int y)
    {
        var hdc = User32.GetDC(_hWnd);
        var c = Gdi32.GetPixel(hdc, x, y);
        Gdi32.DeleteDC(hdc);
        return c;
    }


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