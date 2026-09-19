using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.QuickSereniteaPot;

internal static class SereniteaPotUi
{
    internal static async Task<bool> WaitForMainUi(CancellationToken ct, string stage, bool recordFailure = true)
    {
        using var clock = SereniteaPotTaskControl.CreateTimer();
        var ready = await SereniteaPotWaiter.WaitAsync(() =>
        {
            using var capture = TaskControl.CaptureToRectArea(forceNew: true);
            return Bv.IsInMainUi(capture);
        }, SereniteaPotTaskControl.Delay, ct, TimeSpan.FromSeconds(30), timeProvider: clock);

        if (!ready)
        {
            TaskControl.Logger.LogWarning("尘歌壶:{Stage} 未确认稳定主界面", stage);
            if (recordFailure) SaveFailure(stage);
            else SaveCapture(stage);
        }
        return ready;
    }

    internal static async Task<bool> WaitForEntry(CancellationToken ct, string stage, bool afterTeleport = true)
    {
        using var watch = SereniteaPotTaskControl.CreateTimer();
        var mainUi = false;
        var inPot = false;
        var ready = await SereniteaPotWaiter.WaitAsync(() =>
        {
            using var capture = TaskControl.CaptureToRectArea(forceNew: true);
            mainUi = Bv.IsInMainUi(capture);
            using var finger = capture.Find(ElementRecognition.Get("FingerIcon", capture));
            inPot = finger.IsExist();
            return mainUi && inPot;
        }, SereniteaPotTaskControl.Delay, ct, TimeSpan.FromSeconds(afterTeleport ? 60 : 15),
            afterTeleport ? TimeSpan.FromSeconds(5) : TimeSpan.Zero, timeProvider: watch);

        if (ready)
        {
            TaskControl.Logger.LogInformation("尘歌壶:{Stage} 已确认稳定主界面和壶内标识，耗时 {Seconds:F1} 秒",
                stage, watch.Elapsed.TotalSeconds);
        }
        else
        {
            TaskControl.Logger.LogWarning("尘歌壶:{Stage} 等待超时，主界面={MainUi}，壶内标识={InPot}，耗时 {Seconds:F1} 秒",
                stage, mainUi, inPot, watch.Elapsed.TotalSeconds);
            SaveFailure(stage);
        }
        return ready;
    }

    // 只在失败阶段保存一张图；诊断失败不能覆盖原始错误。
    internal static void SaveFailure(string stage)
    {
        if (SereniteaPotTestLogSink.Current is { } test) test.FailureStage ??= stage;
        SaveCapture(stage);
    }

    internal static void SaveCapture(string stage)
    {
        try
        {
            using var capture = TaskControl.CaptureToRectArea(forceNew: true);
            SaveCapture(stage, capture);
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogDebug(e, "获取尘歌壶诊断截图时发生异常：{Stage}", stage);
        }
    }

    internal static void SaveCapture(string stage, ImageRegion capture)
    {
        try
        {
            string? path;
            if (SereniteaPotTestLogSink.Current is { } test)
            {
                path = Path.Combine(test.DirectoryPath,
                    $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-p{Environment.ProcessId}-{stage}-{Guid.NewGuid():N}.png");
                if (!Cv2.ImWrite(path, capture.SrcMat)) { File.Delete(path); return; }
            }
            else
            {
                path = SereniteaPotScreenshotStore.Save(Global.Absolute(Path.Combine("log", "sereniteapot")),
                    stage, file => Cv2.ImWrite(file, capture.SrcMat));
            }
            if (path == null) return;
            TaskControl.Logger.LogWarning("尘歌壶诊断:{Stage}，前台进程={Foreground}，截图={Path}",
                stage, SystemControl.GetActiveByProcess(), path);
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogDebug(e, "保存尘歌壶失败截图时发生异常：{Stage}", stage);
        }
    }
}
