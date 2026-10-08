using System;
using System.Linq;
using System.Windows;
using BetterGenshinImpact.Service.Worker;
using Serilog;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.Helpers;

/// <summary>
/// <c>Toast</c> 的门面。项目通过 <c>GlobalUsing.cs</c> 里的
/// <c>global using Toast = BetterGenshinImpact.Helpers.BetterGiToast;</c> 别名，
/// 让全部 <c>Toast.Xxx(...)</c> 调用自动走这里，无需逐个改造调用点。
/// <para>
/// 目的有两个：
/// <list type="number">
/// <item>无头 Worker 没有主窗口，<see cref="Wpf.Ui.Violeta.Controls.Toast"/> 会直接在
/// <c>Window.GetWindow(null)</c> 上抛 <see cref="ArgumentNullException"/>。本门面在无窗口时只写日志，
/// 并把提示回传给 Controller 显示。</item>
/// <item>Worker 上产生的提示原本只有 Worker 所在用户看得到，现在统一回传给连接中的 Controller。</item>
/// </list>
/// </para>
/// </summary>
public static class BetterGiToast
{
    public static bool IsStacked
    {
        get => Wpf.Ui.Violeta.Controls.Toast.IsStacked;
        set => Wpf.Ui.Violeta.Controls.Toast.IsStacked = value;
    }

    public static void Information(string message,
        ToastLocation location = ToastLocation.TopCenter,
        Thickness offsetMargin = default,
        int time = ToastConfig.NormalTime)
    {
        Emit(WorkerNoticeLevel.Information, message,
            () => Wpf.Ui.Violeta.Controls.Toast.Information(message, location, offsetMargin, time));
    }

    public static void Success(string message,
        ToastLocation location = ToastLocation.TopCenter,
        Thickness offsetMargin = default,
        int time = ToastConfig.NormalTime)
    {
        Emit(WorkerNoticeLevel.Success, message,
            () => Wpf.Ui.Violeta.Controls.Toast.Success(message, location, offsetMargin, time));
    }

    public static void Warning(string message,
        ToastLocation location = ToastLocation.TopCenter,
        Thickness offsetMargin = default,
        int time = ToastConfig.NormalTime)
    {
        Emit(WorkerNoticeLevel.Warning, message,
            () => Wpf.Ui.Violeta.Controls.Toast.Warning(message, location, offsetMargin, time));
    }

    public static void Error(string message,
        ToastLocation location = ToastLocation.TopCenter,
        Thickness offsetMargin = default,
        int time = ToastConfig.NormalTime)
    {
        Emit(WorkerNoticeLevel.Error, message,
            () => Wpf.Ui.Violeta.Controls.Toast.Error(message, location, offsetMargin, time));
    }

    public static void Question(string message,
        ToastLocation location = ToastLocation.TopCenter,
        Thickness offsetMargin = default,
        int time = ToastConfig.NormalTime)
    {
        Emit(WorkerNoticeLevel.Information, message,
            () => Wpf.Ui.Violeta.Controls.Toast.Question(message, location, offsetMargin, time));
    }

    public static void Information(FrameworkElement owner, string message,
        ToastLocation location = ToastLocation.TopCenter,
        Thickness offsetMargin = default,
        int time = ToastConfig.NormalTime)
    {
        Emit(WorkerNoticeLevel.Information, message,
            () => Wpf.Ui.Violeta.Controls.Toast.Information(owner, message, location, offsetMargin, time));
    }

    public static void Success(FrameworkElement owner, string message,
        ToastLocation location = ToastLocation.TopCenter,
        Thickness offsetMargin = default,
        int time = ToastConfig.NormalTime)
    {
        Emit(WorkerNoticeLevel.Success, message,
            () => Wpf.Ui.Violeta.Controls.Toast.Success(owner, message, location, offsetMargin, time));
    }

    public static void Warning(FrameworkElement owner, string message,
        ToastLocation location = ToastLocation.TopCenter,
        Thickness offsetMargin = default,
        int time = ToastConfig.NormalTime)
    {
        Emit(WorkerNoticeLevel.Warning, message,
            () => Wpf.Ui.Violeta.Controls.Toast.Warning(owner, message, location, offsetMargin, time));
    }

    public static void Error(FrameworkElement owner, string message,
        ToastLocation location = ToastLocation.TopCenter,
        Thickness offsetMargin = default,
        int time = ToastConfig.NormalTime)
    {
        Emit(WorkerNoticeLevel.Error, message,
            () => Wpf.Ui.Violeta.Controls.Toast.Error(owner, message, location, offsetMargin, time));
    }

    public static void Question(FrameworkElement owner, string message,
        ToastLocation location = ToastLocation.TopCenter,
        Thickness offsetMargin = default,
        int time = ToastConfig.NormalTime)
    {
        Emit(WorkerNoticeLevel.Information, message,
            () => Wpf.Ui.Violeta.Controls.Toast.Question(owner, message, location, offsetMargin, time));
    }

    public static void Show(FrameworkElement owner, string message, ToastConfig options)
    {
        Emit(WorkerNoticeLevel.Information, message,
            () => Wpf.Ui.Violeta.Controls.Toast.Show(owner, message, options));
    }

    /// <summary>
    /// 提示入口。无头 Worker 没有窗口可承载 Toast，提示统一回传给 Controller 显示；
    /// 其余情况按原样在本机显示。任何情况下都不允许因为显示提示而抛出异常影响调用方。
    /// </summary>
    private static void Emit(WorkerNoticeLevel level, string message, Action showLocal)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        if (CommandLineOptions.Instance.Headless)
        {
            var forwarded = PublishToController(level, message);
            Log.Information(
                forwarded ? "提示已回传控制端：{Message}" : "提示（无控制端连接，仅记录日志）：{Message}",
                message);
            return;
        }

        if (!TryShowLocal(showLocal))
        {
            Log.Information("提示（当前实例无窗口可显示，仅记录日志）：{Message}", message);
        }
    }

    /// <returns>是否确实回传给了至少一个 Controller</returns>
    private static bool PublishToController(WorkerNoticeLevel level, string message)
    {
        try
        {
            var hub = App.GetService<WorkerNoticeHub>();
            if (hub is null || !hub.HasControllers)
            {
                return false;
            }

            hub.Publish(level, message);
            return true;
        }
        catch (Exception exception)
        {
            Log.Debug(exception, "回传提示到 Controller 失败，已忽略");
            return false;
        }
    }

    private static bool TryShowLocal(Action showLocal)
    {
        var application = Application.Current;
        if (application is null || !HasToastHost(application))
        {
            return false;
        }

        try
        {
            var dispatcher = application.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                showLocal();
            }
            else
            {
                dispatcher.BeginInvoke(showLocal);
            }

            return true;
        }
        catch (Exception exception)
        {
            Log.Debug(exception, "显示提示失败，已忽略");
            return false;
        }
    }

    /// <summary>
    /// Toast 需要一个可用的窗口作为 Owner，否则会在 Window.GetWindow 上抛异常
    /// </summary>
    private static bool HasToastHost(Application application)
    {
        return application.MainWindow is not null
               || application.Windows.OfType<Window>().Any(window => window.IsActive);
    }
}
