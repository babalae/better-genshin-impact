using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Mask;
using BetterGenshinImpact.Core.Monitor;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.GameTask.Runtime.Win32;
using BetterGenshinImpact.Genshin.Paths;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Extensions;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.ChildSession;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using BetterGenshinImpact.Service.Worker;
using BetterGenshinImpact.View;
using BetterGenshinImpact.View.Controls.Markdown;
using BetterGenshinImpact.View.Controls.Webview;
using BetterGenshinImpact.View.Pages.View;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Pages.View;
using BetterGenshinImpact.ViewModel.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Fischless.GameCapture;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System;
using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.System;
using Wpf.Ui.Controls;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class HomePageViewModel : ViewModel, IDisposable
{
    private bool _disposed;
    [ObservableProperty] private IEnumerable<EnumItem<CaptureModes>> _modeNames = EnumExtensions.ToEnumItems<CaptureModes>();

    [ObservableProperty] private string? _selectedMode = CaptureModes.BitBlt.ToString();

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsTriggerButtonChecked))]
    private bool _taskDispatcherEnabled = false;

    /// <summary>
    /// 正在获取运行环境（找窗、关联启动游戏）
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTriggerButtonChecked))]
    private bool _isRuntimeStarting;

    /// <summary>
    /// 已连接 Worker 时：Worker 的截图器或任务正在运行
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTriggerButtonChecked))]
    private bool _isWorkerTriggerRunning;

    /// <summary>
    /// 启动按钮显示为"停止"：本机运行中（或正在启动）；
    /// 已连接 Worker 时改为跟随 Worker 侧状态（此时按钮点击提交的是 Worker 的启停请求）
    /// </summary>
    public bool IsTriggerButtonChecked => _workerController.IsConnected
        ? IsWorkerTriggerRunning
        : TaskDispatcherEnabled || IsRuntimeStarting;

    /// <summary>
    /// 「云原神网页版」卡片只在 Primary 显示
    /// </summary>
    public bool IsCloudWebEntryVisible => InstanceBootstrap.Current.Context.IsRoot;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(StartTriggerCommand))]
    private bool _startButtonEnabled = true;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(StopTriggerCommand))]
    private bool _stopButtonEnabled = true;

    public AllConfig Config { get; set; }

    public bool IsChildSessionEntryVisible => InstanceBootstrap.Current.Context.IsRoot;

    private readonly IMaskWindowHost _maskWindowHost;
    private readonly CustomHtmlMaskService _customHtmlMaskService;
    private readonly ILogger<HomePageViewModel> _logger = App.GetLogger<HomePageViewModel>();

    private readonly GameRuntimeService _gameRuntimeService;
    private readonly Win32RuntimeProvider _win32RuntimeProvider;
    private readonly MouseKeyMonitor _mouseKeyMonitor = new();
    private readonly IBannerImageService _bannerImageService;
    private CancellationTokenSource? _bannerDownloadCancellationTokenSource;

    [ObservableProperty] private InferenceDeviceType[] _inferenceDeviceTypes = Enum.GetValues<InferenceDeviceType>();

    [ObservableProperty] private ImageSource _bannerImageSource;

    private const string DefaultBannerImagePath = "pack://application:,,,/Resources/Images/banner.jpg";
    private readonly string _customBannerImagePath = Global.Absolute("User/Images/custom_banner.jpg");
    [ObservableProperty]
    private bool _isCustomNetworkBanner = false;
    private readonly ChildSessionService _childSessionService;
    private readonly WebViewInstanceStore _webViewInstanceStore;
    private readonly WebViewInstanceLauncher _webViewInstanceLauncher;
    private readonly WorkerController _workerController;

    /// <summary>
    /// 已连接 Worker 时定时刷新 worker.status
    /// </summary>
    private readonly DispatcherTimer _workerStatusTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _workerStatusRefreshing;

    public HomePageViewModel(
        IConfigService configService,
        GameRuntimeService gameRuntimeService,
        Win32RuntimeProvider win32RuntimeProvider,
        ChildSessionService childSessionService,
        IBannerImageService bannerImageService,
        WebViewInstanceStore webViewInstanceStore,
        WebViewInstanceLauncher webViewInstanceLauncher,
        IMaskWindowHost maskWindowHost,
        CustomHtmlMaskService customHtmlMaskService,
        WorkerController workerController)
    {
        _gameRuntimeService = gameRuntimeService;
        _win32RuntimeProvider = win32RuntimeProvider;
        _maskWindowHost = maskWindowHost;
        _customHtmlMaskService = customHtmlMaskService;
        _gameRuntimeService.Started += OnRuntimeStarted;
        _gameRuntimeService.Stopped += OnRuntimeStopped;
        _gameRuntimeService.StartingChanged += OnRuntimeStartingChanged;
        _webViewInstanceStore = webViewInstanceStore;
        _webViewInstanceLauncher = webViewInstanceLauncher;
        _workerController = workerController;
        _workerController.Disconnected += OnWorkerDisconnected;
        _workerController.StatusChanged += OnWorkerStatusChanged;
        _workerStatusTimer.Tick += OnWorkerStatusTimerTick;
        _childSessionService = childSessionService;
        _bannerImageService = bannerImageService;
        Config = configService.Get();
        // 上次使用的 Worker SID 自动回填，避免每次手动输入
        WorkerSidInput = Config.OtherConfig.LastWorkerUserSid;
        // 本地原神的安装目录与网页版无关，网页版实例不去读注册表，也不写共享配置
        if (!InstanceBootstrap.Current.Context.IsWebView)
        {
            ReadGameInstallPath();
        }

        // 无界面 Worker 没有启动页，不加载背景图片
        if (!InstanceBootstrap.Current.Context.IsHeadless)
        {
            InitializeBannerImage();
        }


        // WindowsGraphicsCapture 只支持 Win10 18362 及以上的版本 (Windows 10 version 1903 or later)
        // https://github.com/babalae/better-genshin-impact/issues/394
        if (!OsVersionHelper.IsWindows10_1903_OrGreater)
        {
            // 删除 _modeNames 中的 CaptureModes.WindowsGraphicsCapture / WindowsGraphicsCaptureV2
            // （V2 帧池用 CreateFreeThreaded，同样要求 1903+）
            _modeNames = _modeNames
                .Where(x => x.EnumName != CaptureModes.WindowsGraphicsCapture.ToString()
                         && x.EnumName != CaptureModes.WindowsGraphicsCaptureV2.ToString())
                .ToList();

            // DirectML 是在 Windows 10 版本 1903 和 Windows SDK 的相应版本中引入的。
            // https://learn.microsoft.com/zh-cn/windows/ai/directml/dml
            _inferenceDeviceTypes = _inferenceDeviceTypes
                .Where(x => x != InferenceDeviceType.GpuDirectMl)
                .ToArray();
        }

        WeakReferenceMessenger.Default.Register<PropertyChangedMessage<object>>(this, (sender, msg) =>
        {
            if (msg.PropertyName == "Close")
            {
                OnClosed();
            }
            else if (msg.PropertyName == "SwitchTriggerStatus")
            {
                // 启动中也按"停止"处理：本次启动结束后不再绑定
                if (IsTriggerButtonChecked)
                {
                    _ = OnStopTrigger();
                }
                else
                {
                    _ = OnStartTriggerAsync();
                }
            }
        });
    }

    private bool _autoRun = true;

    [RelayCommand]
    private void OpenChildSessionWindow()
    {
        _childSessionService.ShowWindow();
    }

    [RelayCommand]
    private void OnLoaded()
    {
        // OnTest();

        // 每次进入首页都刷新网页版实例的运行状态
        if (IsCloudWebEntryVisible)
        {
            RefreshCloudWebInstances();
        }

        // WorkerController 是单例，页面重建后连接可能仍然存在，这里同步一次显示状态
        SyncWorkerStateFromController();

        // 组件首次加载时运行一次。
        if (!_autoRun)
        {
            return;
        }

        _autoRun = false;

        // 只对纯 "start" 参数自动启动截图器
        // startOneDragon、--startGroups 等由各自流程中的 StartGameTask 处理
        HandleActivation(CommandLineOptions.Instance);
    }

    public void HandleActivation(CommandLineOptions commandLineOptions)
    {
        var context = InstanceBootstrap.Current.Context;

        // 无界面 Worker 没有主界面，启动后总是启动截图器：遮罩窗口是它唯一的界面。
        // 网页版实例没有主界面，启动后总是打开宿主窗口并等待绑定（见 ApplicationHostService.HandleWebViewActivation）
        if (context.IsHeadless)
        {
            _ = StartHeadlessRuntimeAsync();
        }
        else if (commandLineOptions.Action == CommandLineAction.Start || context.IsWebView)
        {
            _ = OnStartTriggerAsync();
        }

        // TODO: 多实例独立任务选择面板入口预留。
        // 后续在此判断可用子实例，并由选择面板决定 task.* 请求的目标实例。
    }

    /// <summary>
    /// 无界面 Worker 启动截图器。没有界面可以提示，失败只记录日志，不影响 Worker 继续等待指令。
    /// </summary>
    private async Task StartHeadlessRuntimeAsync()
    {
        try
        {
            await _gameRuntimeService.StartAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "无界面 Worker 启动截图器失败");
        }
    }

    private void OnClosed()
    {
        CancelBannerDownload();
        // 已连接 Worker 时不动 Worker 侧：关闭启动页不代表要停掉远端任务
        if (!_workerController.IsConnected)
        {
            _ = OnStopTrigger();
        }

        // 等待任务结束
        _maskWindowHost.Close();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        OnClosed();
        _workerStatusTimer.Stop();
        _workerStatusTimer.Tick -= OnWorkerStatusTimerTick;
        _gameRuntimeService.Started -= OnRuntimeStarted;
        _gameRuntimeService.Stopped -= OnRuntimeStopped;
        _gameRuntimeService.StartingChanged -= OnRuntimeStartingChanged;
        _workerController.Disconnected -= OnWorkerDisconnected;
        _workerController.StatusChanged -= OnWorkerStatusChanged;
        WeakReferenceMessenger.Default.UnregisterAll(this);
        _mouseKeyMonitor.Dispose();
        GC.SuppressFinalize(this);
    }

    [RelayCommand]
    private async Task OnCaptureModeDropDownChanged()
    {
        // 启动的情况下重启
        if (TaskDispatcherEnabled)
        {
            _logger.LogInformation("► 切换捕获模式至[{Mode}]，截图器自动重启...", Config.CaptureMode);
            await OnStopTrigger();
            await OnStartTriggerAsync();
        }
    }

    // [RelayCommand]
    // private void OnInferenceDeviceTypeDropDownChanged(string value)
    // {
    // }

    [RelayCommand]
    private void OnStartCaptureTest()
    {
        var picker = new PickerWindow(true);

        if (picker.PickCaptureTarget(new WindowInteropHelper(UIDispatcherHelper.MainWindow).Handle, out var hWnd))
        {
            if (hWnd != IntPtr.Zero)
            {
                var captureWindow = new CaptureTestWindow();
                captureWindow.StartCapture(hWnd, Config.CaptureMode.ToCaptureMode());
                captureWindow.Show();
            }
            else
            {
                ThemedMessageBox.Error("选择的窗体句柄为空");
            }
        }
    }

    [RelayCommand]
    private void OnManualPickWindow()
    {
        var picker = new PickerWindow();
        if (picker.PickCaptureTarget(new WindowInteropHelper(UIDispatcherHelper.MainWindow).Handle, out var hWnd))
        {
            if (hWnd != IntPtr.Zero)
            {
                // 已在运行时与改造前一致：不做任何事
                if (!_gameRuntimeService.IsRunning)
                {
                    _gameRuntimeService.Start(_win32RuntimeProvider.AttachTo(hWnd));
                }
            }
            else
            {
                ThemedMessageBox.Error("选择的窗体句柄为空！");
            }
        }
    }

    [RelayCommand]
    private async Task OpenDisplayAdvancedGraphicsSettingsAsync()
    {
        // ms-settings:display
        // ms-settings:display-advancedgraphics
        // ms-settings:display-advancedgraphics-default
        await Launcher.LaunchUriAsync(new Uri("ms-settings:display-advancedgraphics"));
    }

    private bool CanStartTrigger() => StartButtonEnabled;

    /// <summary>
    /// 启动截图器。找窗、关联启动、HDR 处理都由运行环境的 Provider 完成。
    /// 已连接跨用户 Worker 时改为启动 Worker 的截图器（本机截图器禁止启动）
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStartTrigger))]
    public async Task OnStartTriggerAsync()
    {
        if (_workerController.IsConnected)
        {
            await ExecuteWorkerActionAsync(
                async () => ApplyWorkerStatus(await _workerController.StartCaptureAsync()),
                "启动 Worker 截图器失败");
            return;
        }

        await _gameRuntimeService.StartAsync();
    }

    private bool CanStopTrigger() => StopButtonEnabled;

    [RelayCommand(CanExecute = nameof(CanStopTrigger))]
    private async Task OnStopTrigger()
    {
        if (_workerController.IsConnected)
        {
            await ExecuteWorkerActionAsync(
                async () => ApplyWorkerStatus(await _workerController.StopCaptureAsync()),
                "停止 Worker 截图器失败");
            return;
        }

        await _gameRuntimeService.StopAsync();
    }

    /// <summary>
    /// 运行环境绑定完成（UI 线程）：显示遮罩；仅 Win32 实例订阅键鼠监听
    /// </summary>
    private void OnRuntimeStarted(object? sender, EventArgs e)
    {
        var handle = _gameRuntimeService.Current!.Window.Handle;
        _maskWindowHost.Attach(handle);
        _customHtmlMaskService.ShowIfEnabled();
        if (_gameRuntimeService.Kind != GameRuntimeKind.WebPage)
        {
            _mouseKeyMonitor.Subscribe(handle);
        }
        TaskDispatcherEnabled = true;
        PrintSystemInfo();
    }

    /// <summary>
    /// 截图器启动后输出运行环境信息，并检查常见的识别干扰项
    /// </summary>
    private void PrintSystemInfo()
    {
        _logger.LogInformation("更好的原神 {Version}", Global.Version);
        var systemInfo = TaskContext.Instance().SystemInfo;
        var width = systemInfo.GameScreenSize.Width;
        var height = systemInfo.GameScreenSize.Height;
        var dpiScale = TaskContext.Instance().DpiScale;
        _logger.LogInformation("遮罩窗口已启动，游戏大小{Width}x{Height}，素材缩放{Scale}，DPI缩放{Dpi}",
            width, height, systemInfo.AssetScale.ToString("F"), dpiScale);

        if (width * 9 != height * 16)
        {
            _logger.LogError("当前游戏分辨率不是16:9，一条龙、配队识别、地图传送、地图追踪等所有独立任务与全自动任务相关功能，都将会无法正常使用！");
        }

        // MSIAfterburner.exe 在左上角会导致识别失败
        if (Process.GetProcessesByName("MSIAfterburner").Length > 0)
        {
            _logger.LogWarning("检测到 MSI Afterburner 正在运行，如果信息位于特定UI上遮盖图像识别要素可能导致识别失败，请关闭MSI Afterburner 或者调整信息位置后重试！");
        }

        // 读取游戏注册表配置。网页版的游戏设置保存在云端，本机注册表里的是本地原神的设置，检查结果会误导用户
        if (_gameRuntimeService.Kind != GameRuntimeKind.WebPage)
        {
            Genshin.Settings2.GameSettingsChecker.LoadGameSettingsAndCheck();
        }
    }

    /// <summary>
    /// 运行环境解绑完成（UI 线程）：隐藏遮罩、取消键鼠监听
    /// </summary>
    private void OnRuntimeStopped(object? sender, EventArgs e)
    {
        _maskWindowHost.Detach();

        TaskDispatcherEnabled = false;
        _mouseKeyMonitor.Unsubscribe();
    }

    private void OnRuntimeStartingChanged(object? sender, EventArgs e)
    {
        IsRuntimeStarting = _gameRuntimeService.IsStarting;
    }

    #region 云原神网页版实例（Primary 首页）

    [ObservableProperty]
    private ObservableCollection<CloudWebInstanceItem> _cloudWebInstances = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LaunchCloudWebInstanceCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCloudWebInstanceCommand))]
    private CloudWebInstanceItem? _selectedCloudWebInstance;

    /// <summary>
    /// 重新读取实例列表与运行状态，尽量保持当前选中项
    /// </summary>
    [RelayCommand]
    private void RefreshCloudWebInstances()
    {
        var selectedName = SelectedCloudWebInstance?.Name;
        var items = _webViewInstanceStore.List()
            .Select(name => new CloudWebInstanceItem(name, WebViewInstanceStore.IsRunning(name)))
            .ToList();
        CloudWebInstances = new ObservableCollection<CloudWebInstanceItem>(items);
        SelectedCloudWebInstance = items.FirstOrDefault(i => string.Equals(i.Name, selectedName, StringComparison.OrdinalIgnoreCase))
                                   ?? items.FirstOrDefault();
    }

    [RelayCommand]
    private void CreateCloudWebInstance()
    {
        var name = PromptDialog.Prompt(
            $"实例名用于区分不同账号，同时作为该实例 WebView 数据（登录态等）的目录名。\n最多 {WebViewInstanceStore.MaxNameLength} 个字符，不能包含 \\ / : * ? \" < > |",
            "新建云原神网页版实例");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            var created = _webViewInstanceStore.Create(name);
            RefreshCloudWebInstances();
            SelectedCloudWebInstance = CloudWebInstances.FirstOrDefault(i => i.Name == created);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            ThemedMessageBox.Warning(ex.Message, "新建实例失败");
        }
    }

    private bool CanOperateCloudWebInstance() => SelectedCloudWebInstance is { IsRunning: false };

    [RelayCommand(CanExecute = nameof(CanOperateCloudWebInstance))]
    private async Task LaunchCloudWebInstanceAsync()
    {
        var item = SelectedCloudWebInstance;
        if (item == null)
        {
            return;
        }

        try
        {
            _webViewInstanceLauncher.Launch(item.Name);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            ThemedMessageBox.Warning(ex.Message, "启动实例失败");
            RefreshCloudWebInstances();
            return;
        }

        // 新进程获取实例名互斥体需要一点时间，稍后再刷新运行状态
        await Task.Delay(3000);
        RefreshCloudWebInstances();
    }

    [RelayCommand(CanExecute = nameof(CanOperateCloudWebInstance))]
    private async Task DeleteCloudWebInstanceAsync()
    {
        var item = SelectedCloudWebInstance;
        if (item == null)
        {
            return;
        }

        var result = await ThemedMessageBox.QuestionAsync(
            $"确定删除实例「{item.Name}」吗？\n该实例的登录态等 WebView 数据会一并删除，且无法恢复。",
            "删除云原神网页版实例");
        if (result != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _webViewInstanceStore.Delete(item.Name);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            ThemedMessageBox.Warning(ex.Message, "删除实例失败");
        }

        RefreshCloudWebInstances();
    }

    #endregion

    #region 跨用户 Worker（Primary 首页）

    /// <summary>
    /// 跨用户 Worker 入口只在 Primary 显示
    /// </summary>
    public bool IsWorkerEntryVisible => InstanceBootstrap.Current.Context.IsRoot;

    /// <summary>
    /// 当前 Windows 用户 SID。目标用户启动 Worker 时作为 --controller-sid 的值
    /// </summary>
    public string LocalUserSid => InstanceBootstrap.Current.Context.WindowsUserSid;

    [RelayCommand]
    private void CopyLocalUserSid()
    {
        try
        {
            Clipboard.SetDataObject(LocalUserSid);
            Toast.Success("已复制本用户 SID");
        }
        catch (Exception ex)
        {
            Toast.Error($"复制 SID 失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 目标 Worker 所在 Windows 用户的 SID
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectWorkerCommand))]
    private string _workerSidInput = string.Empty;

    /// <summary>
    /// 输入的 SID 写入配置（由 ConfigService 防抖落盘），下次启动自动回填
    /// </summary>
    partial void OnWorkerSidInputChanged(string value)
    {
        Config.OtherConfig.LastWorkerUserSid = value;
    }

    /// <summary>
    /// Worker 日志（原本写入遮罩叠加层日志框的内容）的显示位置。
    /// 数值与启动页下拉框的选项顺序一一对应，见 <see cref="WorkerLogDisplayMode"/>
    /// </summary>
    public int WorkerLogDisplayModeIndex
    {
        get => (int)Config.OtherConfig.WorkerLogDisplayMode;
        set
        {
            if (value < (int)WorkerLogDisplayMode.None || value > (int)WorkerLogDisplayMode.LocalWindow)
            {
                return;
            }

            var mode = (WorkerLogDisplayMode)value;
            if (Config.OtherConfig.WorkerLogDisplayMode == mode)
            {
                return;
            }

            Config.OtherConfig.WorkerLogDisplayMode = mode;
            OnPropertyChanged();
            WarnIfWorkerLogEventNotSubscribed(mode);
            _ = PushWorkerLogDisplayModeAsync();
        }
    }

    /// <summary>
    /// 「使用通知渠道」依赖通知事件订阅：订阅列表非空但没有勾选「Worker 日志」时什么都收不到，
    /// 这里提前给出提示，免得用户以为功能没生效
    /// </summary>
    private void WarnIfWorkerLogEventNotSubscribed(WorkerLogDisplayMode mode)
    {
        if (mode != WorkerLogDisplayMode.Notification)
        {
            return;
        }

        var subscribed = NotificationEventSubscriptionHelper.ParseEventCodes(
            Config.NotificationConfig.NotificationEventSubscribe);
        if (subscribed.Count == 0
            || subscribed.Contains(NotificationEvent.WorkerLog.Code, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        Toast.Warning("通知设置里没有勾选「Worker 日志」事件，Worker 日志不会推送");
    }

    /// <summary>
    /// 「使用通知渠道」时日志的聚合间隔（秒）。单位时间内的日志合并成一条通知，避免被渠道限流。
    /// 用 double 承接 NumberBox 的 Value，避免 int↔double 的绑定转换问题
    /// </summary>
    public double WorkerLogNotificationIntervalSeconds
    {
        get => Config.OtherConfig.WorkerLogNotificationIntervalSeconds;
        set
        {
            var normalized = value < 1 ? 1 : (int)Math.Round(value);
            if (Config.OtherConfig.WorkerLogNotificationIntervalSeconds == normalized)
            {
                return;
            }

            Config.OtherConfig.WorkerLogNotificationIntervalSeconds = normalized;
            OnPropertyChanged();
            _ = PushWorkerLogDisplayModeAsync();
        }
    }

    /// <summary>
    /// 把显示位置下发给 Worker。未连接时只落盘，连接成功后由 <see cref="ConnectWorkerAsync"/> 补推
    /// </summary>
    private async Task PushWorkerLogDisplayModeAsync()
    {
        if (!_workerController.IsConnected)
        {
            return;
        }

        try
        {
            var response = await _workerController.SetLogDisplayModeAsync(
                Config.OtherConfig.WorkerLogDisplayMode,
                Config.OtherConfig.WorkerLogNotificationIntervalSeconds);
            _logger.LogInformation("Worker 日志显示位置已下发：{Mode}", response.Mode);
            // 「本地独立窗口」由本机显示：先开窗口，用户马上能看到效果（Worker 的日志随后到达）
            App.GetService<WorkerLogWindowService>()?.HandleModeChanged(response.Mode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "下发 Worker 日志显示位置失败");
            Toast.Warning($"设置 Worker 日志显示位置失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 是否已连接 Worker。连接后本机截图器禁止启动，主按钮改为控制 Worker
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTriggerButtonChecked))]
    [NotifyCanExecuteChangedFor(nameof(ConnectWorkerCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectWorkerCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshWorkerStatusCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartWorkerGameCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopWorkerTaskCommand))]
    [NotifyCanExecuteChangedFor(nameof(PauseWorkerTaskCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResumeWorkerTaskCommand))]
    private bool _isWorkerConnected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectWorkerCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshWorkerStatusCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartWorkerGameCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopWorkerTaskCommand))]
    [NotifyCanExecuteChangedFor(nameof(PauseWorkerTaskCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResumeWorkerTaskCommand))]
    private bool _isWorkerBusy;

    /// <summary>
    /// Worker 侧状态文本（状态、进程、会话与当前任务）
    /// </summary>
    [ObservableProperty]
    private string _workerStatusText = "未连接";

    private bool CanConnectWorker() => !IsWorkerConnected && !IsWorkerBusy;

    [RelayCommand(CanExecute = nameof(CanConnectWorker))]
    private async Task ConnectWorkerAsync()
    {
        await ExecuteWorkerActionAsync(async () =>
        {
            ApplyWorkerStatus(await _workerController.ConnectAsync(WorkerSidInput));
            IsWorkerConnected = true;
            UpdateWorkerStatusTimer();
            // 连接后立刻把当前的日志显示位置推给 Worker，否则 Worker 会按自己的配置继续跑
            await PushWorkerLogDisplayModeAsync();
        }, "连接 Worker 失败");
    }

    private bool CanDisconnectWorker() => IsWorkerConnected;

    [RelayCommand(CanExecute = nameof(CanDisconnectWorker))]
    private async Task DisconnectWorkerAsync()
    {
        await ExecuteWorkerActionAsync(async () =>
        {
            await _workerController.DisconnectAsync();
            IsWorkerConnected = false;
            IsWorkerTriggerRunning = false;
            WorkerStatusText = "未连接";
            UpdateWorkerStatusTimer();
        }, "断开 Worker 失败");
    }

    private bool CanOperateWorker() => IsWorkerConnected && !IsWorkerBusy;

    [RelayCommand(CanExecute = nameof(CanOperateWorker))]
    private async Task RefreshWorkerStatusAsync()
    {
        await ExecuteWorkerActionAsync(
            async () => ApplyWorkerStatus(await _workerController.GetStatusAsync()),
            "刷新 Worker 状态失败");
    }

    [RelayCommand(CanExecute = nameof(CanOperateWorker))]
    private async Task StartWorkerGameAsync()
    {
        await ExecuteWorkerActionAsync(
            async () => ApplyWorkerStatus(await _workerController.StartCaptureAsync()),
            "启动 Worker 截图器失败");
    }

    [RelayCommand(CanExecute = nameof(CanOperateWorker))]
    private async Task StopWorkerTaskAsync()
    {
        await ExecuteWorkerActionAsync(
            () => _workerController.StopTaskAsync(),
            "停止 Worker 任务失败");
    }

    [RelayCommand(CanExecute = nameof(CanOperateWorker))]
    private async Task PauseWorkerTaskAsync()
    {
        await ExecuteWorkerActionAsync(
            () => _workerController.PauseTaskAsync(),
            "暂停 Worker 任务失败");
    }

    [RelayCommand(CanExecute = nameof(CanOperateWorker))]
    private async Task ResumeWorkerTaskAsync()
    {
        await ExecuteWorkerActionAsync(
            () => _workerController.ResumeTaskAsync(),
            "继续 Worker 任务失败");
    }

    private void ApplyWorkerStatus(WorkerStatusResponse status)
    {
        // 主按钮（启动/停止）在已连接 Worker 时跟随 Worker 侧的截图器与任务状态
        IsWorkerTriggerRunning = status.CaptureRunning
                                 || status.State is WorkerState.Starting or WorkerState.RunningTask or WorkerState.Stopping;

        var text = $"{status.State} ｜ 截图器{(status.CaptureRunning ? "运行中" : "未启动")}"
                   + $" ｜ 进程 {status.ProcessId} ｜ 会话 {status.SessionId}";
        if (!string.IsNullOrEmpty(status.CurrentTask))
        {
            text += $" ｜ 当前任务 {status.CurrentTask}";
        }

        if (!string.IsNullOrEmpty(status.ErrorMessage))
        {
            text += $" ｜ {status.ErrorMessage}";
        }

        WorkerStatusText = text;
    }

    /// <summary>
    /// Worker 状态变化可能来自管道线程，切回 UI 线程更新
    /// </summary>
    private void OnWorkerStatusChanged(object? sender, WorkerStatusResponse status)
    {
        UIDispatcherHelper.BeginInvoke(() => ApplyWorkerStatus(status));
    }

    /// <summary>
    /// 连接期间定时刷新 Worker 状态，让主按钮与状态文本跟随 Worker（Worker 不会主动推送）
    /// </summary>
    private void UpdateWorkerStatusTimer()
    {
        if (_workerController.IsConnected)
        {
            if (!_workerStatusTimer.IsEnabled)
            {
                _workerStatusTimer.Start();
            }
        }
        else
        {
            _workerStatusTimer.Stop();
        }
    }

    private void OnWorkerStatusTimerTick(object? sender, EventArgs e)
    {
        if (!_workerController.IsConnected)
        {
            _workerStatusTimer.Stop();
            return;
        }

        if (_workerStatusRefreshing)
        {
            return;
        }

        _ = RefreshWorkerStatusQuietlyAsync();
    }

    private async Task RefreshWorkerStatusQuietlyAsync()
    {
        _workerStatusRefreshing = true;
        try
        {
            ApplyWorkerStatus(await _workerController.GetStatusAsync());
        }
        catch (WorkerUnavailableException)
        {
            // 断开由 WorkerController.Disconnected 事件统一处理
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "刷新 Worker 状态失败");
        }
        finally
        {
            _workerStatusRefreshing = false;
        }
    }

    /// <summary>
    /// 统一处理 Worker 调用：期间禁用按钮，失败只提示不抛出
    /// </summary>
    private async Task ExecuteWorkerActionAsync(Func<Task> action, string failureTitle)
    {
        IsWorkerBusy = true;
        try
        {
            await action();
        }
        catch (WorkerUnavailableException ex)
        {
            _logger.LogWarning(ex, "Worker 操作失败：{Code}", ex.ErrorCode);
            WorkerStatusText = $"{ex.ErrorCode}：{ex.Message}";
            ThemedMessageBox.Warning(ex.Message, failureTitle);
            if (ex.ErrorCode == "worker_offline")
            {
                IsWorkerConnected = false;
                IsWorkerTriggerRunning = false;
                UpdateWorkerStatusTimer();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Worker 操作异常");
            WorkerStatusText = ex.Message;
            ThemedMessageBox.Warning(ex.Message, failureTitle);
        }
        finally
        {
            IsWorkerBusy = false;
        }
    }

    private void SyncWorkerStateFromController()
    {
        IsWorkerConnected = _workerController.IsConnected;
        if (_workerController.ConnectedWorkerSid is { } sid)
        {
            WorkerSidInput = sid;
            WorkerStatusText = $"已连接（{sid}）";
            if (_workerController.LastStatus is { } status)
            {
                ApplyWorkerStatus(status);
            }
        }
        else
        {
            WorkerStatusText = "未连接";
        }

        UpdateWorkerStatusTimer();
    }

    /// <summary>
    /// Worker 断开（含进程退出）在管道接收线程上触发，切回 UI 线程更新
    /// </summary>
    private void OnWorkerDisconnected(object? sender, EventArgs e)
    {
        UIDispatcherHelper.BeginInvoke(() =>
        {
            IsWorkerConnected = false;
            IsWorkerTriggerRunning = false;
            WorkerStatusText = "Worker 已断开";
            UpdateWorkerStatusTimer();
        });
    }

    #endregion

    [RelayCommand]
    public void OnGoToWikiUrl()
    {
        Process.Start(new ProcessStartInfo("https://www.bettergi.com/doc.html") { UseShellExecute = true });
    }

    [RelayCommand]
    private void OnTest()
    {
        // var result = OcrFactory.Paddle.OcrResult(new Mat(@"E:\HuiTask\更好的原神\自动秘境\自动战斗\队伍识别\x2.png", ImreadModes.Grayscale));
        // foreach (var region in result.Regions)
        // {
        //     Debug.WriteLine($"{region.Text}");
        // }

        //try
        //{
        //    YoloV8 predictor = new(Global.Absolute("Assets\\Model\\Fish\\bgi_fish.onnx"));
        //    using var memoryStream = new MemoryStream();
        //    new Bitmap(Global.Absolute("test_yolo.png")).Save(memoryStream, ImageFormat.Bmp);
        //    memoryStream.Seek(0, SeekOrigin.Begin);
        //    var result = predictor.Detect(memoryStream);
        //    ThemedMessageBox.Show(JsonSerializer.Serialize(result));
        //}
        //catch (Exception e)
        //{
        //    ThemedMessageBox.Show(e.StackTrace);
        //}

        // Mat tar = new(@"E:\HuiTask\更好的原神\自动剧情\自动邀约\selected.png", ImreadModes.Grayscale);
        //  var mask = OpenCvCommonHelper.CreateMask(tar, new Scalar(0, 0, 0));
        // var src = new Mat(@"E:\HuiTask\更好的原神\自动剧情\自动邀约\Clip_20240309_135839.png", ImreadModes.Grayscale);
        // var src2 = src.Clone();
        // var res = MatchTemplateHelper.MatchOnePicForOnePic(src, mask);
        // // 把结果画到原图上
        // foreach (var t in res)
        // {
        //     Cv2.Rectangle(src2, t, new Scalar(0, 0, 255));
        // }
        //
        // Cv2.ImWrite(@"E:\HuiTask\更好的原神\自动剧情\自动邀约\x1.png", src2);
    }

    [RelayCommand]
    public void SelectInstallPath()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "原神|YuanShen.exe;GenshinImpact.exe|可执行文件|*.exe|所有文件|*.*"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var path = dialog.FileName;
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        Config.GenshinStartConfig.InstallPath = path;
    }

    private void ReadGameInstallPath()
    {
        // 检查用户是否配置了原神安装目录，如果没有，尝试从注册表中读取
        if (string.IsNullOrEmpty(Config.GenshinStartConfig.InstallPath))
        {
            Task.Run(async () =>
            {
                var p1 = RegistryGameLocator.GetDefaultGameInstallPath();
                if (!string.IsNullOrEmpty(p1))
                {
                    Config.GenshinStartConfig.InstallPath = p1;
                }
                else
                {
                    var p2 = await UnityLogGameLocator.LocateSingleGamePathAsync();
                    if (!string.IsNullOrEmpty(p2))
                    {
                        Config.GenshinStartConfig.InstallPath = p2;
                    }
                }
            });
        }
    }

    //[RelayCommand]
    //private void OnOpenGameCommandLineDocument()
    //{
    //    string md = File.ReadAllText(Global.Absolute(@"Assets\Strings\gicli.md"), Encoding.UTF8);

    //    md = WebUtility.HtmlEncode(md);
    //    string md2html = File.ReadAllText(Global.Absolute(@"Assets\Strings\md2html.html"), Encoding.UTF8);
    //    var html = md2html.Replace("{{content}}", md);

    //    WebpageWindow win = new()
    //    {
    //        Title = "启动参数说明",
    //        Width = 800,
    //        Height = 600,
    //        Owner = Application.Current.MainWindow,
    //        WindowStartupLocation = WindowStartupLocation.CenterOwner
    //    };

    //    win.NavigateToHtml(html);
    //    win.ShowDialog();
    //}

    [RelayCommand]
    private void OnOpenGameCommandLineDocument()
    {
        // 创建 MarkdownView 来显示内容
        var markdownView = new MarkdownView
        {
            FilePath = Global.Absolute(@"Assets\Strings\gicli.md"),
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(12, 0, 12, 12),
            LinkNavigationMode = MarkdownLinkNavigationMode.SystemDefault
        };

        // 创建两行的 Grid 容器
        var grid = new System.Windows.Controls.Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // TitleBar 行
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 内容行

        // 创建 TitleBar
        var titleBar = new Wpf.Ui.Controls.TitleBar
        {
            Title = "启动参数说明",
            Icon = new ImageIcon
            {
                Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(@"pack://application:,,,/Resources/Images/logo.png", UriKind.Absolute))
            },
        };
        System.Windows.Controls.Grid.SetRow(titleBar, 0);
        grid.Children.Add(titleBar);

        // 将 MarkdownView 添加到第二行
        System.Windows.Controls.Grid.SetRow(markdownView, 1);
        grid.Children.Add(markdownView);

        // 创建 FluentWindow 来显示内容
        var dialogWindow = new FluentWindow
        {
            Content = grid,
            Width = 800,
            Height = 600,
            Owner = Application.Current.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.Manual,
            WindowBackdropType = WindowBackdropType.Mica,
            ExtendsContentIntoTitleBar = true,
        };
        dialogWindow.SourceInitialized += (s, e) => WindowHelper.TryApplySystemBackdrop(dialogWindow);
        dialogWindow.ShowDialog();
    }

    [RelayCommand]
    public void OnOpenHardwareAccelerationSettings()
    {
        var dialogWindow = new FluentWindow
        {
            Title = "硬件加速设置",
            Content = new HardwareAccelerationView(new HardwareAccelerationViewModel()),
            Width = 800,
            Height = 600,
            MinWidth = 800,
            MaxWidth = 800,
            MinHeight = 600,
            Owner = Application.Current.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ExtendsContentIntoTitleBar = true,
            WindowBackdropType = WindowBackdropType.Auto,
        };
        dialogWindow.SourceInitialized += (s, e) => WindowHelper.TryApplySystemBackdrop(dialogWindow);
        var result = dialogWindow.ShowDialog();
    }

    #region 背景图片管理

    private void InitializeBannerImage()
    {
        LoadFallbackBannerImage();
        try
        {
            // 检查url文件
            var url = _bannerImageService.ReadConfiguredUrl();
            // 判断是否有内容
            if (!string.IsNullOrEmpty(url))
            {
                _ = DownloadAndApplyBannerImageAsync(url, true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化背景图片失败，使用现有背景图片");
        }
    }

    private void LoadFallbackBannerImage()
    {
        IsCustomNetworkBanner = false;
        try
        {
            // 检查是否存在自定义图片
            if (File.Exists(_customBannerImagePath))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(Path.GetFullPath(_customBannerImagePath));
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                BannerImageSource = bitmap;
                _logger.LogInformation("已加载自定义背景图片");
            }
            else
            {
                // 使用默认图片
                BannerImageSource = new BitmapImage(new Uri(DefaultBannerImagePath, UriKind.Absolute));
                _logger.LogInformation("已加载默认背景图片");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化背景图片失败，使用默认图片");
            BannerImageSource = new BitmapImage(new Uri(DefaultBannerImagePath, UriKind.Absolute));
        }
    }

    private async Task DownloadAndApplyBannerImageAsync(string url, bool showErrorToast)
    {
        CancelBannerDownload();
        var cancellationTokenSource = new CancellationTokenSource();
        _bannerDownloadCancellationTokenSource = cancellationTokenSource;

        try
        {
            if (!await _bannerImageService.DownloadAndSaveAsync(url, cancellationTokenSource.Token))
            {
                return;
            }

            cancellationTokenSource.Token.ThrowIfCancellationRequested();
            RefreshBannerImage();
        }
        catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
        {
            // 新操作替代旧下载或恢复默认图片时无需提示。
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "下载自定义背景图片url失败，使用现有背景图片");
            if (showErrorToast)
            {
                Toast.Error($"下载自定义背景图片url失败：{ex.Message}");
            }

            LoadFallbackBannerImage();
        }
        finally
        {
            if (ReferenceEquals(_bannerDownloadCancellationTokenSource, cancellationTokenSource))
            {
                _bannerDownloadCancellationTokenSource = null;
            }

            cancellationTokenSource.Dispose();
        }
    }

    private void RefreshBannerImage()
    {
        IsCustomNetworkBanner = true;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(Path.GetFullPath(_bannerImageService.NetworkImagePath));
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bitmap.EndInit();
        BannerImageSource = bitmap;
        _logger.LogInformation("已加载自定义背景图片url");
    }

    private void CancelBannerDownload()
    {
        var cancellationTokenSource = _bannerDownloadCancellationTokenSource;
        _bannerDownloadCancellationTokenSource = null;
        cancellationTokenSource?.Cancel();
        _bannerImageService.InvalidatePendingDownloads();
    }

    [RelayCommand]
    private void ChangeBannerImage()
    {
        try
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "选择背景图片",
                Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif|所有文件|*.*",
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() == true)
            {
                ResetBannerImage();
                
                var selectedFile = openFileDialog.FileName;

                // 确保目标目录存在
                var directory = Path.GetDirectoryName(_customBannerImagePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // 复制图片到自定义路径
                File.Copy(selectedFile, _customBannerImagePath, true);

                // 更新UI
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(Path.GetFullPath(_customBannerImagePath));
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache; 
                bitmap.EndInit();
                BannerImageSource = bitmap;
                Toast.Success("背景图片更换成功！");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更换背景图片失败");
            Toast.Error($"更换背景图片失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ChangeWebBannerImage()
    {
        try
        {
            CancelBannerDownload();
            // 打开窗口
            var vm = App.GetService<WebImageInputViewModel>();
            var webImageInput = new WebImageInput(vm!);
            webImageInput.Owner = Application.Current.MainWindow;
            vm!.SubmitCompleted += RefreshBannerImage;
            try
            {
                webImageInput.ShowDialog();
            }
            finally
            {
                vm.SubmitCompleted -= RefreshBannerImage;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更换背景图片失败");
            Toast.Error($"更换背景图片失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RefreshWebBannerImageAsync()
    {
        try
        {
            // 检查url文件
            var url = _bannerImageService.ReadConfiguredUrl();
            // 判断是否有内容
            if (!string.IsNullOrEmpty(url))
            {
                await DownloadAndApplyBannerImageAsync(url, true);
            }
            else
            {
                CancelBannerDownload();
                LoadFallbackBannerImage();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "刷新背景图片失败");
            Toast.Error($"刷新背景图片失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ResetBannerImage()
    {
        try
        {
            CancelBannerDownload();
            // 获取自定义图片的完整路径
            var customImageFullPath = Path.GetFullPath(_customBannerImagePath);
            _logger.LogInformation("尝试恢复默认背景图片，自定义图片路径: {CustomPath}", customImageFullPath);

            // 先切换到默认图片，释放自定义图片的文件锁
            var defaultBitmap = new BitmapImage();
            defaultBitmap.BeginInit();
            defaultBitmap.UriSource = new Uri(DefaultBannerImagePath, UriKind.Absolute);
            defaultBitmap.CacheOption = BitmapCacheOption.OnLoad;
            defaultBitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache; 
            defaultBitmap.EndInit();
            BannerImageSource = defaultBitmap;
            
            if (File.Exists(customImageFullPath))
            {
                File.Delete(customImageFullPath);
            }
            _bannerImageService.ResetNetworkImage();
            IsCustomNetworkBanner = false;
            Toast.Success("已恢复为默认背景图片！");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "恢复默认背景图片失败");
            Toast.Warning("已恢复为默认背景图片！但清除自定义图片失败，请手动删除文件。");
        }
    }

    #endregion
}
