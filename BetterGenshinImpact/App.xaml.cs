using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Extensions;
using BetterGenshinImpact.Helpers.Win32;
using BetterGenshinImpact.Hutao;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notifier;
using BetterGenshinImpact.View;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.ViewModel;
using BetterGenshinImpact.ViewModel.Pages;
using BetterGenshinImpact.ViewModel.Pages.View;
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
        .UseSingleInstance("BetterGI")
        .ConfigureLogging(builder => { builder.ClearProviders(); })
        .ConfigureServices(
            (context, services) =>
            {
                EnsureConfigMigrationChoice();
                UserCache.Initialize();

                // 诊断：检查数据库状态
                LogDatabaseStatus();

                // 提前初始化配置
                var configService = new ConfigService();
                services.AddSingleton<IConfigService>(sp => configService);
                var all = configService.Get();

                var logFolder = Path.Combine(AppContext.BaseDirectory, "log");
                Directory.CreateDirectory(logFolder);
                var logFile = Path.Combine(logFolder, "better-genshin-impact.log");

                var richTextBox = new RichTextBoxImpl();
                services.AddSingleton<IRichTextBox>(richTextBox);

                var loggerConfiguration = new LoggerConfiguration()
                    .WriteTo.File(logFile,
                        outputTemplate:
                        "[{Timestamp:HH:mm:ss.fff}] [{Level:u3}] {SourceContext}{NewLine}{Message}{NewLine}{Exception}{NewLine}",
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 31,
                        retainedFileTimeLimit: TimeSpan.FromDays(21))
                    .WriteTo.Console(outputTemplate: 
                        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                    .MinimumLevel.Debug()
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Warning);
                if (all.MaskWindowConfig.MaskEnabled)
                {
                    loggerConfiguration.WriteTo.RichTextBox(richTextBox, LogEventLevel.Information,
                        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
                }

                Log.Logger = loggerConfiguration.CreateLogger();
                services.AddLogging(c => c.AddSerilog());

                services.AddLocalization();

                services.AddNavigationViewPageProvider();
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
                services.AddSingleton<PathingConfigViewModel>();
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
                services.AddSingleton<TaskTriggerDispatcher>();
                services.AddSingleton<NotificationService>();
                services.AddHostedService(sp => sp.GetRequiredService<NotificationService>());
                services.AddSingleton<NotifierManager>();
                services.AddSingleton<IScriptService, ScriptService>();
                services.AddSingleton<HutaoNamedPipe>();
                services.AddSingleton<BgiOnnxFactory>();
                services.AddSingleton<OcrFactory>();
                services.AddMemoryCache();
                services.AddSingleton<IAppCache, CachingService>();
                services.AddSingleton<MemoryFileCache>();
                services.AddSingleton<IMihoyoMapApiService, MihoyoMapApiService>();
                services.AddSingleton<IKongyingTavernApiService, KongyingTavernApiService>();
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
            // DEBUG only, no overhead
            Debug.WriteLine(ex);
            ConsoleHelper.WriteError($"应用程序启动失败: {ex.Message}");

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

        ConsoleHelper.WriteLine("BetterGI 应用程序正在关闭...");

        UserCache.Shutdown();
        TempManager.CleanUp();

        await _host.StopAsync();
        _host.Dispose();
        
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

    private static void HandleException(Exception e)
    {
        if (e.InnerException != null)
        {
            e = e.InnerException;
        }

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

        // log
        GetLogger<App>().LogDebug(e, "UnHandle Exception");
    }

    private static int _configMigrationChecked;

    private static void LogDatabaseStatus()
    {
        try
        {
            var dbPath = UserStorage.DatabasePath;
            var dbExists = File.Exists(dbPath);

            ConsoleHelper.WriteLine($"=== 数据库状态诊断 ===");
            ConsoleHelper.WriteLine($"数据库路径: {dbPath}");
            ConsoleHelper.WriteLine($"数据库存在: {dbExists}");

            if (dbExists)
            {
                var fileInfo = new FileInfo(dbPath);
                ConsoleHelper.WriteLine($"数据库大小: {fileInfo.Length / 1024.0:F2} KB");

                // 检查是否有config.json在数据库中
                var hasConfig = UserStorage.Exists("config.json");
                ConsoleHelper.WriteLine($"config.json在数据库中: {hasConfig}");

                // 列出数据库中的文件数量
                var entries = UserStorage.ListEntries();
                ConsoleHelper.WriteLine($"数据库中的文件总数: {entries.Count}");

                if (entries.Count > 0)
                {
                    ConsoleHelper.WriteLine("数据库中的部分文件:");
                    foreach (var entry in entries.Take(10))
                    {
                        ConsoleHelper.WriteLine($"  - {entry.Path} ({entry.Size} bytes)");
                    }
                }
            }

            // 检查临时缓存目录
            var cacheDir = UserCache.RootDirectory;
            ConsoleHelper.WriteLine($"\n临时缓存目录: {cacheDir}");
            ConsoleHelper.WriteLine($"缓存目录存在: {Directory.Exists(cacheDir)}");

            if (Directory.Exists(cacheDir))
            {
                var cacheFiles = Directory.GetFiles(cacheDir, "*", SearchOption.AllDirectories);
                ConsoleHelper.WriteLine($"缓存文件数量: {cacheFiles.Length}");
            }

            ConsoleHelper.WriteLine("======================\n");
        }
        catch (Exception ex)
        {
            ConsoleHelper.WriteError($"数据库诊断失败: {ex.Message}");
        }
    }

    internal static void EnsureConfigMigrationChoice()
    {
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(EnsureConfigMigrationChoice);
            return;
        }

        EnsureConfigMigrationChoiceInternal();
    }

    private static void EnsureConfigMigrationChoiceInternal()
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            return;
        }

        if (Interlocked.Exchange(ref _configMigrationChecked, 1) == 1)
        {
            return;
        }

        var dbExists = File.Exists(UserStorage.DatabasePath);
        var legacyConfigExists = UserStorage.LegacyConfigFileExists();

        // 如果数据库不存在但有旧配置文件，询问是否迁移
        if (!dbExists && legacyConfigExists)
        {
            var result = MessageBox.Show(
                "检测到旧版本配置文件。\n是否迁移到数据库？\n\n是：迁移并继续使用旧配置\n否：新建主配置（保留旧配置文件）",
                "BetterGI - 配置迁移",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                UserStorage.MarkLegacyConfigIgnored();
            }
        }
        // 如果数据库已存在，显示信息提示
        else if (dbExists)
        {
            // 检查是否是首次使用数据库（通过检查是否有迁移标记）
            try
            {
                using var connection = OpenDatabaseConnection();
                var migratedDisk = GetMetaValue(connection, "migrated_disk");
                var migratedConfig = GetMetaValue(connection, "migrated_config_entries");
                var firstDbUse = GetMetaValue(connection, "first_db_use_notified");

                // 如果已经迁移但还没有通知过用户
                if ((migratedDisk == "1" || migratedConfig == "1") && firstDbUse != "1")
                {
                    MessageBox.Show(
                        "配置文件已成功迁移到数据库！\n\n" +
                        "• 所有配置现在存储在 User/config.db 中\n" +
                        "• 脚本文件仍保存在原有路径，方便管理\n" +
                        "• 数据库提供更好的性能和可靠性",
                        "BetterGI - 配置迁移完成",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    SetMetaValue(connection, "first_db_use_notified", "1");
                }
            }
            catch
            {
                // 忽略错误，不影响启动
            }
        }
    }

    private static Microsoft.Data.Sqlite.SqliteConnection OpenDatabaseConnection()
    {
        var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = UserStorage.DatabasePath,
            Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate,
            Cache = Microsoft.Data.Sqlite.SqliteCacheMode.Shared
        };

        var connection = new Microsoft.Data.Sqlite.SqliteConnection(builder.ToString());
        connection.Open();
        return connection;
    }

    private static string? GetMetaValue(Microsoft.Data.Sqlite.SqliteConnection connection, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT meta_value FROM user_meta WHERE meta_key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar()?.ToString();
    }

    private static void SetMetaValue(Microsoft.Data.Sqlite.SqliteConnection connection, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
                               INSERT INTO user_meta (meta_key, meta_value)
                               VALUES ($key, $value)
                               ON CONFLICT(meta_key) DO UPDATE SET meta_value = excluded.meta_value;
                               """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }
}
