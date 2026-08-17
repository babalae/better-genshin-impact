using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Core.Monitor;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Music.Service;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Extensions;
using BetterGenshinImpact.Helpers.Win32;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.ChildSession;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notifier;
using BetterGenshinImpact.View;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel;
using BetterGenshinImpact.ViewModel.Pages;
using BetterGenshinImpact.ViewModel.Pages.View;
using BetterGenshinImpact.ViewModel.Windows;
using LazyCache;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.RichTextBox.Abstraction;
using Wpf.Ui;
using Wpf.Ui.DependencyInjection;
using Wpf.Ui.Violeta.Appearance;
using Wpf.Ui.Violeta.Controls;

// Wine 平台适配
using BetterGenshinImpact.Platform.Wine;
using BetterGenshinImpact.Service.Tavern;

namespace BetterGenshinImpact;

public partial class App : Application
{
    // The.NET Generic Host provides dependency injection, configuration, logging, and other services.
    // https://docs.microsoft.com/dotnet/core/extensions/generic-host
    // https://docs.microsoft.com/dotnet/core/extensions/dependency-injection
    // https://docs.microsoft.com/dotnet/core/extensions/configuration
    // https://docs.microsoft.com/dotnet/core/extensions/logging
    private static readonly IHost _host = Host.CreateDefaultBuilder()
        .CheckIntegration()
        .UseElevated()
        .UseInstanceIpc()
        .ConfigureLogging(builder => { builder.ClearProviders(); })
        .ConfigureServices((context, services) =>
            {
                // 提前初始化配置
                var configService = new ConfigService();
                services.AddSingleton<IConfigService>(sp => configService);
                var all = configService.Get();

                var logFolder = Path.Combine(AppContext.BaseDirectory, "log");
                Directory.CreateDirectory(logFolder);
                var logFile = Path.Combine(logFolder, "better-genshin-impact.log");
                var instanceContext = InstanceBootstrap.Current.Context;
                var instanceIdentity =
                    $"{instanceContext.InstanceType}:S{instanceContext.WindowsSessionId}:P{instanceContext.ProcessId}:T{instanceContext.StartedAt.ToUnixTimeMilliseconds()}";

                var richTextBox = new RichTextBoxImpl();
                services.AddSingleton<IRichTextBox>(richTextBox);

                var loggerConfiguration = new LoggerConfiguration()
                    .WriteTo.Logger(fileLoggerConfiguration => fileLoggerConfiguration
                        .Enrich.WithProperty("BgiInstance", instanceIdentity)
                        .WriteTo.File(logFile,
                            outputTemplate:
                            "[{Timestamp:HH:mm:ss.fff}] [{Level:u3}] [{BgiInstance}] {SourceContext}{NewLine}{Message}{NewLine}{Exception}{NewLine}",
                            rollingInterval: RollingInterval.Day,
                            shared: true,
                            retainedFileCountLimit: 31,
                            retainedFileTimeLimit: TimeSpan.FromDays(21)))
                    .WriteTo.Console(outputTemplate:
                        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                    .MinimumLevel.Debug()
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Warning);
                if (all.MaskWindowConfig is { MaskEnabled: true, ShowLogBox: true })
                {
                    loggerConfiguration.WriteTo.RichTextBox(richTextBox, LogEventLevel.Information,
                        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
                }

                Log.Logger = loggerConfiguration.CreateLogger();
                services.AddSingleton<IMissingTranslationReporter, SupabaseMissingTranslationReporter>();
                services.AddSingleton<ITranslationService, JsonTranslationService>();

                services.AddLogging(c => c.AddSerilog());
                // if ("zh-Hans".Equals(all.OtherConfig.UiCultureInfoName, StringComparison.OrdinalIgnoreCase))
                // {
                //     services.AddLogging(c => c.AddSerilog());
                // }
                // else
                // {
                //     services.AddLogging(logging =>
                //     {
                //         logging.ClearProviders();
                //         logging.SetMinimumLevel(LogLevel.Debug);
                //         logging.AddFilter("Microsoft", LogLevel.Warning);
                //         logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);
                //         logging.Services.AddSingleton<ILoggerProvider, TranslatingSerilogLoggerProvider>();
                //     });
                // }

                services.AddLocalization();

                services.AddNavigationViewPageProvider();
                services.AddSingleton(InstanceBootstrap.Current);
                services.AddSingleton<InstanceService>();
                services.AddHostedService(sp => sp.GetRequiredService<InstanceService>());
                // App Host
                services.AddHostedService<ApplicationHostService>();
                // Page resolver service
                services.AddSingleton<INavigationService, NavigationService>();
                services.AddSingleton<IUpdateService, UpdateService>();

                // Service containing navigation, same as INavigationWindow... but without window
                services.AddSingleton<INavigationService, NavigationService>();
                services.AddSingleton<ISnackbarService, SnackbarService>();

                // Main window with navigation
                services.AddView<INavigationWindow, MainWindow, MainWindowViewModel>();
                services.AddSingleton<NotifyIconViewModel>();
                services.AddSingleton<ChildSessionService>();
                services.AddSingleton<ChildSessionAutomationService>();
                services.AddTransient<ChildSessionWindowViewModel>();
                services.AddTransient<ChildSessionWindow>();

                // Views
                services.AddView<HomePage, HomePageViewModel>();
                services.AddView<ScriptControlPage, ScriptControlViewModel>();
                services.AddView<TriggerSettingsPage, TriggerSettingsPageViewModel>();
                services.AddView<MacroSettingsPage, MacroSettingsPageViewModel>();
                services.AddView<CommonSettingsPage, CommonSettingsPageViewModel>();
                services.AddView<TaskSettingsPage, TaskSettingsPageViewModel>();
                services.AddView<HotKeyPage, HotKeyPageViewModel>();
                services.AddView<NotificationSettingsPage, NotificationSettingsPageViewModel>();
                services.AddView<KeyMouseRecordPage, KeyMouseRecordPageViewModel>();
                services.AddView<JsListPage, JsListViewModel>();
                services.AddView<MapPathingPage, MapPathingViewModel>();
                services.AddView<OneDragonFlowPage, OneDragonFlowViewModel>();
                services.AddView<MusicPage, MusicPageViewModel>();
                services.AddSingleton<PathingConfigViewModel>();
                services.AddSingleton<IBannerImageService, BannerImageService>();
                services.AddTransient<WebImageInputViewModel>();
                // services.AddView<PathingConfigView, PathingConfigViewModel>();
                services.AddView<KeyBindingsSettingsPage, KeyBindingsSettingsPageViewModel>();

                // 一条龙 ViewModels
                // services.AddSingleton<CraftViewModel>();
                // services.AddSingleton<DailyCommissionViewModel>();
                // services.AddSingleton<DailyRewardViewModel>();
                // services.AddSingleton<DomainViewModel>();
                // services.AddSingleton<ForgingViewModel>();
                // services.AddSingleton<LeyLineBlossomViewModel>();
                // services.AddSingleton<MailViewModel>();
                // services.AddSingleton<SereniteaPotViewModel>();
                // services.AddSingleton<TcgViewModel>();

                // My Services
                services.AddSingleton<DirectInputMonitor>();
                services.AddSingleton<RawInputMonitor>();
                services.AddSingleton<IRelativeMouseInputMonitorFactory, RelativeMouseInputMonitorFactory>();
                services.AddSingleton<OverlayMetricsService>();
                services.AddSingleton<CustomHtmlMaskService>();
                services.AddSingleton<TaskTriggerDispatcher>();
                services.AddSingleton<RecognitionTemplateAssetService>();
                services.AddSingleton<RecognitionTemplateEditorService>();
                services.AddSingleton<NotificationService>();
                services.AddHostedService(sp => sp.GetRequiredService<NotificationService>());
                services.AddSingleton<NotifierManager>();
                services.AddSingleton<IScriptService, ScriptService>();
                services.AddSingleton<IMusicScoreParser, MusicScoreParser>();
                services.AddSingleton<IMusicStateStore, MusicStateStore>();
                services.AddSingleton<IInstrumentProfileService, InstrumentProfileService>();
                services.AddSingleton<IMusicTimelineBuilder, MusicTimelineBuilder>();
                services.AddSingleton<IMusicLibraryService, MusicLibraryService>();
                services.AddSingleton<IMusicCoverService, MusicCoverService>();
                services.AddSingleton<IKeyInputTransport, PostMessageKeyInputTransport>();
                services.AddSingleton<IKeyInputTransport, SendInputKeyInputTransport>();
                services.AddSingleton<IMusicPlaybackService, MusicPlaybackService>();
                services.AddSingleton<BgiOnnxFactory>();
                services.AddSingleton<OcrFactory>();
                services.AddMemoryCache();
                services.AddSingleton<IAppCache, CachingService>();
                services.AddSingleton<MemoryFileCache>();
                services.AddSingleton<IMihoyoMapApiService, MihoyoMapApiService>();
                services.AddSingleton<IKongyingTavernApiService, KongyingTavernApiService>();
                services.AddSingleton<IHoYoLabMapApiService, HoYoLabMapApiService>();
                services.AddSingleton<IMaskMapPointService, MaskMapPointService>();

                services.AddSingleton(TimeProvider.System);
                services.AddSingleton<IServerTimeProvider, ServerTimeProvider>();

                // Configuration
                //services.Configure<AppConfig>(context.Configuration.GetSection(nameof(AppConfig)));

                I18N.Culture = new CultureInfo("zh-Hans"); // #1846
            }
        )
        .Build();

    public static IServiceProvider ServiceProvider => _host.Services;

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
        Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal;
        // Wine 平台适配
        WinePlatformAddon.ApplyApplicationConfig();
        base.OnStartup(e);

        try
        {
            // 分配控制台窗口以支持控制台输出
            ConsoleHelper.AllocateConsole("BetterGI Console");
            RegisterEvents();
            await _host.StartAsync();
            ServerTimeHelper.Initialize(_host.Services.GetRequiredService<IServerTimeProvider>());
            await UrlProtocolHelper.RegisterAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            ConsoleHelper.WriteError($"应用程序启动失败: {ex.Message}");

            var dialogShown = false;
            try
            {
                // 启动失败发生在 MainWindow 创建前，日志遮罩可能不可用；
                // 视为致命异常，记录日志并尝试弹窗。
                dialogShown = HandleException(ex, isTerminating: true);
            }
            catch (Exception ex2)
            {
                Debug.WriteLine(ex2);
                ConsoleHelper.WriteError($"应用程序启动失败打印日志时又失败了: {ex2.Message}");
            }

            // 启动早期 WPF Dispatcher 可能尚未就绪，HandleException 可能因此不弹窗。
            // 仅在未显示 WPF 弹窗时，用不依赖 WPF 的 WinForms MessageBox 兜底，
            // 避免用户连续看到两个错误对话框。
            if (!dialogShown)
            {
                try
                {
                    System.Windows.Forms.MessageBox.Show(
                        $"{TranslateText("应用程序启动失败：")}{ex.Message}",
                        TranslateText("BetterGI 启动失败"),
                        System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Error);
                }
                catch
                {
                    // 弹窗失败不影响退出。
                }
            }

            // 启动失败 = 无可用的主界面，直接退出，避免留下无窗口的残留进程。
            Shutdown();
        }
    }

    /// <summary>
    /// Occurs when the application is closing.
    /// </summary>
    protected override async void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);

        ConsoleHelper.WriteLine("BetterGI 应用程序正在关闭...");

        TempManager.CleanUp();

        await _host.StopAsync();
        _host.Dispose();
        Log.CloseAndFlush();

        // 释放控制台窗口
        ConsoleHelper.FreeConsoleWindow();
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
            // 忽略V8引擎释放后pending的Task回调抛出的异常
            if (IsV8EngineReleasedException(e.Exception))
            {
                return;
            }

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

    private static bool IsV8EngineReleasedException(Exception? ex)
    {
        while (ex != null)
        {
            if (ex.Message?.Contains("V8 object has been released") == true)
            {
                return true;
            }

            ex = ex.InnerException;
        }

        return false;
    }

    //非UI线程未捕获异常处理事件(例如自己创建的一个子线程)
    private static void CurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            if (e.ExceptionObject is Exception exception)
            {
                // 用官方的 IsTerminating 判断是否致命：致命异常进程将终止，需同步弹窗确保用户可见。
                HandleException(exception, isTerminating: e.IsTerminating);
            }
        }
        catch (Exception ex)
        {
            HandleException(ex, isTerminating: e.IsTerminating);
        }
        finally
        {
            //ignore
        }
    }

    //UI线程未捕获异常处理事件（UI主线程）
    private static void AppDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
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

    /// <summary>
    /// 处理未处理异常。返回 true 表示已尝试显示弹窗（或已处理），false 表示未弹窗（Dispatcher 不可用）。
    /// </summary>
    private static bool HandleException(Exception e, bool isTerminating = false)
    {
        if (e.InnerException != null)
        {
            e = e.InnerException;
        }

        // 错误日志最先落盘并推送到日志遮罩（LogError ≥ Information 会进入遮罩 LogTextBox）。
        // 文件日志：Debug 级别也写盘；遮罩：仅 Information 以上可见。
        // 致命异常（IsTerminating）在日志末尾加 [FATAL] 标记，便于区分。
        var logMessage = isTerminating ? "UnHandle Exception [FATAL]" : "UnHandle Exception";
        GetLogger<App>().LogError(e, logMessage);

        // 可恢复异常（默认）：仅日志，不弹模态窗，避免阻塞 UI 线程。
        // 通过日志遮罩提示用户：非致命异常已记录。
        if (!isTerminating)
        {
            var nonFatalMessage = TranslateText("发生非致命异常，已记录日志，请查看日志详情。");
            GetLogger<App>().LogWarning(nonFatalMessage);
            return false;
        }

        // 终止性异常（如线程池未处理异常导致进程即将结束）：进程终止前同步弹窗兜底，
        // 确保用户能看到报告（阻塞无妨，进程反正要终止）。
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null
            || dispatcher.HasShutdownStarted
            || dispatcher.HasShutdownFinished)
        {
            // Dispatcher 不可用（如启动早期或已关闭）：仅日志，不弹窗。
            return false;
        }

        // 确认 Dispatcher 可用后才记录"正在弹窗"，避免与实际行为不一致。
        var popupShownMessage = TranslateText("发生致命异常，正在弹窗提示，同时已记录日志。");
        GetLogger<App>().LogWarning(popupShownMessage);

        try
        {
            if (dispatcher.CheckAccess())
            {
                ShowExceptionDialog(e);
            }
            else
            {
                // 双信号：startedSignal 表示 UI 线程开始执行弹窗（有限超时），
                // completedSignal 表示弹窗已关闭（无超时等待）。
                // ShowExceptionDialog 是模态的，用户读完并关闭才返回；
                // 若只在"开始"后立即返回，进程终止会强杀刚创建的弹窗。
                using var startedSignal = new System.Threading.ManualResetEventSlim(false);
                using var completedSignal = new System.Threading.ManualResetEventSlim(false);
                dispatcher.InvokeAsync(new Action(() =>
                {
                    try
                    {
                        startedSignal.Set();
                        ShowExceptionDialog(e);
                    }
                    finally
                    {
                        completedSignal.Set();
                    }
                }));

                // 只为"UI 是否开始执行"设置超时：UI 线程被阻塞/死锁时避免无限等待。
                if (!startedSignal.Wait(TimeSpan.FromSeconds(3)))
                {
                    GetLogger<App>().LogWarning(
                        TranslateText("弹窗调度超时，异常已记录，进程即将退出。"));
                    return false;
                }

                // 一旦确认开始执行，就等待弹窗关闭，确保用户能读完致命异常详情。
                completedSignal.Wait();
            }

            return true;
        }
        catch
        {
            // 弹窗失败不影响进程退出。
            return false;
        }
    }

    /// <summary>
    /// 翻译一条异常提示文本。翻译服务不可用时返回原文。
    /// </summary>
    private static string TranslateText(string text)
    {
        try
        {
            return ServiceProvider.GetService<ITranslationService>()?.Translate(text) ?? text;
        }
        catch
        {
            return text;
        }
    }

    private static void ShowExceptionDialog(Exception e)
    {
        try
        {
            ExceptionReport.Show(e);
        }
        catch
        {
            // Fallback.
            System.Windows.Forms.MessageBox.Show(
                $"""
                 程序异常：{e.Source}
                 --
                 {e.StackTrace}
                 --
                 {e.Message}
                 """
            );
        }
    }
}
