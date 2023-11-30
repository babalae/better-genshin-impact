using BetterGenshinImpact.Core.Simulator;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask;

public class SystemControl
{
    public static nint FindGenshinImpactHandle()
    {
        return FindHandleByProcessName("YuanShen", "GenshinImpact", "Genshin Impact Cloud Game");
    }

    public static async Task<nint> StartFromLocalAsync(string path)
    {
        if (path.EndsWith("YuanShen.exe"))
        {
            DirectStartFromYuanShen(path);
            await Task.Delay(10000);
        }
        if (path.EndsWith("launcher.exe"))
        {
            StartFromLauncher(path);
            await Task.Delay(10000);
        }
        if (path.EndsWith("Genshin Impact Cloud Game.exe"))
        {
            StartCloudYaunShen(path);
        }
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

    public static nint GetForegroundWindowHandle()
    {
        return (nint)User32.GetForegroundWindow();
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

    public static Process? GetProcessByHandle(nint hWnd)
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
    public static RECT GetWindowRect(nint hWnd)
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
    public static RECT GetGameScreenRect(nint hWnd)
    {
        User32.GetClientRect(hWnd, out var clientRect);
        return clientRect;
    }

    /// <summary>
    /// GetWindowRect or GetGameScreenRect
    /// </summary>
    /// <param name="hWnd"></param>
    /// <returns></returns>
    public static RECT GetCaptureRect(nint hWnd)
    {
        var windowRect = GetWindowRect(hWnd);
        var gameScreenRect = GetGameScreenRect(hWnd);
        var left = windowRect.Left;
        var top = windowRect.Top + windowRect.Height - gameScreenRect.Height;
        var right = left + gameScreenRect.Width;
        var bottom = top + gameScreenRect.Height;
        return new RECT(left, top, right, bottom);
    }

    public static void ActivateWindow(nint hWnd)
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

    public static void Focus(nint hWnd)
    {
        if (User32.IsWindow(hWnd))
        {
            _ = User32.SendMessage(hWnd, User32.WindowMessage.WM_SYSCOMMAND, User32.SysCommand.SC_RESTORE, 0);
            _ = User32.SetForegroundWindow(hWnd);
            while (User32.IsIconic(hWnd))
            {
                continue;
            }
            _ = User32.BringWindowToTop(hWnd);
        }
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

    private static void DirectStartFromYuanShen(string path)
    {
        // 直接exe启动
        var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private static void StartFromLauncher(string path)
    {
        // 通过launcher启动
        var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        Thread.Sleep(1000);
        // 获取launcher窗口句柄
        var hWnd = FindHandleByProcessName("launcher");
        var rect = GetWindowRect(hWnd);
        var dpiScale = Helpers.DpiHelper.ScaleY;
        // 对于launcher，启动按钮的位置时固定的，在launcher窗口的右下角
        Thread.Sleep(1000);
        Simulation.MouseEvent.Click((int)((float)rect.right * dpiScale) - (rect.Width / 5), (int)((float)rect.bottom * dpiScale) - (rect.Height / 8));
    }

    private static void StartCloudYaunShen(string path)
    {
        // 通过launcher启动
        var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        Thread.Sleep(10000);
        // 获取launcher窗口句柄
        var hWnd = FindHandleByProcessName("Genshin Impact Cloud Game");
        var rect = GetWindowRect(hWnd);
        var dpiScale = Helpers.DpiHelper.ScaleY;
        // 对于launcher，启动按钮的位置时固定的，在launcher窗口的右下角
        Simulation.MouseEvent.Click(rect.right - (rect.Width / 6), rect.bottom - (rect.Height / 13 * 3));
        // TODO：点完之后有个15s的倒计时，好像不处理也没什么问题，直接睡个20s吧
        Thread.Sleep(20000);
    }
}
