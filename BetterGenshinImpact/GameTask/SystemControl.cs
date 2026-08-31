using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Instance;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask;

public class SystemControl
{
    private static readonly ILogger Logger = App.GetLogger<SystemControl>();

    private const string ChildSessionGenshinStartArgs =
        "-popupwindow -screen-width 1920 -screen-height 1080";

    private static readonly Regex ChildSessionOverriddenArgumentRegex = new(
        @"(?<!\S)(?:-popupwindow|-screen-(?:width|height)(?:\s*=\s*(?:""[^""]*""|\S+)|\s+(?:""[^""]*""|(?!-)\S+))?)(?=\s|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static nint FindGenshinImpactHandle()
    {
        var processNames = TaskContext.Instance().GetGenshinGameProcessNameList();

        // 其他设置：窗口类名优先检测（默认关闭，关闭时走原始进程名+MainWindowHandle 路径）
        // 开启后：①按窗口类名枚举 → ②按进程枚举最大可见窗口 → ③原版方式（均未命中才回退）
        if (TaskContext.Instance().Config.OtherConfig.WindowClassDetectPreferred)
        {
            // ①优先：按窗口类名 EnumWindows 枚举（不看标题，规避标题变化），同会话优先
            var handle = FindWindowByUnityWndClass(processNames);
            if (handle != 0)
            {
                Logger.LogInformation("[窗口检测] ①按窗口类名枚举命中，句柄={Handle}", handle);
                return handle;
            }

            // ②次选：白名单进程拥有的可见窗口中客户区最大者，同会话优先
            handle = FindLargestVisibleWindowByProcessName(processNames);
            if (handle != 0)
            {
                Logger.LogInformation("[窗口检测] ②按进程枚举最大可见窗口命中，句柄={Handle}", handle);
                return handle;
            }

            // 仍未命中，回退原版逻辑（进程名 + MainWindowHandle）
            handle = FindHandleByProcessName(processNames.ToArray());
            Logger.LogInformation("[窗口检测] 未命中，回退旧逻辑，句柄={Handle}", handle);
            return handle;
        }

        return FindHandleByProcessName(processNames.ToArray());
    }

    /// <summary>
    /// ①优先：按窗口类名 EnumWindows 枚举顶层可见窗口（不看窗口标题，规避标题变化），
    /// 再经 GetWindowThreadProcessId 反查进程名，须在游戏进程名白名单内；
    /// 同会话命中直接返回，否则记住第一个跨会话命中继续枚举（多开/多会话时同会话优先）
    /// </summary>
    private static nint FindWindowByUnityWndClass(IEnumerable<string> processNames)
    {
        var nameSet = new HashSet<string>(processNames, StringComparer.OrdinalIgnoreCase);
        var currentSessionId = Process.GetCurrentProcess().SessionId;
        nint found = 0;
        _ = User32.EnumWindows((hWnd, lParam) =>
        {
            if (!User32.IsWindowVisible(hWnd))
            {
                return true;
            }

            var className = GetWindowClassName((nint)hWnd);
            if (!string.Equals(className, "UnityWndClass", StringComparison.OrdinalIgnoreCase)
                && !(className?.StartsWith("Qt", StringComparison.OrdinalIgnoreCase) == true
                     && className.EndsWith("QWindowIcon", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            _ = User32.GetWindowThreadProcessId(hWnd, out var pid);
            try
            {
                using var p = Process.GetProcessById((int)pid);
                if (!nameSet.Contains(p.ProcessName))
                {
                    return true;
                }

                if (p.SessionId == currentSessionId)
                {
                    // 同会话命中，直接采用
                    found = (nint)hWnd;
                    return false;
                }
            }
            catch (ArgumentException)
            {
                // pid 已失效（进程已退出），跳过
                return true;
            }
            catch (Exception ex)
            {
                // ArgumentException 已单独接走，落到这里的多为跨会话/提权进程访问被拒
                Logger.LogDebug(ex, "[窗口检测] ①读取窗口进程信息失败（pid={Pid}）", (int)pid);
                return true;
            }

            // 跨会话命中：先记住，继续枚举看有没有同会话的
            if (found == 0)
            {
                found = (nint)hWnd;
            }

            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>
    /// ②次选：对游戏进程名白名单内的所有进程（不依赖 MainWindowHandle）
    /// EnumWindows 枚举其名下的可见顶层窗口，取客户区面积最大者；
    /// 存在同会话进程时只在本会话窗口里选（多开/多会话时同会话优先）
    /// </summary>
    private static nint FindLargestVisibleWindowByProcessName(IEnumerable<string> processNames)
    {
        var currentSessionId = Process.GetCurrentProcess().SessionId;
        var pidSet = new HashSet<int>();
        var sameSessionPids = new HashSet<int>();
        foreach (var name in processNames)
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    pidSet.Add(p.Id);
                    if (p.SessionId == currentSessionId)
                    {
                        sameSessionPids.Add(p.Id);
                    }
                }
                catch (InvalidOperationException)
                {
                    // 进程已退出，跳过
                }
                finally
                {
                    p.Dispose();
                }
            }
        }

        if (pidSet.Count == 0)
        {
            return 0;
        }

        var effectivePids = sameSessionPids.Count > 0 ? sameSessionPids : pidSet;

        nint best = 0;
        long bestArea = -1;
        _ = User32.EnumWindows((hWnd, lParam) =>
        {
            if (!User32.IsWindowVisible(hWnd))
            {
                return true;
            }

            _ = User32.GetWindowThreadProcessId(hWnd, out var pid);
            if (!effectivePids.Contains((int)pid))
            {
                return true;
            }

            User32.GetClientRect(hWnd, out var rect);
            var area = (long)rect.Right * rect.Bottom;
            if (area > bestArea)
            {
                bestArea = area;
                best = (nint)hWnd;
            }

            return true;
        }, IntPtr.Zero);
        return best;
    }

    private static string? GetWindowClassName(nint hWnd)
    {
        var sb = new StringBuilder(256);
        _ = User32.GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static async Task<nint> StartFromLocalAsync(string path)
    {
        if (!File.Exists(path))
        {
            await ThemedMessageBox.ErrorAsync($"原神启动路径 {path} 不存在，请前往 启动——同时启动原神——原神安装路径 重新进行配置！");
            return IntPtr.Zero;
        }

        var cfg = TaskContext.Instance().Config.GenshinStartConfig;
        var workdir = Path.GetDirectoryName(path) ?? "";
        var arg = BuildGenshinStartArguments(
            cfg.GenshinStartArgs,
            InstanceBootstrap.Current.Context.InstanceType == BetterGiInstanceType.ChildSession);

        if (cfg.StartGameWithCmd)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c start \"\" /d \"{workdir}\" \"{path}\" {arg}",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi);
        }
        else
        {
            Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                Arguments = arg,
                WorkingDirectory = workdir
            });
        }

        for (var i = 0; i < 5; i++)
        {
            var handle = FindGenshinImpactHandle();
            if (handle != 0)
            {
                await Task.Delay(2333);
                handle = FindGenshinImpactHandle();
                await Task.Delay(2577);
                return handle;
            }

            await Task.Delay(5577);
        }

        return FindGenshinImpactHandle();
    }

    internal static string BuildGenshinStartArguments(string? configuredArguments, bool isChildSession)
    {
        var arguments = configuredArguments?.Trim() ?? string.Empty;
        if (!isChildSession)
        {
            return arguments;
        }

        arguments = ChildSessionOverriddenArgumentRegex.Replace(arguments, string.Empty).Trim();
        return string.IsNullOrEmpty(arguments)
            ? ChildSessionGenshinStartArgs
            : $"{arguments} {ChildSessionGenshinStartArgs}";
    }

    public static bool IsGenshinImpactActiveByProcess()
    {
        var name = GetActiveProcessName();
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        var processNames = TaskContext.Instance().GetGenshinGameProcessNameList();
        return processNames.Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
    }
    
    public static string GetActiveByProcess()
    {
        return GetActiveProcessName() ?? "Unknown";
    }

    public static bool IsGenshinImpactActive()
    {
        var hWnd = User32.GetForegroundWindow();
        return hWnd == TaskContext.Instance().GameHandle;
    }

    public static bool IsGenshinImpactMinimized()
    {
        return User32.IsIconic(TaskContext.Instance().GameHandle);
    }

    public static nint GetForegroundWindowHandle()
    {
        return (nint)User32.GetForegroundWindow();
    }

    public static nint FindHandleByProcessName(params string[] names)
    {
        var currentSessionId = Process.GetCurrentProcess().SessionId;
        foreach (var name in names)
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    if (p.SessionId == currentSessionId)
                        return p.MainWindowHandle;
                }
                catch (InvalidOperationException)
                {
                    // 进程已退出，跳过
                }
                finally
                {
                    p.Dispose();
                }
            }
        }

        return 0;
    }

    public static nint FindHandleByWindowName()
    {
        var handle = (nint)User32.FindWindow("UnityWndClass", "原神");
        if (handle != 0)
        {
            return handle;
        }

        handle = (nint)User32.FindWindow("UnityWndClass", "Genshin Impact");
        if (handle != 0)
        {
            return handle;
        }

        handle = (nint)User32.FindWindow("Qt5152QWindowIcon", "云·原神");
        if (handle != 0)
        {
            return handle;
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
    public static void RestartApplication(string[] newArgs)
    {
        // 获取当前程序路径
        string exePath = Process.GetCurrentProcess().MainModule.FileName;

        // 构建参数字符串
        var restartArgs = new List<string>(newArgs);
        var instanceType = InstanceBootstrap.Current.Context.InstanceType;
        if (instanceType == BetterGiInstanceType.ChildSession)
        {
            restartArgs.Add(CommandLineOptions.InstanceArgument);
            restartArgs.Add("childSession");
        }
        else if (instanceType == BetterGiInstanceType.WebView)
        {
            restartArgs.Add(CommandLineOptions.InstanceArgument);
            restartArgs.Add("webview");
        }
        restartArgs.Add(CommandLineOptions.RestartFromProcessIdArgument);
        restartArgs.Add(Environment.ProcessId.ToString());

        // 启动新进程
        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false
        };
        foreach (var argument in restartArgs)
        {
            startInfo.ArgumentList.Add(argument);
        }
        Process.Start(startInfo);

        // 关闭当前程序
        Environment.Exit(0);
    }
    public static void FocusWindow(nint hWnd)
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
            _ = User32.SetActiveWindow(hWnd);
        }
    }
    public static void MinimizeAndActivateWindow(nint hWnd)
    {
        HWND hShell = User32.FindWindow("Shell_TrayWnd", null);
        User32.SendMessage(hShell, 0x0111, (IntPtr)419, IntPtr.Zero);
        Thread.Sleep(500);
        FocusWindow(hWnd);
    }
    public static void RestoreWindow(nint hWnd)
    {
        if (User32.IsWindow(hWnd))
        {
            _ = User32.SendMessage(hWnd, User32.WindowMessage.WM_SYSCOMMAND, User32.SysCommand.SC_RESTORE, 0);
            _ = User32.SetForegroundWindow(hWnd);

            if (User32.IsIconic(hWnd))
            {
                _ = User32.ShowWindow(hWnd, ShowWindowCommand.SW_RESTORE);
            }

            _ = User32.BringWindowToTop(hWnd);
            _ = User32.SetActiveWindow(hWnd);
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

    // private static void StartFromLauncher(string path)
    // {
    //     // 通过launcher启动
    //     var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    //     Thread.Sleep(1000);
    //     // 获取launcher窗口句柄
    //     var hWnd = FindHandleByProcessName("launcher");
    //     var rect = GetWindowRect(hWnd);
    //     var dpiScale = Helpers.DpiHelper.ScaleY;
    //     // 对于launcher，启动按钮的位置时固定的，在launcher窗口的右下角
    //     Thread.Sleep(1000);
    //     Simulation.MouseEvent.Click((int)((float)rect.right * dpiScale) - (rect.Width / 5), (int)((float)rect.bottom * dpiScale) - (rect.Height / 8));
    // }
    //
    // private static void StartCloudYaunShen(string path)
    // {
    //     // 通过launcher启动
    //     var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    //     Thread.Sleep(10000);
    //     // 获取launcher窗口句柄
    //     var hWnd = FindHandleByProcessName("Genshin Impact Cloud Game");
    //     var rect = GetWindowRect(hWnd);
    //     var dpiScale = Helpers.DpiHelper.ScaleY;
    //     // 对于launcher，启动按钮的位置时固定的，在launcher窗口的右下角
    //     Simulation.MouseEvent.Click(rect.right - (rect.Width / 6), rect.bottom - (rect.Height / 13 * 3));
    //     // TODO：点完之后有个15s的倒计时，好像不处理也没什么问题，直接睡个20s吧
    //     Thread.Sleep(20000);
    // }
    public static void CloseGame()
    {
        try
        {
            var currentSessionId = Process.GetCurrentProcess().SessionId;
            var processNames = TaskContext.Instance().GetGenshinGameProcessNameList();
            var processes = new List<Process>();
            foreach (var name in processNames)
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try
                    {
                        if (p.SessionId == currentSessionId)
                            processes.Add(p);
                        else
                            p.Dispose();
                    }
                    catch (InvalidOperationException)
                    {
                        p.Dispose();
                    }
                }
            }
            var targets = processes.GroupBy(p => p.Id).Select(g => g.First()).ToArray();

            if (targets.Length > 0)
            {
                foreach (var process in targets)
                {
                    try
                    {
                        // 尝试正常关闭进程
                        process.CloseMainWindow();
                        
                        // 给进程一些时间来响应关闭请求
                        if (!process.WaitForExit(5000))
                        {
                            // 如果进程没有在5秒内关闭，则强制终止它
                            process.Kill();
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"关闭游戏进程时出错: {ex.Message}");
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CloseGame方法执行出错: {ex.Message}");
        }
    }

    public static void Shutdown()
    {
        try
        {
            // 使用Windows API安全关闭系统
            // 这里使用的是标准的Windows关机命令，需要适当的权限
            Process.Start("shutdown", "/s /t 60 /c \"系统将在60秒后关闭，请保存您的工作。\"");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Shutdown方法执行出错: {ex.Message}");
        }
    }
}
