using MicaSetup.Helper;
using MicaSetup.Natives;
using MicaSetup.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace MicaSetup.Design.Controls;

#pragma warning disable IDE0002

public static class HostBuilderExtension
{
    public static IHostBuilder UseHostBuilder(this IHostBuilder builder, Action<IHostBuilder> custom)
    {
        custom?.Invoke(builder);
        return builder;
    }

    public static IHostBuilder UseAsUninst(this IHostBuilder builder)
    {
        Option.Current.IsUninst = true;
        return builder;
    }

    public static IHostBuilder UseLogger(this IHostBuilder builder, bool enabled = true)
    {
        Option.Current.Logging = enabled;
        Logger.Info("Setup run started ...");
        return builder;
    }

    public static IHostBuilder UseServices(this IHostBuilder builder, Action<IServiceCollection> service)
    {
        ServiceCollection serviceCollection = new();
        service?.Invoke(serviceCollection);
        ServiceManager.Services = builder.ServiceProvider = serviceCollection.BuildServiceProvider();
        return builder;
    }

    public static IHostBuilder UseTempPathFork(this IHostBuilder builder)
    {
        if (RuntimeHelper.IsDebuggerAttached)
        {
            return builder;
        }
        TempPathForkHelper.Fork();
        return builder;
    }

    public static IHostBuilder UseElevated(this IHostBuilder builder)
    {
        RuntimeHelper.EnsureElevated();
        return builder;
    }

    public static IHostBuilder UseDpiAware(this IHostBuilder builder)
    {
        _ = DpiAwareHelper.SetProcessDpiAwareness();
        return builder;
    }

    public static IHostBuilder UseSingleInstance(this IHostBuilder builder, string instanceName, Action<bool> callback = null!)
    {
        RuntimeHelper.CheckSingleInstance(instanceName, callback);
        return builder;
    }

    public static IHostBuilder UseLanguage(this IHostBuilder builder, string name)
    {
        Thread.CurrentThread.CurrentUICulture = Thread.CurrentThread.CurrentCulture = new CultureInfo(name);
        return builder;
    }

    public static IHostBuilder UseMuiLanguage(this IHostBuilder builder)
    {
        MuiLanguage.SetupLanguage();
        return builder;
    }

    public static IHostBuilder UseTheme(this IHostBuilder builder, WindowsTheme theme = WindowsTheme.Auto)
    {
        ThemeService.Current.SetTheme(theme);
        return builder;
    }

    public static IHostBuilder UseFonts(this IHostBuilder builder, Action<List<MuiLanguageFont>> handler)
    {
        handler?.Invoke(MuiLanguage.FontSelector);
        return builder;
    }

    public static IHostBuilder UseOptions(this IHostBuilder builder, Action<Option> handler)
    {
        handler?.Invoke(Option.Current);
        return builder;
    }

    public static IHostBuilder UsePages(this IHostBuilder builder, Action<Dictionary<string, Type>> handler)
    {
        handler?.Invoke(ShellPageSetting.PageDict);
        return builder;
    }

    public static IHostBuilder UseDispatcherUnhandledExceptionCatched(this IHostBuilder builder, DispatcherUnhandledExceptionEventHandler handler = null!)
    {
        if (builder?.App != null)
        {
            if (handler != null)
            {
                if (builder!.App is Application app)
                {
                    app.DispatcherUnhandledException += handler;
                }
            }
            else
            {
                if (builder!.App is Application app)
                {
                    app.DispatcherUnhandledException += (object s, DispatcherUnhandledExceptionEventArgs e) =>
                    {
                        Logger.Fatal("Application.DispatcherUnhandledException", e?.Exception?.ToString()!);
                        e!.Handled = true;
                    };
                }
            }
        }
        return builder!;
    }

    public static IHostBuilder UseDomainUnhandledExceptionCatched(this IHostBuilder builder, UnhandledExceptionEventHandler handler = null!)
    {
        if (handler != null)
        {
            AppDomain.CurrentDomain.UnhandledException += handler;
        }
        else
        {
            AppDomain.CurrentDomain.UnhandledException += (object s, UnhandledExceptionEventArgs e) =>
            {
                Logger.Fatal("AppDomain.CurrentDomain.UnhandledException", e?.ExceptionObject?.ToString()!);
            };
        }
        return builder;
    }

    public static IHostBuilder UseUnobservedTaskExceptionCatched(this IHostBuilder builder, EventHandler<UnobservedTaskExceptionEventArgs> handler = null!)
    {
        if (handler != null)
        {
            TaskScheduler.UnobservedTaskException += handler;
        }
        else
        {
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Logger.Fatal("TaskScheduler.UnobservedTaskException", e?.Exception?.ToString()!);
                e?.SetObserved();
            };
        }
        return builder;
    }
}
