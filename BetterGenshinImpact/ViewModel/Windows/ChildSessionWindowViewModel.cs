using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.ChildSession;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.ViewModel.Windows;

public partial class ChildSessionWindowViewModel : ViewModel
{
    private const string DesktopHelpUrl = "https://www.bettergi.com/feats/command/session.html";
    private const double DefaultWindowWidth = 1280d;
    private const double SmallWindowWidth = 500d;

    private readonly ChildSessionService _childSessionService;
    private readonly DispatcherTimer _notificationTimer;
    private bool _startRequested;
    private ChildSessionDiagnosticsWindow? _diagnosticsWindow;
    private ChildSessionAutoLoginWindow? _autoLoginWindow;
    private bool _autoSwitchedToGameMouse;

    [ObservableProperty]
    private bool _isDiagnosticsSuggested;

    [ObservableProperty]
    private Brush _connectionStatusBrush = Brushes.Red;

    [ObservableProperty]
    private string _connectionStatusToolTip = "桌面分身未启动";

    [ObservableProperty]
    private string _connectionStatusTitle = "桌面分身尚未启动";

    [ObservableProperty]
    private string _connectionStatusDescription = "点击“启动并连接”，BetterGI 将创建独立桌面并建立 RDP 连接。";

    [ObservableProperty]
    private string _rdpStatusText = "RDP 未连接";

    [ObservableProperty]
    private string _childSessionStatusText = "桌面分身会话未创建";

    [ObservableProperty]
    private bool _isRdpConnected;

    [ObservableProperty]
    private bool _isConnectionPromptVisible = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSessionInteractionEnabled))]
    private bool _isSessionClosing;

    [ObservableProperty]
    private bool _isTopmost;

    [ObservableProperty]
    private bool _isAdaptive = true;

    [ObservableProperty]
    private bool _isOneToOne;

    [ObservableProperty]
    private bool _keepAspectRatio = true;

    [ObservableProperty]
    private WindowPositionConfig? _normalWindowPosition;

    [ObservableProperty]
    private WindowPositionConfig? _smallWindowPosition;

    [ObservableProperty]
    private int _smallWindowResizeRequest;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowSizeModeMenuHeader))]
    [NotifyPropertyChangedFor(nameof(WindowSizeModeMenuToolTip))]
    [NotifyPropertyChangedFor(nameof(WindowResizeTargetWidth))]
    private bool _isSmallWindowMode;

    [ObservableProperty]
    private bool _sendSystemShortcutsToRemote = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InputModeButtonText))]
    [NotifyPropertyChangedFor(nameof(InputModeButtonToolTip))]
    [NotifyPropertyChangedFor(nameof(IsWatching))]
    [NotifyPropertyChangedFor(nameof(FullscreenInputModeText))]
    [NotifyPropertyChangedFor(nameof(FullscreenInputModeToolTip))]
    private ChildSessionInputMode _inputMode = ChildSessionInputMode.Normal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AudioButtonToolTip))]
    private bool _isAudioMuted;

    /// <summary>
    /// 全屏时浮层工具栏是否固定显示。写入设置，与窗口置顶无关。
    /// </summary>
    [ObservableProperty]
    private bool _isFullscreenBarPinned;

    [ObservableProperty]
    private bool _isNotificationOpen;

    [ObservableProperty]
    private string _notificationTitle = "";

    [ObservableProperty]
    private string _notificationMessage = "";

    [ObservableProperty]
    private InfoBarSeverity _notificationSeverity = InfoBarSeverity.Informational;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleSendSystemShortcutsToRemoteCommand))]
    private bool _isSystemShortcutsReconnectPending;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleAudioMutedCommand))]
    private bool _isAudioReconnectPending;

    public bool IsDefaultResolutionSelected => true;

    public string TopmostButtonToolTip => IsTopmost ? "取消置顶" : "置顶";

    /// <summary>
    /// 观看模式下键鼠不进分身。
    /// </summary>
    public bool IsWatching => InputMode == ChildSessionInputMode.Watch;

    /// <summary>
    /// 工具栏上那个输入模式按钮当前显示的状态。
    /// </summary>
    public string InputModeButtonText => InputMode switch
    {
        ChildSessionInputMode.Watch => "观看模式",
        ChildSessionInputMode.GameMouse => "游戏鼠标",
        _ => "普通鼠标"
    };

    public string InputModeButtonToolTip => InputMode switch
    {
        ChildSessionInputMode.Watch =>
            "当前处于观看模式：本机键鼠不会进入桌面分身。点击切换为普通鼠标。",
        ChildSessionInputMode.GameMouse =>
            "当前处于游戏鼠标模式：按住 Alt 可临时释放鼠标。点击切换为观看模式。",
        _ => "当前处于普通鼠标模式。点击切换为游戏鼠标；再点一次进入观看模式。"
    };

    /// <summary>
    /// 全屏浮层上那个开关的文字：只有「游戏模式 / 观看模式」两态。
    /// 全屏是为了玩游戏，普通鼠标这一档在这里没有意义，所以不参与。
    /// </summary>
    public string FullscreenInputModeText => IsWatching ? "观看模式" : "游戏模式";

    /// <summary>
    /// 全屏浮层开关的提示：只讲两态，不讲三态循环。
    /// </summary>
    public string FullscreenInputModeToolTip => IsWatching
        ? "当前处于观看模式：本机键鼠不会进入桌面分身。点击切换为游戏模式。"
        : "当前处于游戏模式：按住 Alt 可临时释放鼠标。点击切换为观看模式。";

    public string AudioButtonToolTip => IsAudioMuted
        ? "开启桌面分身声音，不影响主桌面的其他程序；切换时会自动重新连接 RDP"
        : "关闭桌面分身声音，不影响主桌面的其他程序；切换时会自动重新连接 RDP";

    public string WindowSizeModeMenuHeader => IsSmallWindowMode ? "还原窗口" : "小窗模式";

    public string WindowSizeModeMenuToolTip => IsSmallWindowMode
        ? "将桌面分身窗口还原至默认大小"
        : "将桌面分身窗口缩放至宽 750，并按 16:9 比例同步缩小高度";

    public double WindowResizeTargetWidth => IsSmallWindowMode
        ? SmallWindowWidth
        : DefaultWindowWidth;

    public bool HasChildSession => _childSessionService.ChildSessionId is not null;

    public bool IsSessionInteractionEnabled => !IsSessionClosing;

    public ChildSessionWindowViewModel(ChildSessionService childSessionService)
    {
        _childSessionService = childSessionService;
        _isTopmost = _childSessionService.TopmostEnabled;
        _isAdaptive = _childSessionService.SmartSizingEnabled;
        _isOneToOne = !_isAdaptive;
        _keepAspectRatio = _childSessionService.KeepAspectRatio;
        _normalWindowPosition = _childSessionService.NormalWindowPosition;
        _smallWindowPosition = _childSessionService.SmallWindowPosition;
        _sendSystemShortcutsToRemote = _childSessionService.SendSystemShortcutsToRemote;
        _inputMode = _childSessionService.InputMode;
        _isAudioMuted = _childSessionService.AudioMuted;
        _isFullscreenBarPinned = _childSessionService.FullscreenBarPinned;
        _notificationTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _notificationTimer.Tick += OnNotificationTimerTick;
        _childSessionService.StateChanged += OnChildSessionStateChanged;
        _childSessionService.ConnectionFailed += OnChildSessionConnectionFailed;
        _childSessionService.SystemShortcutsReconnectCompleted +=
            OnSystemShortcutsReconnectCompleted;
        _childSessionService.AudioReconnectCompleted += OnAudioReconnectCompleted;
        UpdateConnectionStatus();
    }

    partial void OnNormalWindowPositionChanged(WindowPositionConfig? value)
    {
        _childSessionService.NormalWindowPosition = value;
    }

    partial void OnSmallWindowPositionChanged(WindowPositionConfig? value)
    {
        _childSessionService.SmallWindowPosition = value;
    }

    public async Task LogoffAndHideAsync()
    {
        if (IsSessionClosing)
        {
            return;
        }

        IsSessionClosing = true;
        UpdateConnectionStatus();

        try
        {
            // 先让“正在关闭”状态完成渲染，再执行可能耗时的注销操作。
            await Dispatcher.Yield(DispatcherPriority.Render);
            await _childSessionService.LogoffAndHideAsync();
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            IsSessionClosing = false;
            UpdateConnectionStatus();
        }
    }

    partial void OnIsTopmostChanged(bool value)
    {
        OnPropertyChanged(nameof(TopmostButtonToolTip));
    }

    /// <summary>
    /// 启动桌面分身的命令入口。直接尝试连接，失败后提供诊断按钮，
    /// 由用户主动打开诊断面板检查环境。
    /// </summary>
    [RelayCommand]
    private async Task StartAsync()
    {
        // 环境条件只在用户主动诊断时检查，避免挡住本来可以连上的用户。
        IsDiagnosticsSuggested = false;
        _startRequested = true;
        IsConnectionPromptVisible = false;

        try
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            await ExecuteAsync(_childSessionService.StartAsync, suggestDiagnosticsOnError: true);
        }
        finally
        {
            _startRequested = false;
            UpdateConnectionStatus();
        }
    }

    [RelayCommand]
    private void Hide()
    {
        Execute(_childSessionService.HideWindow);
    }

    [RelayCommand]
    private void SwitchWindow()
    {
        Execute(_childSessionService.ShowChildSessionTaskView);
    }

    [RelayCommand]
    private Task LaunchBetterGiAsync()
    {
        return ExecuteAsync(_childSessionService.LaunchBetterGiAsync);
    }

    [RelayCommand]
    private void SelectDefaultResolution()
    {
        OnPropertyChanged(nameof(IsDefaultResolutionSelected));
    }

    [RelayCommand]
    private void UseAdaptive()
    {
        if (!Execute(() => _childSessionService.SetSmartSizing(true)))
        {
            return;
        }

        IsAdaptive = true;
        IsOneToOne = false;
        OnPropertyChanged(nameof(IsAdaptive));
        OnPropertyChanged(nameof(IsOneToOne));
    }

    [RelayCommand]
    private void UseOneToOne()
    {
        if (!Execute(() => _childSessionService.SetSmartSizing(false)))
        {
            return;
        }

        IsAdaptive = false;
        IsOneToOne = true;
        OnPropertyChanged(nameof(IsAdaptive));
        OnPropertyChanged(nameof(IsOneToOne));
    }

    [RelayCommand]
    private void ToggleSmallWindowMode()
    {
        IsSmallWindowMode = !IsSmallWindowMode;
        SmallWindowResizeRequest++;
    }

    [RelayCommand]
    private void ToggleKeepAspectRatio()
    {
        if (!Execute(() => _childSessionService.SetKeepAspectRatio(!KeepAspectRatio)))
        {
            return;
        }

        KeepAspectRatio = _childSessionService.KeepAspectRatio;
    }

    [RelayCommand(CanExecute = nameof(CanToggleSendSystemShortcutsToRemote))]
    private void ToggleSendSystemShortcutsToRemote()
    {
        try
        {
            var reconnectStarted = _childSessionService.SetSendSystemShortcutsToRemote(
                !SendSystemShortcutsToRemote);
            SendSystemShortcutsToRemote = _childSessionService.SendSystemShortcutsToRemote;
            IsSystemShortcutsReconnectPending = reconnectStarted;
            UpdateConnectionStatus();

            ShowNotification(
                reconnectStarted ? "正在应用设置" : "设置已保存",
                reconnectStarted
                    ? "RDP 正在自动重新连接，连接完成后系统组合键设置生效。"
                    : "当前没有桌面分身会话，系统组合键设置将在下次连接时生效。",
                InfoBarSeverity.Informational);
        }
        catch (Exception exception)
        {
            IsSystemShortcutsReconnectPending = false;
            SendSystemShortcutsToRemote = _childSessionService.SendSystemShortcutsToRemote;
            ShowNotification(
                "设置切换失败",
                exception.GetBaseException().Message,
                InfoBarSeverity.Error,
                TimeSpan.FromSeconds(8));
            UpdateConnectionStatus();
        }
    }

    private bool CanToggleSendSystemShortcutsToRemote()
    {
        return !IsSystemShortcutsReconnectPending;
    }

    /// <summary>
    /// 工具栏上的输入模式按钮：按「观看模式 → 普通鼠标 → 游戏鼠标」循环切换。
    /// </summary>
    [RelayCommand]
    private void CycleInputMode()
    {
        if (!Execute(() => _childSessionService.CycleInputMode()))
        {
            InputMode = _childSessionService.InputMode;
            return;
        }

        InputMode = _childSessionService.InputMode;

        // 用户自己选过模式了：退出全屏时按他的选择来，别再还原成普通鼠标。
        _autoSwitchedToGameMouse = false;

        ShowNotification(
            InputModeButtonText,
            InputModeButtonToolTip,
            InputMode == ChildSessionInputMode.Watch
                ? InfoBarSeverity.Informational
                : InfoBarSeverity.Success);
    }

    /// <summary>
    /// 全屏浮层上的「游戏模式 / 观看模式」开关。
    ///
    /// 全屏时不显示标题栏，那个按钮够不着，所以在浮层上给一个两态入口：
    /// 观看模式 ⇄ 游戏模式（游戏模式即游戏鼠标，玩游戏时需要的那个）。
    /// </summary>
    [RelayCommand]
    private void ToggleFullscreenWatchMode()
    {
        var target = IsWatching ? ChildSessionInputMode.GameMouse : ChildSessionInputMode.Watch;
        if (!Execute(() => _childSessionService.SetInputMode(target)))
        {
            InputMode = _childSessionService.InputMode;
            return;
        }

        InputMode = _childSessionService.InputMode;

        // 同 CycleInputMode：这次是用户的选择，退出全屏时不再还原。
        _autoSwitchedToGameMouse = false;
    }

    /// <summary>
    /// 切换全屏浮层工具栏的固定状态：固定后它不再随鼠标离开自动收起。写入设置。
    /// </summary>
    [RelayCommand]
    private void ToggleFullscreenBarPinned()
    {
        if (!Execute(() => { _childSessionService.FullscreenBarPinned = !IsFullscreenBarPinned; }))
        {
            return;
        }

        IsFullscreenBarPinned = _childSessionService.FullscreenBarPinned;
    }

    /// <summary>
    /// 进入全屏时把「普通鼠标」自动切成「游戏鼠标」——全屏就是为了玩游戏。
    /// 观看模式与游戏鼠标保持不变，并把「是不是我们切的」记下来，退出时据此还原。
    /// </summary>
    internal void ApplyInputModeForFullscreen()
    {
        _autoSwitchedToGameMouse = _childSessionService.InputMode == ChildSessionInputMode.Normal;
        if (!_autoSwitchedToGameMouse)
        {
            InputMode = _childSessionService.InputMode;
            return;
        }

        Execute(() => _childSessionService.SetInputMode(ChildSessionInputMode.GameMouse));
        InputMode = _childSessionService.InputMode;
    }

    /// <summary>
    /// 退出全屏时还原进入前的输入模式。
    ///
    /// 只还原「进入全屏时被我们改成游戏鼠标、且之后用户没动过」的那一次；
    /// 用户在全屏里自己切过模式的话，标记已被清掉，按他的选择继续。
    /// </summary>
    internal void RestoreInputModeAfterFullscreen()
    {
        if (!_autoSwitchedToGameMouse)
        {
            return;
        }

        _autoSwitchedToGameMouse = false;
        Execute(() => _childSessionService.SetInputMode(ChildSessionInputMode.Normal));
        InputMode = _childSessionService.InputMode;
    }

    /// <summary>
    /// 打开自动登录设置。已经打开时把它叫到前面，不重复创建。
    /// </summary>
    [RelayCommand]
    private void OpenAutoLoginSettings()
    {
        if (_autoLoginWindow is { IsLoaded: true } existing)
        {
            if (existing.WindowState == WindowState.Minimized)
            {
                existing.WindowState = WindowState.Normal;
            }

            existing.Activate();
            return;
        }

        var window = App.GetService<ChildSessionAutoLoginWindow>();
        if (window is null)
        {
            return;
        }

        window.Owner = Application.Current.Windows.OfType<ChildSessionWindow>().FirstOrDefault();
        window.Closed += (_, _) =>
        {
            _autoLoginWindow = null;

            // 对话框里可能改了自动登录开关，回来时把界面同步一次。
            UpdateConnectionStatus();
        };
        _autoLoginWindow = window;
        window.Show();
    }

    [RelayCommand(CanExecute = nameof(CanToggleAudioMuted))]
    private void ToggleAudioMuted()
    {
        try
        {
            var reconnectStarted = _childSessionService.SetAudioMuted(!IsAudioMuted);
            IsAudioMuted = _childSessionService.AudioMuted;
            IsAudioReconnectPending = reconnectStarted;
            UpdateConnectionStatus();

            ShowNotification(
                reconnectStarted ? "正在应用音频设置" : "音频设置已保存",
                reconnectStarted
                    ? "RDP 正在自动重新连接，连接完成后音频设置生效。"
                    : IsAudioMuted
                        ? "桌面分身将在下次连接时保持静音。"
                        : "桌面分身将在下次连接时播放声音。",
                InfoBarSeverity.Informational);
        }
        catch (Exception exception)
        {
            IsAudioReconnectPending = false;
            IsAudioMuted = _childSessionService.AudioMuted;
            ShowNotification(
                "音频设置切换失败",
                exception.GetBaseException().Message,
                InfoBarSeverity.Error,
                TimeSpan.FromSeconds(8));
            UpdateConnectionStatus();
        }
    }

    private bool CanToggleAudioMuted()
    {
        return !IsAudioReconnectPending;
    }

    [RelayCommand]
    private void ShowDesktop()
    {
        Execute(_childSessionService.ShowChildSessionDesktop);
    }

    [RelayCommand]
    private void ShowTaskView()
    {
        Execute(_childSessionService.ShowChildSessionTaskView);
    }

    [RelayCommand]
    private async Task LaunchExecutableAsync()
    {
        var fileDialog = new OpenFileDialog
        {
            Title = "选择要在 BetterGI 桌面分身中以管理员权限启动的程序",
            Filter = "Windows 程序 (*.exe)|*.exe",
            CheckFileExists = true,
            CheckPathExists = true,
            DereferenceLinks = true,
            Multiselect = false
        };

        if (fileDialog.ShowDialog() != true)
        {
            return;
        }

        var executablePath = Path.GetFullPath(fileDialog.FileName);
        await ExecuteAsync(() => _childSessionService.LaunchExecutableAsync(executablePath));
    }

    [RelayCommand]
    private void ToggleTopmost()
    {
        if (!Execute(() => _childSessionService.SetTopmost(!IsTopmost)))
        {
            return;
        }

        IsTopmost = _childSessionService.TopmostEnabled;
    }

    [RelayCommand]
    private static void OpenDesktopHelp()
    {
        Process.Start(new ProcessStartInfo(DesktopHelpUrl)
        {
            UseShellExecute = true
        });
    }

    private async Task ExecuteAsync(Func<Task> action, bool suggestDiagnosticsOnError = false)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            // 启动直接抛异常（例如系统缺少 Child Session API）时也要提供诊断入口：
            // 用户点击后再检查环境，帮助定位具体缺少的条件。
            if (suggestDiagnosticsOnError)
            {
                SuggestDiagnosticsOnFailure();
            }

            ShowError(exception);
        }
        finally
        {
            UpdateConnectionStatus();
        }
    }

    private bool Execute(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception exception)
        {
            ShowError(exception);
            return false;
        }
        finally
        {
            UpdateConnectionStatus();
        }
    }

    private void OnChildSessionStateChanged(object? sender, EventArgs e)
    {
        if (Application.Current.Dispatcher.CheckAccess())
        {
            UpdateConnectionStatus();
            return;
        }

        _ = Application.Current.Dispatcher.BeginInvoke(UpdateConnectionStatus);
    }

    /// <summary>
    /// 打开诊断与修复面板。已经打开时把它叫到前面，不重复创建。
    /// </summary>
    [RelayCommand]
    private void OpenDiagnostics()
    {
        if (_diagnosticsWindow is { IsLoaded: true } existing)
        {
            // 最小化时 Activate() 不会把窗口还原，用户点菜单会以为没反应。
            if (existing.WindowState == WindowState.Minimized)
            {
                existing.WindowState = WindowState.Normal;
            }

            existing.Activate();
            return;
        }

        var window = App.GetService<ChildSessionDiagnosticsWindow>();
        if (window is null)
        {
            return;
        }

        // 面板属于桌面分身窗口，挂上去后跟随它一起关闭。
        window.Owner = Application.Current.Windows.OfType<ChildSessionWindow>().FirstOrDefault()
                       ?? Application.Current.MainWindow;
        window.Closed += (_, _) => _diagnosticsWindow = null;
        _diagnosticsWindow = window;
        window.Show();
    }

    /// <summary>
    /// 启动失败时显示诊断入口，由用户点击后打开面板。
    ///
    /// 失败会被重试流程触发多次，这里只更新按钮的可见状态，
    /// 用户也可以通过菜单打开，不会反复抢焦点。
    /// </summary>
    private void SuggestDiagnosticsOnFailure()
    {
        IsDiagnosticsSuggested = true;
    }

    private void OnChildSessionConnectionFailed(
        object? sender,
        ChildSessionConnectionFailedEventArgs e)
    {
        _ = Application.Current.Dispatcher.BeginInvoke(
            DispatcherPriority.Normal,
            new Action(() =>
            {
                IsSystemShortcutsReconnectPending = false;
                IsAudioReconnectPending = false;
                UpdateConnectionStatus();
                ShowNotification(
                    "RDP 连接失败",
                    e.Message,
                    InfoBarSeverity.Error,
                    TimeSpan.FromSeconds(10));

                // 失败原因往往在环境里（服务被禁用、被补丁接管等），
                // 提供诊断按钮，让用户主动检查，无需自己翻日志。
                SuggestDiagnosticsOnFailure();
            }));
    }

    private void OnSystemShortcutsReconnectCompleted(object? sender, EventArgs e)
    {
        _ = Application.Current.Dispatcher.BeginInvoke(
            DispatcherPriority.Normal,
            new Action(() =>
            {
                IsSystemShortcutsReconnectPending = false;
                UpdateConnectionStatus();
                ShowNotification(
                    "设置已生效",
                    SendSystemShortcutsToRemote
                        ? "系统组合键现在会发送到桌面分身。"
                        : "系统组合键现在会在本机生效。",
                    InfoBarSeverity.Success);
            }));
    }

    private void OnAudioReconnectCompleted(object? sender, EventArgs e)
    {
        _ = Application.Current.Dispatcher.BeginInvoke(
            DispatcherPriority.Normal,
            new Action(() =>
            {
                IsAudioReconnectPending = false;
                IsAudioMuted = _childSessionService.AudioMuted;
                UpdateConnectionStatus();
                ShowNotification(
                    IsAudioMuted ? "桌面分身已静音" : "桌面分身声音已开启",
                    IsAudioMuted
                        ? "仅桌面分身的声音已关闭，主桌面其他程序不受影响。"
                        : "桌面分身的声音已恢复。",
                    InfoBarSeverity.Success);
            }));
    }

    private void ShowNotification(
        string title,
        string message,
        InfoBarSeverity severity,
        TimeSpan? duration = null)
    {
        _notificationTimer.Stop();
        NotificationTitle = title;
        NotificationMessage = message;
        NotificationSeverity = severity;
        IsNotificationOpen = true;
        _notificationTimer.Interval = duration ?? TimeSpan.FromSeconds(5);
        _notificationTimer.Start();
    }

    private void OnNotificationTimerTick(object? sender, EventArgs e)
    {
        _notificationTimer.Stop();
        IsNotificationOpen = false;
    }

    private void UpdateConnectionStatus()
    {
        var connectedState = _childSessionService.ConnectedState;
        var childSessionId = _childSessionService.ChildSessionId;
        IsTopmost = _childSessionService.TopmostEnabled;
        IsAdaptive = _childSessionService.SmartSizingEnabled;
        IsOneToOne = !IsAdaptive;
        KeepAspectRatio = _childSessionService.KeepAspectRatio;
        SendSystemShortcutsToRemote = _childSessionService.SendSystemShortcutsToRemote;
        InputMode = _childSessionService.InputMode;
        IsAudioMuted = _childSessionService.AudioMuted;
        IsRdpConnected = connectedState == 1;
        IsConnectionPromptVisible = IsSessionClosing || connectedState == 0 && !_startRequested;

        if (IsSessionClosing)
        {
            ConnectionStatusBrush = Brushes.Orange;
            ConnectionStatusTitle = "正在关闭桌面分身";
            ConnectionStatusDescription = "正在断开 RDP 并注销桌面分身会话，此过程可能需要一些时间。";
            RdpStatusText = connectedState == 0 ? "RDP 已断开" : "正在断开 RDP";
            ChildSessionStatusText = childSessionId is null
                ? "正在确认桌面分身会话已注销"
                : $"正在注销桌面分身会话 {childSessionId.Value}";
            ConnectionStatusToolTip = "关闭期间已暂停桌面分身操作，完成后窗口会自动隐藏。";
            OnPropertyChanged(nameof(HasChildSession));
            return;
        }

        if (IsRdpConnected)
        {
            IsDiagnosticsSuggested = false;
            ConnectionStatusBrush = Brushes.LimeGreen;
            ConnectionStatusTitle = "桌面分身已连接";
            ConnectionStatusDescription = "RDP 连接正常，可以直接在桌面分身中操作。";
        }
        else if (connectedState == 2)
        {
            ConnectionStatusBrush = childSessionId is null ? Brushes.Red : Brushes.DodgerBlue;
            ConnectionStatusTitle = "正在连接桌面分身";
            ConnectionStatusDescription = "正在创建或恢复 RDP 连接，请稍候。";
        }
        else if (childSessionId is not null)
        {
            ConnectionStatusBrush = Brushes.DodgerBlue;
            ConnectionStatusTitle = "桌面分身已启动，等待连接";
            ConnectionStatusDescription = "桌面分身会话仍在运行，点击“启动并连接”可重新建立 RDP 连接。";
        }
        else
        {
            ConnectionStatusBrush = Brushes.Red;
            ConnectionStatusTitle = "桌面分身尚未启动";
            ConnectionStatusDescription = "点击“启动并连接”，BetterGI 将创建独立桌面并建立 RDP 连接。";
        }

        RdpStatusText = connectedState switch
        {
            0 => "RDP 未连接",
            1 => "RDP 已连接",
            2 => "RDP 正在连接",
            _ => $"RDP 状态未知（{connectedState}）"
        };
        ChildSessionStatusText = childSessionId is null
            ? "桌面分身会话未创建"
            : $"桌面分身会话 {childSessionId.Value}";

        ConnectionStatusToolTip = _childSessionService.StatusText;
        OnPropertyChanged(nameof(HasChildSession));
    }

    private static void ShowError(Exception exception)
    {
        var actualException = exception.GetBaseException();
        var suggestion = actualException is System.ComponentModel.Win32Exception { NativeErrorCode: 5 }
            ? "\n\n操作被系统拒绝，请确认 BetterGI 正在以管理员权限运行。"
            : string.Empty;

        ThemedMessageBox.Error(
            $"{actualException.Message}{suggestion}",
            "BetterGI 桌面分身");
    }
}
