using BetterGenshinImpact.Core.Config;
using Microsoft.Extensions.Hosting;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace BetterGenshinImpact.Helpers;

internal static class RuntimeHelper
{
    public static bool IsElevated { get; } = GetElevated();
    public static bool IsDebuggerAttached => Debugger.IsAttached;
    public static bool IsDesignMode { get; } = GetDesignMode();

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
                MessageBox.Show("以管理员权限启动 BetterGI 失败，非管理员权限下所有模拟操作功能均不可用！\r\n请尝试 右键 —— 以管理员身份运行 的方式启动 BetterGI", 
                    "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        GC.KeepAlive(handle);
        GC.KeepAlive(Task.Factory.StartNew(() =>
        {
            while (handle.WaitOne())
            {
#if false
                UIDispatcherHelper.BeginInvoke(main =>
                {
                    main?.Activate();
                    main?.Show();
                });
#endif
            }
        }, TaskCreationOptions.LongRunning));
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
        RuntimeHelper.CheckSingleInstance(instanceName, callback);
        return self;
    }
}
