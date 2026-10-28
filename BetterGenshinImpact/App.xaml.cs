using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Mask;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Core.Monitor;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.GameTask.SoloTask.Execution;
using BetterGenshinImpact.GameTask.AutoSkip.Audio;
using BetterGenshinImpact.GameTask.Music.Service;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.GameTask.Runtime.Win32;
using BetterGenshinImpact.GameTask.Runtime.WebPage;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Extensions;
using BetterGenshinImpact.Helpers.Win32;
using BetterGenshinImpact.Infrastructure.Cache;
using BetterGenshinImpact.Infrastructure.Network;
using BetterGenshinImpact.Infrastructure.Network.Download;
using BetterGenshinImpact.Infrastructure.Storage;
using BetterGenshinImpact.Infrastructure.Temp;
using BetterGenshinImpact.Infrastructure.WebView;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Remote;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.Service.ChildSession;
using BetterGenshinImpact.Service.ExternalAccess;
using BetterGenshinImpact.Service.ExternalAccess.Logging;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.Service.I18n;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notifier;
using BetterGenshinImpact.View;
using BetterGenshinImpact.View.Mask;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel;
using BetterGenshinImpact.ViewModel.Controls;
using BetterGenshinImpact.ViewModel.Pages;
using BetterGenshinImpact.ViewModel.Pages.View;
using BetterGenshinImpact.ViewModel.SoloTaskSettings;
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
        .UseDefaultServiceProvider((context, options) =>
        {
#if DEBUG
            options.ValidateOnBuild = true;
            options.ValidateScopes = true;
#endif
        })
        .ConfigureLogging(builder => { builder.ClearProviders(); })
        .ConfigureServices((context, services) =>
            {
                // 提前初始化配置
                var configService = new ConfigService();
                services.AddSingleton<IConfigService>(sp => configService);
                var all = configService.Get();
                // 只读的配置根，与 IConfigService.Get() 是同一个实例；需要保存的继续使用 IConfigService
                services.AddSingleton(all);

                // 网络：在任何请求发出前接管默认代理，第三方 SDK 和自建的 HttpClient 也跟随“网络与代理”设置
                var appWebProxy = new AppWebProxy(all.NetworkConfig, HttpClient.DefaultProxy);
                HttpClient.DefaultProxy = appWebProxy;
                services.AddBgiHttpClients(appWebProxy);

                var logFolder = Path.Combine(AppContext.BaseDirectory, "log");
                Directory.CreateDirectory(logFolder);
                var logFile = Path.Combine(logFolder, "better-genshin-impact.log");
                var instanceContext = InstanceBootstrap.Current.Context;
                // 网页版实例带上实例名，例如 WebView(小号A):S1:P1234:T…
                var instanceTypeLabel = instanceContext.InstanceName is { } instanceName
                    ? $"{instanceContext.InstanceType}({instanceName})"
                    : instanceContext.InstanceType.ToString();
                var instanceIdentity =
                    $"{instanceTypeLabel}:S{instanceContext.WindowsSessionId}:P{instanceContext.ProcessId}:T{instanceContext.StartedAt.ToUnixTimeMilliseconds()}";

                // 外部日志分发器必须先于 Serilog 创建，确保启动阶段日志也能进入回放缓冲。
                var externalLogHub = new ExternalLogHub();
                services.AddSingleton(externalLogHub);

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
                loggerConfiguration.WriteTo.Sink(
                    new ExternalLogSink(externalLogHub, instanceIdentity),
                    LogEventLevel.Debug);
                // 日志遮罩输出：仅当“遮罩启用且日志框可见”时才真正写入，隐藏时避免不必要的 UI 开销（#3161）。
                // 条件改为运行时每次写入时动态判断，因此启动后通过快捷键切换 ShowLogBox 也能即时恢复日志（#3357）。
                loggerConfiguration.WriteTo.Sink(
                    new ConditionalLogEventSink(
                        new LoggerConfiguration()
                            .WriteTo.RichTextBox(richTextBox, LogEventLevel.Information,
                                "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                            .CreateLogger(),
                        () => all.MaskWindowConfig is { MaskEnabled: true, ShowLogBox: true }),
                    LogEventLevel.Information);

                Log.Logger = loggerConfiguration.CreateLogger();
                services.AddLogging(c => c.AddSerilog());

                // 配置和日志已经就绪，在任何推理服务或会话初始化前选定原生运行库。
                all.HardwareAccelerationConfig ??= new();
                var inferenceStartupStatus = InferenceRuntimeService.PrepareStartup(all.HardwareAccelerationConfig, InstanceBootstrap.Current.Context.IsRoot);
                // 只规范配置并绑定运行库；待验证状态在主窗口实际开始验证时才落盘。
                configService.Save();
                services.AddSingleton(inferenceStartupStatus);
                var onnxRuntimeLoader = new OnnxRuntimeLoader(all.HardwareAccelerationConfig, AppContext.BaseDirectory);
                services.AddSingleton(onnxRuntimeLoader);
                services.AddSingleton<IOnnxRuntimeSelection>(onnxRuntimeLoader);
                // 下载服务同时是下载中心的任务列表和下载目录的存储区域，见 Docs/design/infrastructure-foundation.md 第 7 节
                services.AddSingleton(sp => new DownloadService(
                    sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientNames.Download),
                    AppPaths.Downloads,
                    sp.GetRequiredService<ILogger<DownloadService>>()));
                services.AddSingleton<IDownloadService>(sp => sp.GetRequiredService<DownloadService>());
                services.AddSingleton<IDownloadMonitor>(sp => sp.GetRequiredService<DownloadService>());
                services.AddSingleton<IStorageAreaProvider>(sp => sp.GetRequiredService<DownloadService>());
                services.AddSingleton<DownloadCenterViewModel>();
                // 运行库安装包经下载服务下载；NuGet、NVIDIA 元数据走 Default 客户端（自动解压缩 gzip）
                services.AddSingleton(sp => new OnnxRuntimeInstaller(
                    AppContext.BaseDirectory,
                    sp.GetRequiredService<IDownloadService>(),
                    sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientNames.Default)));
                services.AddSingleton<IOnnxRuntimeInstaller>(sp => sp.GetRequiredService<OnnxRuntimeInstaller>());
                services.AddSingleton<IInferenceDeviceDiscovery, InferenceDeviceDiscovery>();
                services.AddSingleton<IInferenceBenchmarkLeaseProvider>(sp => new InferenceBenchmarkCoordinator(
                    BetterGenshinImpact.GameTask.Common.TaskControl.TaskSemaphore,
                    () => sp.GetRequiredService<TaskTriggerDispatcher>().PauseForInference()));
                services.AddSingleton<InferenceModelDiagnostics>();
                services.AddSingleton<IInferenceModelDiagnostics>(sp => sp.GetRequiredService<InferenceModelDiagnostics>());
                services.AddSingleton<InferenceRuntimeService>();
                services.AddSingleton<HardwareAccelerationViewModelFactory>();
                services.AddSingleton<IInferenceSettingsInteraction, InferenceSettingsInteraction>();

                services.AddLocalization();
                var i18nService = I18nService.Instance;
                var uiLanguage = all.OtherConfig.UiCultureInfoName switch
                {
                    "zh-CN" => "zh-Hans",
                    "en-US" => "en",
                    "ja-JP" => "ja",
                    _ => all.OtherConfig.UiCultureInfoName,
                };
                if (uiLanguage != all.OtherConfig.UiCultureInfoName)
                {
                    all.OtherConfig.UiCultureInfoName = uiLanguage;
                    configService.Save();
                }

                i18nService.ChangeLanguage(uiLanguage);
                services.AddSingleton(i18nService);

                services.AddNavigationViewPageProvider();
                services.AddSingleton(InstanceBootstrap.Current);
                // 基础设施：存储区域、旧目录清理与临时文件，见 Docs/design/infrastructure-foundation.md
                services.AddSingleton<PendingCleanupStore>();
                services.AddSingleton<IStorageService, StorageService>();
                services.AddHostedService(sp => new LegacyDataCleaner(
                    instanceContext.IsRoot, sp.GetRequiredService<ILogger<LegacyDataCleaner>>()));
                // 临时目录按“实例类型-进程号”命名，多个实例共用 Data/Temp 时互不影响
                services.AddSingleton(sp => new TempFileService(
                    $"{instanceContext.InstanceType.ToString().ToLowerInvariant()}-{instanceContext.ProcessId}",
                    sp.GetRequiredService<ILogger<TempFileService>>()));
                services.AddSingleton<ITempFileService>(sp => sp.GetRequiredService<TempFileService>());
                services.AddSingleton<IStorageAreaProvider>(sp => sp.GetRequiredService<TempFileService>());
                services.AddHostedService(sp => sp.GetRequiredService<TempFileService>());
                services.AddSingleton<IStorageAreaProvider, ScriptUpdateBackupStorageAreaProvider>();
                services.AddSingleton<IStorageAreaProvider, InferenceCacheStorageAreaProvider>();
                services.AddSingleton<IStorageAreaProvider, AppStorageAreaProvider>();
                // WebView2：环境入口是创建环境的唯一位置，同时登记软件内网页的 Profile；云原神实例的缓存由实例存储登记
                services.AddSingleton(sp => new WebViewEnvironmentProvider(
                    instanceContext.InstanceName, sp.GetRequiredService<ILogger<WebViewEnvironmentProvider>>()));
                services.AddSingleton<IWebViewEnvironmentProvider>(sp => sp.GetRequiredService<WebViewEnvironmentProvider>());
                services.AddSingleton<IStorageAreaProvider>(sp => sp.GetRequiredService<WebViewEnvironmentProvider>());
                services.AddSingleton<IStorageAreaProvider, CloudGameStorageAreaProvider>();
                services.AddTransient<NetworkSettingsViewModel>();
                services.AddTransient<StorageManagementViewModel>();
                services.AddTransient<StorageManagementWindow>();
                services.AddSingleton<Func<StorageManagementWindow>>(sp => () => sp.GetRequiredService<StorageManagementWindow>());
                // 窗口、任务栏、Alt+Tab 与托盘共用同一份实例视觉身份。
                services.AddSingleton(_ => InstanceVisualIdentity.Create(
                    instanceContext,
                    $"BetterGI · 更好的原神 · {Global.Version}{(RuntimeHelper.IsDebug ? " · Dev" : string.Empty)}"));
                // 独立任务：启动时扫描主程序集中带 [SoloTaskInfo] 的任务类
                services.AddSingleton(_ => new SoloTaskRegistry([typeof(App).Assembly]));
                services.AddSingleton<SoloTaskExecutionService>();
                services.AddSingleton<InstanceService>();
                services.AddHostedService(sp => sp.GetRequiredService<InstanceService>());
                // 外部访问服务：协议适配层统一复用同一能力服务，仅 Primary 实例实际监听端口。
                services.AddSingleton<ExternalAccessState>();
                services.AddSingleton<ExternalAccessTokenService>();
                services.AddSingleton<IBgiExternalCapabilityService, BgiExternalCapabilityService>();
                services.AddSingleton<ExternalLogWebSocketHandler>();
                services.AddSingleton<ExternalAccessHost>();
                services.AddHostedService(sp => sp.GetRequiredService<ExternalAccessHost>());
                // App Host
                services.AddHostedService<ApplicationHostService>();
                // Page resolver service
                services.AddSingleton<INavigationService, NavigationService>();
                services.AddSingleton<IUpdateService, UpdateService>();
                services.AddSingleton<ISnackbarService, SnackbarService>();

                // Main window with navigation
                services.AddView<INavigationWindow, MainWindow, MainWindowViewModel>();
                services.AddSingleton<NotifyIconViewModel>();
                services.AddSingleton<ChildSessionService>();
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
                services.AddView<PuloniaTaskPlanPage, PuloniaTaskPlanViewModel>();
                services.AddView<PuloniaOneDragonPage, PuloniaOneDragonViewModel>();
                services.AddView<PuloniaTaskHistoryPage, PuloniaTaskHistoryViewModel>();
                services.AddView<AccountRecordsPage, AccountRecordsPageViewModel>();
                services.AddSingleton<InventoryCatalog>();
                services.AddSingleton<InventoryStore>();
                services.AddSingleton<InventoryIconCache>();
                services.AddTransient<AccountRecordsInventoryViewModel>();
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

                // 遮罩窗口：业务侧只依赖 IMaskWindowDrawingBoard / IMaskWindowHost / IMaskWindowMapState
                services.AddSingleton<MaskWindowDrawingBoard>();
                services.AddSingleton<IMaskWindowDrawingBoard>(sp => sp.GetRequiredService<MaskWindowDrawingBoard>());
                services.AddSingleton<IMaskWindowSnapshotSource<MaskWindowDrawingSnapshot>>(sp => sp.GetRequiredService<MaskWindowDrawingBoard>());
                services.AddSingleton<MaskWindowMapState>();
                services.AddSingleton<IMaskWindowMapState>(sp => sp.GetRequiredService<MaskWindowMapState>());
                services.AddSingleton<IMaskWindowSnapshotSource<MaskWindowMapSnapshot>>(sp => sp.GetRequiredService<MaskWindowMapState>());
                services.AddSingleton<MaskWindowViewModel>();
                services.AddTransient<MaskWindow>();
                services.AddSingleton<Func<MaskWindow>>(sp => () => sp.GetRequiredService<MaskWindow>());
                services.AddSingleton<IMaskWindowHost, MaskWindowHost>();
                services.AddSingleton<DialogueOptionVoiceDiagnosticState>();
                services.AddSingleton<DialogueOptionVoiceDiagnosticService>();
                services.AddHostedService(sp => sp.GetRequiredService<DialogueOptionVoiceDiagnosticService>());
                services.AddSingleton<TaskTriggerDispatcher>();
                services.AddSingleton<GameScreenshotService>();
                services.AddSingleton<TaskStopService>();
                services.AddSingleton<WindowsNotificationSuppressionService>();
                // 游戏运行环境：按实例类型选定 Provider，见 Docs/design/game-runtime.md
                services.AddSingleton<Win32RuntimeProvider>();
                services.AddSingleton<IGameRuntimeProvider>(sp => sp.GetRequiredService<Win32RuntimeProvider>());
                services.AddSingleton<WebPageRuntimeProvider>();
                services.AddSingleton<IGameRuntimeProvider>(sp => sp.GetRequiredService<WebPageRuntimeProvider>());
                services.AddTransient<CloudWebHostWindow>();
                services.AddSingleton<Func<CloudWebHostWindow>>(sp => () => sp.GetRequiredService<CloudWebHostWindow>());
                services.AddSingleton<GameRuntimeService>();
                services.AddSingleton<BetterGenshinImpact.Core.Recorder.KeyMouseRecordService>();
                // 云原神网页版实例：实例名存储与启动器（Primary 首页的管理窗口使用）
                services.AddSingleton<WebViewInstanceStore>();
                services.AddSingleton<WebViewInstanceLauncher>();
                services.AddTransient<CloudWebInstanceWindowViewModel>();
                services.AddTransient<CloudWebInstanceWindow>();
                services.AddSingleton<Func<CloudWebInstanceWindow>>(sp => () => sp.GetRequiredService<CloudWebInstanceWindow>());
                services.AddSingleton<RecognitionTemplateAssetService>();
                services.AddSingleton<RecognitionTemplateEditorService>();
                services.AddSingleton<NotificationService>();
                services.AddHostedService(sp => sp.GetRequiredService<NotificationService>());
                services.AddSingleton<NotifierManager>();
                services.AddSingleton<IScriptService, ScriptService>();
                // Pulonia：纯任务保持无游戏依赖；游戏能力通过统一协调器按需准备会话和输入所有权。
                services.AddSingleton<PuloniaTaskStore>();
                services.AddSingleton<PuloniaTaskBuilder>();
                services.AddSingleton<PuloniaTaskClipboardService>();
                services.AddSingleton<PuloniaCSharpTaskRegistry>();
                services.AddSingleton<PuloniaGameTaskCoordinator>();
                services.AddSingleton<IPuloniaGenshinAccountSwitcher, PuloniaGenshinAccountSwitcher>();
                services.AddSingleton<IPuloniaTaskExecutor, PuloniaCSharpTaskExecutor>();
                services.AddSingleton<IPuloniaTaskExecutor, PuloniaShellTaskExecutor>();
                services.AddSingleton<IPuloniaTaskExecutor, PuloniaPathingTaskExecutor>();
                services.AddSingleton<IPuloniaTaskExecutor, PuloniaJavaScriptTaskExecutor>();
                services.AddSingleton<IPuloniaTaskExecutor, PuloniaKeyMouseTaskExecutor>();
                services.AddSingleton<IPuloniaTaskExecutor, PuloniaSoloTaskExecutor>();
                services.AddSingleton<PuloniaTaskResourceCatalog>();
                services.AddSingleton(_ => BetterGenshinImpact.Core.Script.Repositories.ScriptRepositoryStore.Shared);
                services.AddSingleton<PuloniaRepositoryManagementService>();
                // Pulonia 跨实例派发：主实例经派发代理把节点交给子实例，子实例用节点宿主执行。
                // 两者都在首次使用时才取得多实例服务，避免与其构造互相依赖
                services.AddSingleton(sp => new PuloniaRemoteExecutionBroker(() => sp.GetRequiredService<InstanceService>()));
                services.AddSingleton<IPuloniaRemoteNodeDispatcher>(sp => sp.GetRequiredService<PuloniaRemoteExecutionBroker>());
                services.AddSingleton(sp => new PuloniaRemoteNodeHost(
                    () => sp.GetRequiredService<InstanceService>(),
                    sp.GetServices<IPuloniaTaskExecutor>(),
                    sp.GetRequiredService<PuloniaGameTaskCoordinator>(),
                    sp.GetRequiredService<TaskStopService>(),
                    sp.GetRequiredService<ILogger<PuloniaRemoteNodeHost>>(),
                    sp.GetService<IPuloniaGenshinAccountSwitcher>()));
                // 只有主实例是协调器：子实例不读写共享的 User/Pulonia/state.json，也不消费队列。
                // 调度宿主与页面都依赖接口，只注册这一份单例；容器关闭时释放它（IAsyncDisposable）。
                services.AddSingleton<IPuloniaTaskService>(sp => new PuloniaTaskService(
                    sp.GetRequiredService<PuloniaTaskStore>(),
                    sp.GetRequiredService<PuloniaTaskBuilder>(),
                    sp.GetServices<IPuloniaTaskExecutor>(),
                    sp.GetRequiredService<PuloniaGameTaskCoordinator>(),
                    sp.GetRequiredService<TaskStopService>(),
                    sp.GetRequiredService<TimeProvider>(),
                    sp.GetService<IPuloniaGenshinAccountSwitcher>(),
                    sp.GetRequiredService<IPuloniaRemoteNodeDispatcher>(),
                    sp.GetRequiredService<InstanceBootstrap>().Context.IsRoot,
                    sp.GetRequiredService<ILogger<PuloniaTaskService>>()));
                services.AddSingleton(TimeProvider.System);
                services.AddSingleton<PuloniaTaskTriggerHost>();
                services.AddHostedService(sp => sp.GetRequiredService<PuloniaTaskTriggerHost>());
                // Pulonia 任务计划页与新一条龙页共用同一编辑会话；共享设置、路线 CD 与运行预览窗口由视图层提供。
                services.AddSingleton<BetterGenshinImpact.ViewModel.Pages.Pulonia.PuloniaPlanEditingSession>();
                // 路线 CD 总览：任务计划页与全局执行记录页各持一个实例，筛选状态互不影响。
                services.AddTransient<BetterGenshinImpact.ViewModel.Pages.Pulonia.PuloniaCollectionCooldownOverviewViewModel>();
                services.AddSingleton<BetterGenshinImpact.ViewModel.Pages.Pulonia.IPuloniaTaskSettingsDialogs,
                    BetterGenshinImpact.View.Windows.Pulonia.PuloniaTaskSettingsDialogs>();
                // 树脂设置公共弹窗；各宿主页面在后续接入时只依赖视图模型层接口。
                services.AddSingleton<BetterGenshinImpact.ViewModel.Resin.IResinSettingsDialogs, ResinSettingsDialogs>();
                // 体力计划子任务设置弹窗；设置视图模型只依赖接口，隔离环境下不打开窗口。
                services.AddSingleton<BetterGenshinImpact.ViewModel.SoloTaskSettings.IResinPlanSettingsDialogs, ResinPlanSettingsDialogs>();
                services.AddSingleton<IMusicScoreParser, MusicScoreParser>();
                services.AddSingleton<IMusicStateStore, MusicStateStore>();
                services.AddSingleton<IInstrumentProfileService, InstrumentProfileService>();
                services.AddSingleton<IMusicTimelineBuilder, MusicTimelineBuilder>();
                services.AddSingleton<IMusicLibraryService, MusicLibraryService>();
                services.AddSingleton<IMusicCoverService, MusicCoverService>();
                services.AddSingleton<IMusicInstrumentSwitcher, MusicInstrumentSwitcher>();
                services.AddSingleton<IKeyInputTransport, PostMessageKeyInputTransport>();
                services.AddSingleton<IKeyInputTransport, SendInputKeyInputTransport>();
                services.AddSingleton<IMusicPlaybackService, MusicPlaybackService>();
                services.AddSingleton<BgiOnnxFactory>();
                services.AddSingleton<OcrFactory>();
                services.AddMemoryCache();
                services.AddSingleton<IAppCache, CachingService>();
                // 文件缓存与远程图片，见 Docs/design/infrastructure-foundation.md 第 5 节
                services.AddSingleton(sp => new FileCache(AppPaths.FileCache, sp.GetRequiredService<TimeProvider>(),
                    sp.GetRequiredService<ILogger<FileCache>>()));
                services.AddSingleton<IFileCache>(sp => sp.GetRequiredService<FileCache>());
                services.AddSingleton<IStorageAreaProvider>(sp => sp.GetRequiredService<FileCache>());
                services.AddHostedService(sp => new CacheMaintenanceService(
                    sp.GetRequiredService<FileCache>(), all.StorageConfig, instanceContext.IsRoot,
                    sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILogger<CacheMaintenanceService>>()));
                services.AddSingleton<IRemoteImageService, RemoteImageService>();
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

    /// <summary>
    /// 异常与退出阶段直接使用应用拥有的 Serilog，不向可能已释放的 DI 容器索取日志服务。
    /// </summary>
    private static readonly ExceptionLogWriter _exceptionLog = new(() => Log.ForContext<App>());

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
            if (InstanceBootstrap.Current?.Context.IsRoot == true)
            {
                // 推迟到本次启动的清理（推理缓存、网页数据等）必须在使用方加载之前执行
                TryExecutePendingCleanup();
            }

            var inferenceRuntime = _host.Services.GetRequiredService<InferenceRuntimeService>();
            // 此处只准备设备配置；模型验证和提示由主实例的 MainWindowViewModel.OnLoaded 执行。
            await inferenceRuntime.InitializeAsync();
            // 主窗口在宿主启动时创建；先在 UI 线程注册独立任务设置界面的隐式模板
            SoloTaskSettingsTemplates.Register(Current.Resources, App.GetService<SoloTaskRegistry>()!);
            await _host.StartAsync();
            ServerTimeHelper.Initialize(_host.Services.GetRequiredService<IServerTimeProvider>());
            if (InstanceBootstrap.Current?.Context.IsRoot == true)
            {
                // 宿主启动完成后实例上下文已初始化；主实例尽力清理遗留锁，失败也不影响启动。
                TryCleanupPuloniaLockFiles();
                await UrlProtocolHelper.RegisterAsync();
            }
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
                        $"应用程序启动失败：{ex.Message}",
                        "BetterGI 启动失败",
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

    /// <summary>尽力执行上次登记、推迟到本次启动的存储清理；失败时只记录日志，不影响 BetterGI 启动。</summary>
    private static void TryExecutePendingCleanup()
    {
        try
        {
            _host.Services.GetRequiredService<PendingCleanupStore>().ExecutePending();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "执行推迟的存储清理失败，已跳过且不影响程序启动。");
        }
    }

    /// <summary>尽力清理 Pulonia 遗留锁；失败时只记录日志，不影响 BetterGI 启动。</summary>
    private static void TryCleanupPuloniaLockFiles()
    {
        try
        {
            // 主实例清理空闲锁，其他实例正在持有的活跃锁会因无法独占打开而保留。
            var removedLockCount = BetterGenshinImpact.Core.Script.Repositories.ScriptRepositoryStore.Shared
                .CleanupStaleLockFiles();
            if (removedLockCount > 0)
                Log.Information("已清理 {LockFileCount} 个 Pulonia 遗留锁文件。", removedLockCount);
        }
        catch (Exception ex)
        {
            try
            {
                Debug.WriteLine(ex);
                Log.Warning(ex, "清理 Pulonia 遗留锁文件失败，已跳过且不影响程序启动。");
            }
            catch
            {
                // 启动早期日志组件也可能不可用，锁清理失败必须保持完全无害。
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

        // 先通知当前运行退出，再停止宿主服务，避免任务在依赖项释放后继续执行。
        _host.Services.GetService<TaskStopService>()?.StopAll(TaskStopReason.ApplicationShutdown);

        // 写入防抖窗口内尚未落盘的配置改动
        try
        {
            _host.Services.GetService<IConfigService>()?.Flush();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }

        // 临时文件由 TempFileService 在宿主停止时删除

        try
        {
            await _host.Services.GetRequiredService<InferenceRuntimeService>().StopAsync();
            await _host.Services.GetRequiredService<OnnxRuntimeInstaller>().StopAsync();
            await _host.Services.GetRequiredService<InferenceModelDiagnostics>().StopAsync();
            await _host.StopAsync();
        }
        catch (Exception ex)
        {
            _exceptionLog.Error(ex, "停止应用宿主时发生异常。");
        }
        finally
        {
            try
            {
                _host.Dispose();
            }
            catch (Exception ex)
            {
                // 服务释放异常发生时容器可能已经失效，不能再调用 GetLogger。
                _exceptionLog.Error(ex, "释放应用宿主时发生异常。");
            }
            finally
            {
                try
                {
                    Log.CloseAndFlush();
                }
                catch (Exception ex)
                {
                    _exceptionLog.Error(ex, "关闭与刷新日志时发生异常。");
                }
                finally
                {
                    // 释放控制台窗口
                    ConsoleHelper.FreeConsoleWindow();
                }
            }
        }
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
            _exceptionLog.Error(e.Exception, "原始未处理任务异常。");
            _exceptionLog.Error(ex, "处理任务异常时再次发生异常。");
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
            if (e.ExceptionObject is Exception original)
                _exceptionLog.Error(original, "原始未处理线程异常。");
            _exceptionLog.Error(ex, "处理线程异常时再次发生异常。");
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
            _exceptionLog.Error(e.Exception, "原始未处理 UI 异常。");
            _exceptionLog.Error(ex, "处理 UI 异常时再次发生异常。");
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
        // 错误日志最先落盘并推送到日志遮罩（LogError ≥ Information 会进入遮罩 LogTextBox）。
        // 文件日志：Debug 级别也写盘；遮罩：仅 Information 以上可见。
        // 致命异常（IsTerminating）在日志末尾加 [FATAL] 标记，便于区分。
        var logMessage = isTerminating ? "UnHandle Exception [FATAL]" : "UnHandle Exception";
        // 保留外层堆栈和全部 InnerException，不让异常处理自己的生命周期错误覆盖原始错误。
        _exceptionLog.Error(e, logMessage);

        // 可恢复异常（默认）：仅日志，不弹模态窗，避免阻塞 UI 线程。
        // 通过日志遮罩提示用户：非致命异常已记录。
        if (!isTerminating)
        {
            const string nonFatalMessage = "发生非致命异常，已记录日志，请查看日志详情。";
            _exceptionLog.Warning(nonFatalMessage);
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
        const string popupShownMessage = "发生致命异常，正在弹窗提示，同时已记录日志。";
        _exceptionLog.Warning(popupShownMessage);

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
                    _exceptionLog.Warning("弹窗调度超时，异常已记录，进程即将退出。");
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
