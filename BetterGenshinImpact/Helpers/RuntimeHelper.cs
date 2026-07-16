using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.View.Windows;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace BetterGenshinImpact.Helpers;

internal static class RuntimeHelper
{
    // Keep the named handle alive for the lifetime of the primary process.
    private static EventWaitHandle? _singleInstanceHandle;

    public static bool IsElevated { get; } = GetElevated();
    public static bool IsDebuggerAttached => Debugger.IsAttached;
    public static bool IsDesignMode { get; } = GetDesignMode();

    public static bool IsDebug =>
#if DEBUG
        true;

#else
        false;
#endif

    private static bool GetElevated()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool GetDesignMode()
    {
        if (LicenseManager.UsageMode == LicenseUsageMode.Designtime)
        {
            return true;
        }
        else if (Process.GetCurrentProcess().ProcessName == "devenv")
        {
            return true;
        }
        return false;
    }

    public static void EnsureElevated()
    {
        if (!IsElevated)
        {
            RestartAsElevated();
        }
    }

    public static string ReArguments()
    {
        string[] args = Environment.GetCommandLineArgs().Skip(1).ToArray();

        for (int i = default; i < args.Length; i++)
        {
            args[i] = $@"""{args[i]}""";
        }
        return string.Join(" ", args);
    }

    public static void RestartAsElevated(string fileName = null!, string dir = null!, string args = null!, int? exitCode = null, bool forced = false)
    {
        try
        {
            ProcessStartInfo startInfo = new()
            {
                UseShellExecute = true,
                WorkingDirectory = dir ?? Global.StartUpPath,
                FileName = fileName ?? "BetterGI.exe",
                Arguments = args ?? ReArguments(),
                Verb = "runas"
            };
            try
            {
                _ = Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                // 延迟显示错误对话框，等待 Config 初始化完成
                Application.Current?.Dispatcher.InvokeAsync(async () =>
                {
                    // 轮询等待 Config 初始化完成（最多等待 3 秒）
                    var timeout = TimeSpan.FromSeconds(3);
                    var startTime = DateTime.Now;
                    while (ConfigService.Config == null && DateTime.Now - startTime < timeout)
                    {
                        await Task.Delay(50);
                    }

                    ThemedMessageBox.Error("以管理员权限启动 BetterGI 失败，非管理员权限下所有模拟操作功能均不可用！\r\n请尝试 右键 —— 以管理员身份运行 的方式启动 BetterGI");
                }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                return;
            }
        }
        catch (Win32Exception)
        {
            return;
        }
        if (forced)
        {
            Process.GetCurrentProcess().Kill();
        }
        Environment.Exit(exitCode ?? 'r' + 'u' + 'n' + 'a' + 's');
    }

    public static void CheckSingleInstance(string instanceName, Action<bool> callback = null!, Action<string[]> activationCallback = null!)
    {
        var pipeName = $"{instanceName}.Activation";

        try
        {
            using EventWaitHandle handle = EventWaitHandle.OpenExisting(instanceName);
            try
            {
                using NamedPipeClientStream pipe = new(".", pipeName, PipeDirection.Out);
                pipe.Connect(3000);
                using BinaryWriter writer = new(pipe);
                writer.Write(JsonConvert.SerializeObject(Environment.GetCommandLineArgs()));
                writer.Flush();
            }
            catch (Exception ex) when (ex is IOException or TimeoutException)
            {
                Debug.WriteLine(ex);
            }
            callback?.Invoke(false);
            Environment.Exit(0xFFFF);
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            callback?.Invoke(true);
            _singleInstanceHandle = new EventWaitHandle(false, EventResetMode.AutoReset, instanceName);
        }

        _ = Task.Factory.StartNew(() =>
        {
            while (true)
            {
                try
                {
                    using NamedPipeServerStream pipe = new(pipeName, PipeDirection.In);
                    pipe.WaitForConnection();
                    using BinaryReader reader = new(pipe);
                    var args = JsonConvert.DeserializeObject<string[]>(reader.ReadString()) ?? [];

                    Application.Current.Dispatcher?.BeginInvoke(() =>
                    {
                        Application.Current.MainWindow?.Show();
                        Application.Current.MainWindow?.Activate();
                        SystemControl.RestoreWindow(new WindowInteropHelper(Application.Current.MainWindow).Handle);
                        activationCallback?.Invoke(args);
                    });
                }
                catch (Exception ex) when (ex is IOException or JsonException)
                {
                    // Ignore an interrupted activation and continue listening.
                }
            }
        }, TaskCreationOptions.LongRunning).ConfigureAwait(false);
    }

    public static void CheckIntegration()
    {
        if (!Directory.Exists(Global.Absolute("Assets")) || !Directory.Exists(Global.Absolute("GameTask")))
        {
            StringBuilder stringBuilder = new("发现有关键文件缺失，");
            stringBuilder.Append(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) == Global.StartUpPath
                ? "请不要把主程序exe文件剪切到桌面。正确的做法：请右键点击主程序，在弹出的菜单中选择“发送到”选项，然后选择“桌面创建快捷方式”。"
                : "请重新安装软件");

            ThemedMessageBox.Warning(stringBuilder.ToString());
            Environment.Exit(0xFFFF);
        }
    }
}

internal static class RuntimeExtension
{
    public static IHostBuilder UseElevated(this IHostBuilder app)
    {
        RuntimeHelper.EnsureElevated();
        return app;
    }

    public static IHostBuilder UseSingleInstance(this IHostBuilder self, string instanceName, Action<bool> callback = null!, Action<string[]> activationCallback = null!)
    {
        if (!Environment.GetCommandLineArgs().Contains("--no-single"))
        {
            RuntimeHelper.CheckSingleInstance(instanceName, callback, activationCallback);
        }
        return self;
    }

    public static IHostBuilder CheckIntegration(this IHostBuilder app)
    {
        RuntimeHelper.CheckIntegration();
        return app;
    }
}
