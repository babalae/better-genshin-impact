using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using Fischless.GameCapture;
using GeniusInvokationAutoToy.Utils;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.Common;

public class TaskControl
{
    public static ILogger Logger { get; } = App.GetLogger<TaskControl>();

    public static void Sleep(int millisecondsTimeout)
    {
        Retry.Do(() =>
        {
            if (!SystemControl.IsGenshinImpactActiveByProcess())
            {
                Logger.LogInformation("当前获取焦点的窗口不是原神，暂停");
                throw new RetryException("当前获取焦点的窗口不是原神");
            }
        }, TimeSpan.FromSeconds(1), 100);
        Thread.Sleep(millisecondsTimeout);
    }

    public static void Sleep(int millisecondsTimeout, CancellationTokenSource cts)
    {
        if (cts.IsCancellationRequested)
        {
            throw new NormalEndException("取消自动伐木任务");
        }
        Retry.Do(() =>
        {
            if (cts.IsCancellationRequested)
            {
                throw new NormalEndException("取消自动伐木任务");
            }
            if (!SystemControl.IsGenshinImpactActiveByProcess())
            {
                Logger.LogInformation("当前获取焦点的窗口不是原神，暂停");
                throw new RetryException("当前获取焦点的窗口不是原神");
            }
        }, TimeSpan.FromSeconds(1), 100);
        Thread.Sleep(millisecondsTimeout);
        if (cts.IsCancellationRequested)
        {
            throw new NormalEndException("取消自动伐木任务");
        }
    }

    public static Bitmap CaptureGameBitmap(IGameCapture? gameCapture)
    {
        var bitmap = gameCapture?.Capture();
        // wgc 缓冲区设置的2 所以至少截图3次
        if (gameCapture?.Mode == CaptureModes.WindowsGraphicsCapture)
        {
            for (int i = 0; i < 2; i++)
            {
                bitmap = gameCapture?.Capture();
                Sleep(50);
            }
        }

        if (bitmap == null)
        {
            Logger.LogWarning("截图失败!");
            throw new Exception("截图失败");
        }

        return bitmap;
    }

    public static CaptureContent CaptureToContent(IGameCapture? gameCapture)
    {
        var bitmap = CaptureGameBitmap(gameCapture);
        return new CaptureContent(bitmap, 0, 0, null);
    }
}