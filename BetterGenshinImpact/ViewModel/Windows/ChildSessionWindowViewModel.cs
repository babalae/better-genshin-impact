using System;
using System.Diagnostics;
using System.IO;
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
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace BetterGenshinImpact.ViewModel.Windows;

public partial class ChildSessionWindowViewModel : ViewModel
{
    private const string DesktopHelpUrl = "https://www.bettergi.com/feats/command/session.html";
    private const double DefaultWindowWidth = 1280d;
    private const double SmallWindowWidth = 500d;

    private readonly ChildSessionService _childSessionService;
    private readonly DispatcherTimer _notificationTimer;
    private bool _startRequested;

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
    [NotifyPropertyChangedFor(nameof(GameMouseModeButtonText))]
    [NotifyPropertyChangedFor(nameof(GameMouseModeButtonToolTip))]
    private bool _isGameMouseModeEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AudioButtonToolTip))]
    private bool _isAudioMuted;

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

    public string GameMouseModeButtonText =>
        IsGameMouseModeEnabled ? "游戏鼠标" : "普通鼠标";

    public string GameMouseModeButtonToolTip => IsGameMouseModeEnabled
        ? "桌面分身内的 BetterGI 打开时生效；当前窗口处于焦点时，鼠标将会被锁定在窗口内；按住 Alt 可临时释放鼠标。"
        : "切换至游戏鼠标模式。桌面分身内的 BetterGI 打开时生效；当前窗口处于前台时，鼠标将会被锁定在窗口内；按住 Alt 可临时释放鼠标。";

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
        _isGameMouseModeEnabled = _childSessionService.IsGameMouseModeEnabled;
        _isAudioMuted = _childSessionService.AudioMuted;
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

    [RelayCommand]
    private async Task StartAsync()
    {
        if (_childSessionService.ConnectedState != 1
            && _childSessionService.IsRdpWrapperEnabled())
        {
            var result = await ThemedMessageBox.WarningAsync(
                "检测到系统已安装并启用 RDP Wrapper。\n\n"
                + "RDP Wrapper 提供了更强大的远程多用户支持，但与当前的桌面分身功能不兼容，"
                + "可能导致桌面分身无法正常启动。\n\n是否仍要继续？",
                "RDP Wrapper 兼容性提醒",
                MessageBoxButton.YesNo,
                MessageBoxResult.No);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }

        _startRequested = true;
        IsConnectionPromptVisible = false;

        try
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            await ExecuteAsync(_childSessionService.StartAsync);
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

    [RelayCommand]
    private void ToggleGameMouseMode()
    {
        var enabled = !IsGameMouseModeEnabled;
        if (!Execute(() => _childSessionService.SetGameMouseModeEnabled(enabled)))
        {
            IsGameMouseModeEnabled = _childSessionService.IsGameMouseModeEnabled;
            return;
        }

        IsGameMouseModeEnabled = _childSessionService.IsGameMouseModeEnabled;
        ShowNotification(
            enabled ? "游戏鼠标已开启" : "普通鼠标已开启",
            enabled
                ? "桌面分身内的 BetterGI 打开时生效。当前窗口处于焦点时，鼠标将会被锁定在窗口内，按住 Alt 临时释放鼠标。"
                : "BetterGI 不再向桌面分身转发相对鼠标信息。",
            enabled ? InfoBarSeverity.Informational : InfoBarSeverity.Success);
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

    private async Task ExecuteAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
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
        IsGameMouseModeEnabled = _childSessionService.IsGameMouseModeEnabled;
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
