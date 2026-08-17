using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Instance;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask;

public class SystemControl
{
    private const string ChildSessionGenshinStartArgs =
        "-popupwindow -screen-width 1920 -screen-height 1080";
    private static readonly TimeSpan GenshinWindowWaitTimeout = TimeSpan.FromSeconds(75);
    private static readonly ILogger<SystemControl> Logger = App.GetLogger<SystemControl>();

    private static readonly Regex ChildSessionOverriddenArgumentRegex = new(
        @"(?<!\S)(?:-popupwindow|-screen-(?:width|height)(?:\s*=\s*(?:""[^""]*""|\S+)|\s+(?:""[^""]*""|(?!-)\S+))?)(?=\s|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static nint FindGenshinImpactHandle()
    {
        var processNames = TaskContext.Instance().GetGenshinGameProcessNameList();
        return FindHandleByProcessName(processNames.ToArray());
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

        StartLocalGame(path, workdir, arg, cfg.StartGameWithCmd);
        var handle = await WaitForGenshinWindowAsync(GenshinWindowWaitTimeout);
        if (handle != 0)
        {
            return handle;
        }

        if (!IsGenshinImpactRunningInCurrentSession())
        {
            Logger.LogWarning("关联启动后 75 秒内未发现原神进程，自动重试启动一次");
            StartLocalGame(path, workdir, arg, cfg.StartGameWithCmd);
        }
        else
        {
            Logger.LogWarning("原神进程已经出现但窗口尚未就绪，继续等待窗口");
        }

        handle = await WaitForGenshinWindowAsync(GenshinWindowWaitTimeout);
        if (handle == 0)
        {
            Logger.LogError("关联启动原神失败：150 秒内未发现当前会话中的游戏窗口");
        }

        return handle;
    }

    internal static ProcessStartInfo BuildLocalGameStartInfo(
        string path,
        string workdir,
        string arg,
        bool startGameWithCmd)
    {
        if (!startGameWithCmd)
        {
            return new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                Arguments = arg,
                WorkingDirectory = workdir
            };
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = workdir
        };

        var extension = Path.GetExtension(path);
        string command;
        if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
        {
            command = string.IsNullOrWhiteSpace(arg)
                ? $"\"{path}\""
                : $"\"{path}\" {arg}";
        }
        else
        {
            command = $"start \"\" /d \"{workdir}\" \"{path}\" {arg}".TrimEnd();
        }

        // cmd.exe does not understand the backslash-escaped quotes generated when a
        // complete command is placed in ProcessStartInfo.ArgumentList. Supplying the
        // conventional /s /c "<command>" string keeps quoted paths with spaces intact.
        startInfo.Arguments = $"/d /s /c \"{command}\"";
        return startInfo;
    }

    private static void StartLocalGame(
        string path,
        string workdir,
        string arg,
        bool startGameWithCmd)
    {
        var startInfo = BuildLocalGameStartInfo(path, workdir, arg, startGameWithCmd);
        Logger.LogInformation(
            "启动原神关联命令：Path={Path}, WorkingDirectory={WorkingDirectory}, StartGameWithCmd={StartGameWithCmd}, Arguments={Arguments}, Session={SessionId}",
            path,
            workdir,
            startGameWithCmd,
            arg,
            Process.GetCurrentProcess().SessionId);

        var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException($"无法创建原神启动进程：{path}");
        }

        Logger.LogInformation(
            "原神关联启动进程已创建：PID={ProcessId}, ProcessName={ProcessName}, Session={SessionId}",
            process.Id,
            process.ProcessName,
            process.SessionId);

        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            try
            {
                Logger.LogInformation(
                    "原神关联启动进程已退出：PID={ProcessId}, ExitCode={ExitCode}",
                    process.Id,
                    process.ExitCode);
            }
            catch (InvalidOperationException)
            {
                // The process object may no longer expose its exit code during shutdown.
            }
            finally
            {
                process.Dispose();
            }
        };
    }

    private static async Task<nint> WaitForGenshinWindowAsync(TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            var handle = FindGenshinImpactHandle();
            if (handle != 0)
            {
                await Task.Delay(2500);
                return FindGenshinImpactHandle();
            }

            await Task.Delay(2000);
        }

        return FindGenshinImpactHandle();
    }

    private static bool IsGenshinImpactRunningInCurrentSession()
    {
        var currentSessionId = Process.GetCurrentProcess().SessionId;
        foreach (var processName in TaskContext.Instance().GetGenshinGameProcessNameList())
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        if (process.SessionId == currentSessionId && !process.HasExited)
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        // Process exited while it was being inspected.
                    }
                }
            }
        }

        return false;
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
