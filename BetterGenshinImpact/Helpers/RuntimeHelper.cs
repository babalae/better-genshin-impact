using BetterGenshinImpact.Helpers;
﻿using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.View.Windows;
using Microsoft.Extensions.Hosting;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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

                    ThemedMessageBox.Error(Lang.S["Gen_11913_f6bfc6"]);
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

    public static void CheckSingleInstance(string instanceName, Action<bool> callback = null!)
    {
        EventWaitHandle? handle;

        try
        {
            handle = EventWaitHandle.OpenExisting(instanceName);
            handle.Set();
            callback?.Invoke(false);
            Environment.Exit(0xFFFF);
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            callback?.Invoke(true);
            handle = new EventWaitHandle(false, EventResetMode.AutoReset, instanceName);
        }

        _ = Task.Factory.StartNew(() =>
        {
            while (handle.WaitOne())
            {
                Application.Current.Dispatcher?.BeginInvoke(() =>
                {
                    Application.Current.MainWindow?.Show();
                    Application.Current.MainWindow?.Activate();
                    SystemControl.RestoreWindow(new WindowInteropHelper(Application.Current.MainWindow).Handle);
                });
            }
        }, TaskCreationOptions.LongRunning).ConfigureAwait(false);
    }

    public static void CheckIntegration()
    {
        if (!Directory.Exists(Global.Absolute("Assets")) || !Directory.Exists(Global.Absolute("GameTask")))
        {
            StringBuilder stringBuilder = new(Lang.S["Gen_11912_4686b5"]);
            stringBuilder.Append(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) == Global.StartUpPath
                ? Lang.S["Gen_11911_3889b3"]
                : Lang.S["Gen_11910_5b78e4"]);

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

    public static IHostBuilder UseSingleInstance(this IHostBuilder self, string instanceName, Action<bool> callback = null!)
    {
        if (!Environment.GetCommandLineArgs().Contains("--no-single"))
        {
            RuntimeHelper.CheckSingleInstance(instanceName, callback);
        }
        return self;
    }

    public static IHostBuilder CheckIntegration(this IHostBuilder app)
    {
        RuntimeHelper.CheckIntegration();
        return app;
    }
}
