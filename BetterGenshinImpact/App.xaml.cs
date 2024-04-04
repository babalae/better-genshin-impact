using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Extensions;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notifier;
using BetterGenshinImpact.View;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.ViewModel;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Wpf.Ui;

namespace BetterGenshinImpact;

public partial class App : Application
{
    // The.NET Generic Host provides dependency injection, configuration, logging, and other services.
    // https://docs.microsoft.com/dotnet/core/extensions/generic-host
    // https://docs.microsoft.com/dotnet/core/extensions/dependency-injection
    // https://docs.microsoft.com/dotnet/core/extensions/configuration
    // https://docs.microsoft.com/dotnet/core/extensions/logging
    private static readonly IHost _host = Host.CreateDefaultBuilder()
        .UseElevated()
        .UseSingleInstance("BetterGI")
        .ConfigureServices(
            (context, services) =>
            {
                // 提前初始化配置
                var configService = new ConfigService();
                services.AddSingleton<IConfigService>(sp => configService);
                var all = configService.Get();

                var logFolder = Path.Combine(AppContext.BaseDirectory, "log");
                Directory.CreateDirectory(logFolder);
                var logFile = Path.Combine(logFolder, "better-genshin-impact.log");

                var maskWindow = new MaskWindow();
                services.AddSingleton(maskWindow);

                var loggerConfiguration = new LoggerConfiguration()
                    .WriteTo.File(path: logFile, outputTemplate: "[{Timestamp:HH:mm:ss.fff}] [{Level:u3}] {SourceContext}{NewLine}{Message}{NewLine}{Exception}{NewLine}", rollingInterval: RollingInterval.Day)
                    .MinimumLevel.Debug()
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Warning);
                if (all.MaskWindowConfig.MaskEnabled)
                {
                    loggerConfiguration.WriteTo.RichTextBox(maskWindow.LogBox, LogEventLevel.Information, outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
                }
                Log.Logger = loggerConfiguration.CreateLogger();
                services.AddLogging(c => c.AddSerilog());

                // App Host
                services.AddHostedService<ApplicationHostService>();

                // Page resolver service
                services.AddSingleton<IPageService, PageService>();

                // Service containing navigation, same as INavigationWindow... but without window
                services.AddSingleton<INavigationService, NavigationService>();

                // Main window with navigation
                services.AddView<INavigationWindow, MainWindow, MainWindowViewModel>();
                services.AddSingleton<NotifyIconViewModel>();

                // Views
                services.AddView<HomePage, HomePageViewModel>();
                services.AddView<ScriptControlPage, ScriptControlViewModel>();
                services.AddView<TriggerSettingsPage, TriggerSettingsPageViewModel>();
                services.AddView<MacroSettingsPage, MacroSettingsPageViewModel>();
                services.AddView<CommonSettingsPage, CommonSettingsPageViewModel>();
                services.AddView<TaskSettingsPage, TaskSettingsPageViewModel>();
                services.AddView<HotKeyPage, HotKeyPageViewModel>();
                services.AddView<NotificationSettingsPage, NotificationSettingsPageViewModel>();

                // My Services
                services.AddSingleton<TaskTriggerDispatcher>();
                services.AddSingleton<NotificationService>();
                services.AddHostedService<NotificationService>(sp => sp.GetRequiredService<NotificationService>());
                services.AddSingleton<NotifierManager>();

                // Configuration
                //services.Configure<AppConfig>(context.Configuration.GetSection(nameof(AppConfig)));
            }
        )
        .Build();

    public static ILogger<T> GetLogger<T>()
    {
        return _host.Services.GetService<ILogger<T>>()!;
    }

    /// <summary>
    /// Gets registered service.
    /// </summary>
    /// <typeparam name="T">Type of the service to get.</typeparam>
    /// <returns>Instance of the service or <see langword="null"/>.</returns>
    public static T? GetService<T>() where T : class
    {
        return _host.Services.GetService(typeof(T)) as T;
    }

    /// <summary>
    /// Gets registered service.
    /// </summary>
    /// <returns>Instance of the service or <see langword="null"/>.</returns>
    /// <returns></returns>
    public static object? GetService(Type type)
    {
        return _host.Services.GetService(type);
    }

    /// <summary>
    /// Occurs when the application is loading.
    /// </summary>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            RegisterEvents();
            await _host.StartAsync();
            await UrlProtocolHelper.RegisterAsync();
        }
        catch (Exception ex)
        {
            // DEBUG only, no overhead
            Debug.WriteLine(ex);

            if (Debugger.IsAttached)
            {
                Debugger.Break();
            }
        }
    }

    /// <summary>
    /// Occurs when the application is closing.
    /// </summary>
    protected override async void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);

        await _host.StopAsync();
        _host.Dispose();
    }

    /// <summary>
    /// 注册事件
    /// </summary>
    private void RegisterEvents()
    {
        //Task线程内未捕获异常处理事件
        TaskScheduler.UnobservedTaskException += TaskSchedulerUnobservedTaskException;

        //UI线程未捕获异常处理事件（UI主线程）
        this.DispatcherUnhandledException += AppDispatcherUnhandledException;

        //非UI线程未捕获异常处理事件(例如自己创建的一个子线程)
        AppDomain.CurrentDomain.UnhandledException += CurrentDomainUnhandledException;
    }

    private static void TaskSchedulerUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            HandleException(e.Exception);
        }
        catch (Exception ex)
        {
            HandleException(ex);
        }
        finally
        {
            e.SetObserved();
        }
    }

    //非UI线程未捕获异常处理事件(例如自己创建的一个子线程)
    private static void CurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            if (e.ExceptionObject is Exception exception)
            {
                HandleException(exception);
            }
        }
        catch (Exception ex)
        {
            HandleException(ex);
        }
        finally
        {
            //ignore
        }
    }

    //UI线程未捕获异常处理事件（UI主线程）
    private static void AppDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            HandleException(e.Exception);
        }
        catch (Exception ex)
        {
            HandleException(ex);
        }
        finally
        {
            //处理完后，我们需要将Handler=true表示已此异常已处理过
            e.Handled = true;
        }
    }

    private static void HandleException(Exception e)
    {
        MessageBox.Show("程序异常：" + e.Source + "\r\n--" + Environment.NewLine + e.StackTrace + "\r\n---" + Environment.NewLine + e.Message);

        // log
    }
}
