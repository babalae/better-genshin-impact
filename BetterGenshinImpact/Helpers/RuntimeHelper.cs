using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.View.Windows;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
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
    private static readonly object SingleInstanceActivationLock = new();
    private static readonly Queue<string[]> PendingSingleInstanceActivationArgs = new();
    private static EventHandler<SingleInstanceActivatedEventArgs>? _singleInstanceActivated;

    public static event EventHandler<SingleInstanceActivatedEventArgs>? SingleInstanceActivated
    {
        add
        {
            List<string[]> pendingArgs = [];

            lock (SingleInstanceActivationLock)
            {
                _singleInstanceActivated += value;

                while (PendingSingleInstanceActivationArgs.Count > 0)
                {
                    pendingArgs.Add(PendingSingleInstanceActivationArgs.Dequeue());
                }
            }

            foreach (string[] args in pendingArgs)
            {
                value?.Invoke(null, new SingleInstanceActivatedEventArgs(args));
            }
        }
        remove
        {
            lock (SingleInstanceActivationLock)
            {
                _singleInstanceActivated -= value;
            }
        }
    }

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

    public static void CheckSingleInstance(string instanceName, Action<bool> callback = null!)
    {
        EventWaitHandle? handle;

        try
        {
            handle = EventWaitHandle.OpenExisting(instanceName);
            string[] activationArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
            if (!TrySendSingleInstanceActivationArgsWithRetry(instanceName, activationArgs))
            {
                Debug.WriteLine("Failed to forward single instance activation args.");
            }

            handle.Set();
            callback?.Invoke(false);
            Environment.Exit(0xFFFF);
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            callback?.Invoke(true);
            handle = new EventWaitHandle(false, EventResetMode.AutoReset, instanceName);
            StartSingleInstanceActivationPipe(instanceName);
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

    private static string GetSingleInstanceActivationPipeName(string instanceName)
    {
        return $"{instanceName}.Activation";
    }

    private static bool TrySendSingleInstanceActivationArgsWithRetry(string instanceName, string[] args)
    {
        const int maxAttempts = 10;
        const int retryDelayMilliseconds = 100;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (TrySendSingleInstanceActivationArgs(instanceName, args))
            {
                return true;
            }

            if (attempt < maxAttempts)
            {
                Thread.Sleep(retryDelayMilliseconds);
            }
        }

        return false;
    }

    private static bool TrySendSingleInstanceActivationArgs(string instanceName, string[] args)
    {
        try
        {
            using NamedPipeClientStream pipeClient = new(".", GetSingleInstanceActivationPipeName(instanceName), PipeDirection.Out);
            pipeClient.Connect(300);

            using StreamWriter writer = new(pipeClient, new UTF8Encoding(false))
            {
                AutoFlush = true
            };
            writer.Write(JsonSerializer.Serialize(args));
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return false;
        }
    }

    private static void StartSingleInstanceActivationPipe(string instanceName)
    {
        string pipeName = GetSingleInstanceActivationPipeName(instanceName);
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    using NamedPipeServerStream pipeServer = new(
                        pipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await pipeServer.WaitForConnectionAsync().ConfigureAwait(false);

                    using StreamReader reader = new(pipeServer, Encoding.UTF8);
                    string payload = await reader.ReadToEndAsync().ConfigureAwait(false);
                    string[] args = JsonSerializer.Deserialize<string[]>(payload) ?? [];
                    PublishSingleInstanceActivation(args);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }
        });
    }

    private static void PublishSingleInstanceActivation(string[] args)
    {
        EventHandler<SingleInstanceActivatedEventArgs>? handler;

        lock (SingleInstanceActivationLock)
        {
            handler = _singleInstanceActivated;

            if (handler == null)
            {
                PendingSingleInstanceActivationArgs.Enqueue(args);
                return;
            }
        }

        handler.Invoke(null, new SingleInstanceActivatedEventArgs(args));
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

internal sealed class SingleInstanceActivatedEventArgs(string[] args) : EventArgs
{
    public string[] Args { get; } = args;
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
