using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace MicaSetup.Helper;

public static class RuntimeHelper
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
        if (IsDebuggerAttached)
        {
            Logger.Warn("[RuntimeHelper] IsDebuggerAttached causes skip EnsureElevated");
            return;
        }
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
            FluentProcess.Create()
                .FileName(fileName ?? Path.Combine(dir ?? AppDomain.CurrentDomain.BaseDirectory, AppDomain.CurrentDomain.FriendlyName))
                .Arguments(args ?? ReArguments())
                .WorkingDirectory(dir ?? Environment.CurrentDirectory)
                .UseShellExecute()
                .Verb("runas")
                .Start()
                .Forget();
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
        _ = Task.Run(() =>
        {
            while (handle.WaitOne())
            {
                UIDispatcherHelper.BeginInvoke(main =>
                {
                    main?.Activate();
                    main?.Show();
                });
            }
        });
    }
}
