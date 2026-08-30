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
    private static readonly ILogger<SystemControl> Logger = App.GetLogger<SystemControl>();

    /// <summary>
    ///     显示设置恢复监听是否在运行（防止重复启动游戏时生成多个监听争用同一份快照）
    /// </summary>
    private static int DisplayModeRestoreMonitorActive;

    private const string ChildSessionGenshinStartArgs =
        "-popupwindow -screen-width 1920 -screen-height 1080";

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

        // 原神 7.0+ 的显示设置以注册表为准（启动参数无法设置窗口模式），启动游戏前按需写入注册表
        TaskCompletionSource<bool>? launchOutcome = null;
        if (cfg.AutoSetWindowedModeEnabled)
        {
            GenshinDisplayRegistryHelper.CaptureAndSetWindowed();
            // 写注册表后立即启动恢复监听，启动结果由下方查找游戏窗口的循环通知：
            // 找到窗口 → 等游戏退出后恢复；查找超时 → 立即恢复（或等待慢启动的进程退出后再恢复）
            launchOutcome = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            StartDisplayModeRestoreMonitor(launchOutcome);
        }

        try
        {
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
                    // 游戏窗口已找到，通知监听等待游戏退出后恢复显示设置
                    launchOutcome?.TrySetResult(true);
                    await Task.Delay(2333);
                    handle = FindGenshinImpactHandle();
                    await Task.Delay(2577);
                    return handle;
                }

                await Task.Delay(5577);
            }

            launchOutcome?.TrySetResult(false);
            return FindGenshinImpactHandle();
        }
        catch
        {
            // 启动过程异常（如 Process.Start 失败）时同样通知监听立即恢复，避免快照悬挂
            launchOutcome?.TrySetResult(false);
            throw;
        }
    }

    /// <summary>
    ///     存在尚未恢复的原神显示设置快照时恢复：游戏未运行则立即恢复，运行中则等游戏退出后恢复。
    ///     用于 BetterGI 启动时的兜底（上次进程意外退出可能遗留快照）。
    /// </summary>
    internal static void RestoreDisplaySettingsIfPending()
    {
        if (!GenshinDisplayRegistryHelper.HasPendingDisplaySettings())
        {
            return;
        }

        StartDisplayModeRestoreMonitor(null);
    }

    /// <summary>
    ///     游戏退出后恢复启动前的显示设置：原神退出时会把显示设置写回注册表，
    ///     必须等游戏进程完全退出后再恢复，否则恢复结果会被游戏退出时的写回覆盖。
    /// </summary>
    internal static void StartDisplayModeRestoreMonitor(TaskCompletionSource<bool>? launchOutcome)
    {
        // 已有恢复监听在运行时不再新建，避免多个监听争用同一份快照
        if (Interlocked.Exchange(ref DisplayModeRestoreMonitorActive, 1) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var processNames = TaskContext.Instance().GetGenshinGameProcessNameList();

                // 联动启动时等待启动结果（找到窗口或查找超时），
                // 避免在游戏启动过程中误判为“已退出”而过早恢复注册表（恢复后游戏启动时会读到已恢复的设置）。
                // 兜底场景（BetterGI 启动时发现遗留快照）没有启动流程，直接按当前游戏进程状态处理。
                if (launchOutcome != null)
                {
                    await launchOutcome.Task;
                }

                // 等游戏进程退出（联动启动失败但进程仍存在即慢启动场景，同样需要等其退出后再恢复）
                while (IsGenshinRunning(processNames))
                {
                    await Task.Delay(3000);
                }

                // 游戏进程退出时写回注册表可能略有延迟，稍候片刻避免与恢复竞争
                await Task.Delay(3000);

                if (GenshinDisplayRegistryHelper.RestorePreviousDisplaySettings(out var restoredSettings))
                {
                    var settings = restoredSettings[0];
                    Logger.LogInformation(
                        "检测到原神已退出，已将显示设置恢复为启动前的 {Width}x{Height}",
                        settings.Width, settings.Height);
                }
            }
            catch (Exception e)
            {
                Logger.LogWarning(e, "恢复原神显示设置失败");
            }
            finally
            {
                Interlocked.Exchange(ref DisplayModeRestoreMonitorActive, 0);
            }
        });
    }

    private static bool IsGenshinRunning(IEnumerable<string> processNames)
    {
        return processNames.Any(name => Process.GetProcessesByName(name).Length > 0);
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
