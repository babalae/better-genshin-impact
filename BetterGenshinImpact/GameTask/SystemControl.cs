using BetterGenshinImpact.Core.Simulator;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask;

public class SystemControl
{
    public static nint FindGenshinImpactHandle()
    {
        return FindHandleByProcessName("YuanShen", "GenshinImpact", "Genshin Impact Cloud Game");
    }

    public static nint StartFromLocal(string path)
    {
        // 使用 path 在新的线程中启动游戏
        var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        Thread.Sleep(10000);
        return FindGenshinImpactHandle();
    }

    public static bool IsGenshinImpactActiveByProcess()
    {
        var name = GetActiveProcessName();
        return name is "YuanShen" or "GenshinImpact" or "Genshin Impact Cloud Game";
    }

    public static bool IsGenshinImpactActive()
    {
        var hWnd = User32.GetForegroundWindow();
        return hWnd == TaskContext.Instance().GameHandle;
    }

    public static IntPtr GetForegroundWindowHandle()
    {
        return (IntPtr)User32.GetForegroundWindow();
    }

    public static nint FindHandleByProcessName(params string[] names)
    {
        foreach (var name in names)
        {
            var pros = Process.GetProcessesByName(name);
            if (pros.Any())
            {
                return pros[0].MainWindowHandle;
            }
        }

        return 0;
    }

    public static string? GetActiveProcessName()
    {
        try
        {
            var hWnd = User32.GetForegroundWindow();
            _ = User32.GetWindowThreadProcessId(hWnd, out var pid);
            var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    public static Process? GetProcessByHandle(IntPtr hWnd)
    {
        try
        {
            _ = User32.GetWindowThreadProcessId(hWnd, out var pid);
            var p = Process.GetProcessById((int)pid);
            return p;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return null;
        }
    }

    /// <summary>
    /// 获取窗口位置
    /// </summary>
    /// <param name="hWnd"></param>
    /// <returns></returns>
    public static RECT GetWindowRect(IntPtr hWnd)
    {
        // User32.GetWindowRect(hWnd, out var windowRect);
        DwmApi.DwmGetWindowAttribute<RECT>(hWnd, DwmApi.DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, out var windowRect);
        return windowRect;
    }

    /// <summary>
    /// 游戏本身分辨率获取
    /// </summary>
    /// <param name="hWnd"></param>
    /// <returns></returns>
    public static RECT GetGameScreenRect(IntPtr hWnd)
    {
        User32.GetClientRect(hWnd, out var clientRect);
        return clientRect;
    }

    /// <summary>
    /// GetWindowRect or GetGameScreenRect
    /// </summary>
    /// <param name="hWnd"></param>
    /// <returns></returns>
    public static RECT GetCaptureRect(IntPtr hWnd)
    {
        var windowRect = GetWindowRect(hWnd);
        var gameScreenRect = GetGameScreenRect(hWnd);
        var left = windowRect.Left;
        var top = windowRect.Top + windowRect.Height - gameScreenRect.Height;
        var right = left + gameScreenRect.Width;
        var bottom = top + gameScreenRect.Height;
        return new RECT(left, top, right, bottom);
    }

    public static void ActivateWindow(IntPtr hWnd)
    {
        User32.ShowWindow(hWnd, ShowWindowCommand.SW_RESTORE);
        User32.SetForegroundWindow(hWnd);
    }

    public static void ActivateWindow()
    {
        if (!TaskContext.Instance().IsInitialized)
        {
            throw new Exception("请先启动BetterGI");
        }
        ActivateWindow(TaskContext.Instance().GameHandle);
    }

    public static bool IsFullScreenMode(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        var exStyle = User32.GetWindowLong(hWnd, User32.WindowLongFlags.GWL_EXSTYLE);

        return (exStyle & (int)User32.WindowStylesEx.WS_EX_TOPMOST) != 0;
    }

}