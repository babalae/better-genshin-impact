using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Model;
using Fischless.GameCapture;
using Microsoft.Extensions.Logging;
using System;
using System.Drawing;
using System.Threading;

namespace BetterGenshinImpact.GameTask.Common;

public class TaskControl
{
    public static ILogger Logger { get; } = App.GetLogger<TaskControl>();

    public static readonly SemaphoreSlim TaskSemaphore = new(1, 1);

    public static void CheckAndSleep(int millisecondsTimeout)
    {
        if (!SystemControl.IsGenshinImpactActiveByProcess())
        {
            Logger.LogInformation("当前获取焦点的窗口不是原神，停止执行");
            throw new NormalEndException("当前获取焦点的窗口不是原神");
        }

        Thread.Sleep(millisecondsTimeout);
    }

    public static void Sleep(int millisecondsTimeout)
    {
        NewRetry.Do(() =>
        {
            if (!SystemControl.IsGenshinImpactActiveByProcess())
            {
                Logger.LogInformation("当前获取焦点的窗口不是原神，暂停");
                throw new RetryException("当前获取焦点的窗口不是原神");
            }
        }, TimeSpan.FromSeconds(1), 100);
        Thread.Sleep(millisecondsTimeout);
    }

    public static void Sleep(int millisecondsTimeout, CancellationTokenSource? cts)
    {
        if (cts is { IsCancellationRequested: true })
        {
            throw new NormalEndException("取消自动任务");
        }

        if (millisecondsTimeout <= 0)
        {
            return;
        }

        NewRetry.Do(() =>
        {
            if (cts is { IsCancellationRequested: true })
            {
                throw new NormalEndException("取消自动任务");
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
            throw new NormalEndException("取消自动任务");
        }
    }

    public static void SleepWithoutThrow(int millisecondsTimeout, CancellationTokenSource cts)
    {
        try
        {
            Sleep(millisecondsTimeout, cts);
        }
        catch
        {
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
            // 重试5次
            for (var i = 0; i < 15; i++)
            {
                bitmap = gameCapture?.Capture();
                if (bitmap != null)
                {
                    return bitmap;
                }

                Sleep(30);
            }

            throw new Exception("尝试多次后,截图失败!");
        }
        else
        {
            return bitmap;
        }
    }

    public static Bitmap CaptureGameBitmap()
    {
        return CaptureGameBitmap(TaskTriggerDispatcher.GlobalGameCapture);
    }

    public static CaptureContent CaptureToContent(IGameCapture? gameCapture)
    {
        var bitmap = CaptureGameBitmap(gameCapture);
        return new CaptureContent(bitmap, 0, 0);
    }

    public static CaptureContent CaptureToContent()
    {
        return CaptureToContent(TaskTriggerDispatcher.GlobalGameCapture);
    }

    public static RectArea CaptureToRectArea()
    {
        return CaptureToContent().CaptureRectArea;
    }

    public static CaptureContent GetContentFromDispatcher()
    {
        return TaskTriggerDispatcher.Instance().GetLastCaptureContent();
    }

    public static RectArea GetRectAreaFromDispatcher()
    {
        return GetContentFromDispatcher().CaptureRectArea;
    }
}
