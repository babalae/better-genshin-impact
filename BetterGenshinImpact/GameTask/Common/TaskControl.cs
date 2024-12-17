using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Model.Area;
using Fischless.GameCapture;
using Microsoft.Extensions.Logging;
using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

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
        if (SystemControl.Suspend())
        {
            Logger.LogWarning("快捷键触发暂停，等待解除");
            throw new RetryException("快捷键触发暂停");
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
            if (SystemControl.Suspend())
            {
                Logger.LogWarning("快捷键触发暂停，等待解除");
                throw new RetryException("快捷键触发暂停");
            }
        }, TimeSpan.FromSeconds(1), 100);
        Thread.Sleep(millisecondsTimeout);
    }

    // public static void Sleep(int millisecondsTimeout, CancellationTokenSource? cts)
    // {
    //     if (cts is { IsCancellationRequested: true })
    //     {
    //         throw new NormalEndException("取消自动任务");
    //     }
    //
    //     if (millisecondsTimeout <= 0)
    //     {
    //         return;
    //     }
    //
    //     NewRetry.Do(() =>
    //     {
    //         if (cts is { IsCancellationRequested: true })
    //         {
    //             throw new NormalEndException("取消自动任务");
    //         }
    //
    //         if (!SystemControl.IsGenshinImpactActiveByProcess())
    //         {
    //             Logger.LogInformation("当前获取焦点的窗口不是原神，暂停");
    //             throw new RetryException("当前获取焦点的窗口不是原神");
    //         }
    //     }, TimeSpan.FromSeconds(1), 100);
    //     Thread.Sleep(millisecondsTimeout);
    //     if (cts is { IsCancellationRequested: true })
    //     {
    //         throw new NormalEndException("取消自动任务");
    //     }
    // }

    // public static async Task Delay(int millisecondsTimeout, CancellationTokenSource cts)
    // {
    //     if (cts is { IsCancellationRequested: true })
    //     {
    //         throw new NormalEndException("取消自动任务");
    //     }
    //
    //     if (millisecondsTimeout <= 0)
    //     {
    //         return;
    //     }
    //
    //     NewRetry.Do(() =>
    //     {
    //         if (cts is { IsCancellationRequested: true })
    //         {
    //             throw new NormalEndException("取消自动任务");
    //         }
    //
    //         if (!SystemControl.IsGenshinImpactActiveByProcess())
    //         {
    //             Logger.LogInformation("当前获取焦点的窗口不是原神，暂停");
    //             throw new RetryException("当前获取焦点的窗口不是原神");
    //         }
    //     }, TimeSpan.FromSeconds(1), 100);
    //     await Task.Delay(millisecondsTimeout, cts.Token);
    //     if (cts is { IsCancellationRequested: true })
    //     {
    //         throw new NormalEndException("取消自动任务");
    //     }
    // }

    public static void Sleep(int millisecondsTimeout, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            throw new NormalEndException("取消自动任务");
        }

        if (millisecondsTimeout <= 0)
        {
            return;
        }

        NewRetry.Do(() =>
        {
            if (ct.IsCancellationRequested)
            {
                throw new NormalEndException("取消自动任务");
            }

            if (!SystemControl.IsGenshinImpactActiveByProcess())
            {
                Logger.LogInformation("当前获取焦点的窗口不是原神，暂停");
                throw new RetryException("当前获取焦点的窗口不是原神");
            }
            if (SystemControl.Suspend())
            {
                Logger.LogWarning("快捷键触发暂停，等待解除");
                throw new RetryException("快捷键触发暂停");
            }
        }, TimeSpan.FromSeconds(1), 100);
        Thread.Sleep(millisecondsTimeout);
        if (ct.IsCancellationRequested)
        {
            throw new NormalEndException("取消自动任务");
        }
    }

    public static async Task Delay(int millisecondsTimeout, CancellationToken ct)
    {
        if (ct is { IsCancellationRequested: true })
        {
            throw new NormalEndException("取消自动任务");
        }

        if (millisecondsTimeout <= 0)
        {
            return;
        }

        NewRetry.Do(() =>
        {
            if (ct is { IsCancellationRequested: true })
            {
                throw new NormalEndException("取消自动任务");
            }

            if (!SystemControl.IsGenshinImpactActiveByProcess())
            {
                Logger.LogInformation("当前获取焦点的窗口不是原神，暂停");
                throw new RetryException("当前获取焦点的窗口不是原神");
            }
            if (SystemControl.Suspend())
            {
                Logger.LogWarning("快捷键触发暂停，等待解除");
                throw new RetryException("快捷键触发暂停");
            }
        }, TimeSpan.FromSeconds(1), 100);
        await Task.Delay(millisecondsTimeout, ct);
        if (ct is { IsCancellationRequested: true })
        {
            throw new NormalEndException("取消自动任务");
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
            for (var i = 0; i < 5; i++)
            {
                bitmap = gameCapture?.Capture();
                if (bitmap != null)
                {
                    return bitmap;
                }

                Sleep(30);
            }

            throw new System.Exception("尝试多次后,截图失败!");
        }
        else
        {
            return bitmap;
        }
    }

    // private static CaptureContent CaptureToContent(IGameCapture? gameCapture)
    // {
    //     var bitmap = CaptureGameBitmap(gameCapture);
    //     return new CaptureContent(bitmap, 0, 0);
    // }
    //
    // public static CaptureContent CaptureToContent()
    // {
    //     return CaptureToContent(TaskTriggerDispatcher.GlobalGameCapture);
    // }

    // public static ImageRegion CaptureToRectArea()
    // {
    //     return CaptureToContent(TaskTriggerDispatcher.GlobalGameCapture).CaptureRectArea;
    // }

    // /// <summary>
    // /// 此方法 TaskDispatcher至少处于 DispatcherCaptureModeEnum.CacheCaptureWithTrigger 状态才能使用
    // /// </summary>
    // /// <returns></returns>
    // [Obsolete]
    // public static CaptureContent GetContentFromDispatcher()
    // {
    //     return TaskTriggerDispatcher.Instance().GetLastCaptureContent();
    // }

    // /// <summary>
    // /// 此方法 TaskDispatcher至少处于 DispatcherCaptureModeEnum.CacheCaptureWithTrigger 状态才能使用
    // /// </summary>
    // /// <returns></returns>
    // public static ImageRegion GetRectAreaFromDispatcher()
    // {
    //     return TaskTriggerDispatcher.Instance().GetLastCaptureContent().CaptureRectArea;
    // }

    /// <summary>
    /// 自动判断当前运行上下文中截图方式，并选择合适的截图方式返回
    /// </summary>
    /// <returns></returns>
    public static ImageRegion CaptureToRectArea(bool forceNew = false)
    {
        return TaskTriggerDispatcher.Instance().CaptureToRectArea(forceNew);
    }
}
