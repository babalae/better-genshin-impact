using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingSize = System.Drawing.Size;

namespace BetterGenshinImpact.Service.ChildSession;

public sealed class ChildSessionService : IDisposable
{
    private static readonly DrawingSize DefaultDesktopSize = new(1920, 1080);
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(60);
    private const int InitialConnectionRetryCount = 3;
    private const int RdpSettingsReconnectRetryCount = 2;
    private const int ErrorTimeout = 1460;

    private readonly IServiceProvider _serviceProvider;
    private readonly InstanceService _instanceService;
    /// <summary>启动前检查及登录后短期观察共用的 UU 修复服务。</summary>
    private readonly UuRemoteSessionRepairService _uuRemoteSessionRepairService;
    private readonly ChildSessionConfig _config;
    private readonly ILogger<ChildSessionService> _logger;
    private readonly DispatcherTimer _statusTimer;
    private readonly SemaphoreSlim _launchSemaphore = new(1, 1);
    private readonly CancellationTokenSource _disposeCancellationTokenSource = new();
    /// <summary>覆盖本次修复、RDP 等待、重试和自动启动的取消源。</summary>
    private CancellationTokenSource? _startupCancellationTokenSource;

    private ChildSessionWindow? _desktopWindow;
    private bool _autoLaunchBetterGiPending;
    private TaskCompletionSource<bool>? _connectionAttemptCompletionSource;
    private ChildSessionConnectionFailedEventArgs? _lastConnectionFailure;
    private int _initialConnectionRetriesRemaining;
    private bool _connectionRetryInProgress;
    private RdpSettingChange _pendingRdpSettingChanges;
    private int _rdpSettingsReconnectRetriesRemaining;
    private bool _rdpSettingsReconnectRetryInProgress;
    private bool _statusTickInProgress;
    /// <summary>同一启动周期只在首次登录完成后开启一次观察。</summary>
    private bool _uuRemoteStartupMonitorPending;
    /// <summary>登录后观察任务，重启或注销时等待它退出再释放取消源。</summary>
    private Task? _uuRemoteStartupMonitorTask;
    /// <summary>合并尚未完成的重复启动请求，避免重置修复次数或同时建立 RDP 连接。</summary>
    private Task? _startupTask;
    /// <summary>注销期间拒绝新的启动请求，避免异步注销与连接交叉执行。</summary>
    private bool _isLoggingOff;
    private bool _disposed;
    private string? _lastOperationMessage;
    private IReadOnlyList<ChildSessionEnvironmentCheck.Issue> _environmentIssues = [];

    public event EventHandler? StateChanged;

    public event EventHandler<ChildSessionConnectionFailedEventArgs>? ConnectionFailed;

    public event EventHandler? SystemShortcutsReconnectCompleted;

    public event EventHandler? AudioReconnectCompleted;

    /// <summary>向窗口转发 UU 检查、修复和再次冲突状态。</summary>
    public event EventHandler<UuRemoteRepairStateChangedEventArgs>? UuRemoteRepairStateChanged;

    public string StatusText { get; private set; } = "桌面分身尚未启动";

    public bool IsDesktopVisible => _desktopWindow?.IsVisible == true;

    public int ConnectedState { get; private set; }

    /// <summary>包括 UU 检查等待阶段，关闭窗口时需要取消整个启动任务。</summary>
    public bool IsStarting => _startupTask is { IsCompleted: false };

    public uint? ChildSessionId { get; private set; }

    public bool SendSystemShortcutsToRemote { get; private set; } = true;

    public bool IsGameMouseModeEnabled => _instanceService.IsGameMouseModeEnabled;

    public bool TopmostEnabled => _config.TopmostEnabled;

    public bool SmartSizingEnabled => _config.SmartSizingEnabled;

    public bool KeepAspectRatio => _config.KeepAspectRatio;

    public bool AudioMuted => _config.AudioMuted;

    public bool WatchModeEnabled => _config.WatchModeEnabled;

    public WindowPositionConfig? NormalWindowPosition
    {
        get => _config.NormalWindowPosition;
        set => _config.NormalWindowPosition = value;
    }

    public WindowPositionConfig? SmallWindowPosition
    {
        get => _config.SmallWindowPosition;
        set => _config.SmallWindowPosition = value;
    }

    /// <summary>启动时自动修复 UU 主会话冲突的持久化开关。</summary>
    public bool IsUuRemoteRepairEnabled => _uuRemoteSessionRepairService.IsEnabled;

    /// <summary>当前实例是否满足自动修复的权限和会话要求。</summary>
    public bool IsUuRemoteRepairAvailable => _uuRemoteSessionRepairService.IsAvailable;

    /// <summary>是否已完成首次启用的风险确认。</summary>
    public bool IsUuRemoteRepairRiskAcknowledged =>
        _uuRemoteSessionRepairService.IsRiskAcknowledged;

    /// <summary>修改 UU 自动修复开关，不改变系统策略。</summary>
    public void SetUuRemoteRepairEnabled(bool enabled)
    {
        _uuRemoteSessionRepairService.SetEnabled(enabled);
        RefreshState(enabled
            ? "已开启启动时自动修复 UU 远程主会话冲突"
            : "已关闭启动时自动修复 UU 远程主会话冲突");
    }

    /// <summary>在用户明确确认风险后记录确认状态。</summary>
    public void AcknowledgeUuRemoteRepairRisk()
    {
        _uuRemoteSessionRepairService.AcknowledgeRisk();
    }

    /// <summary>
    /// 系统是否已安装并启用 RDP Wrapper。它与桌面分身功能不兼容，界面会在启动前据此提示用户。
    /// </summary>
    public bool IsRdpWrapperEnabled()
    {
        return ChildSessionNativeMethods.IsRdpWrapperEnabled();
    }

    /// <summary>
    /// 最近一次启动前环境预检是否发现了可能影响桌面分身的环境设置。
    /// </summary>
    public bool HasEnvironmentIssue => _environmentIssues.Count > 0;

    /// <summary>
    /// 最近一次启动前环境预检的结论文本，供界面提示与问题排查使用。
    /// </summary>
    public string EnvironmentIssueSummary =>
        ChildSessionEnvironmentCheck.BuildSummary(_environmentIssues);

    public bool HasActiveChildSession()
    {
        if (!_instanceService.Context.IsRoot)
        {
            return false;
        }

        RefreshState();
        return ChildSessionId is not null;
    }

    public ChildSessionService(
        IServiceProvider serviceProvider,
        InstanceService instanceService,
        UuRemoteSessionRepairService uuRemoteSessionRepairService,
        IConfigService configService,
        ILogger<ChildSessionService> logger)
    {
        _serviceProvider = serviceProvider;
        _instanceService = instanceService;
        _uuRemoteSessionRepairService = uuRemoteSessionRepairService;
        _uuRemoteSessionRepairService.RepairStateChanged +=
            OnUuRemoteRepairStateChanged;
        _config = configService.Get().ChildSessionConfig;
        SendSystemShortcutsToRemote = _config.SendSystemShortcutsToRemote;
        _logger = logger;
        _statusTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _statusTimer.Tick += OnStatusTimerTick;
        _statusTimer.Start();
        RefreshState();
    }

    /// <summary>
    /// 启动桌面分身：先做一次环境预检，确保 Child Sessions 已启用，然后建立 RDP 连接并等待登录完成。
    /// 重复请求共享同一个启动任务；UU 修复失败仍继续连接，注销或退出则取消整个启动周期。
    /// </summary>
    public Task StartAsync()
    {
        ThrowIfDisposed();
        var dispatcher = Application.Current.Dispatcher;
        if (!dispatcher.CheckAccess())
        {
            return dispatcher.InvokeAsync(StartAsync).Task.Unwrap();
        }

        if (_isLoggingOff)
        {
            throw new InvalidOperationException("正在注销桌面分身，请等待注销完成后再启动。");
        }

        if (_startupTask is { IsCompleted: false })
        {
            return _startupTask;
        }

        var previousCycleCleanup = CancelStartupCycle();
        _startupCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            _disposeCancellationTokenSource.Token);
        _startupTask = StartCoreAsync(_startupCancellationTokenSource.Token, previousCycleCleanup);
        return _startupTask;
    }

    /// <summary>等待上一周期退出后检查 UU，再在 STA UI 线程建立或恢复 RDP 连接。</summary>
    private async Task StartCoreAsync(CancellationToken cancellationToken, Task previousCycleCleanup)
    {
        TaskCompletionSource<bool>? completionSource = null;
        try
        {
            // 先让 StartAsync 保存任务与取消源，确保立即点击关闭也能取消本次启动。
            await Task.Yield();
            await previousCycleCleanup.WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            RefreshEnvironmentCheck();
            EnsureChildSessionsEnabled();
            RefreshState();
            _connectionRetryInProgress = false;
            _rdpSettingsReconnectRetryInProgress = false;
            _uuRemoteSessionRepairService.BeginStartupCycle();

            _logger.LogDebug(
                "收到桌面分身启动请求。RDP 状态：{ConnectedState}，Child Session：{ChildSessionId}，"
                + "UU 自动修复已启用：{UuRepairEnabled}。",
                ConnectedState, ChildSessionId, _uuRemoteSessionRepairService.IsEnabled);

            if (_uuRemoteSessionRepairService.IsEnabled)
            {
                // 在修复前创建并显示窗口，让风险提示的订阅和渲染先于任何会话操作。
                ShowDesktopWindow(EnsureDesktopWindow());
            }

            // 已连接时也检查：UU 可能在桌面分身启动之后才迁移到后台会话。
            var repairResult = await _uuRemoteSessionRepairService.TryRepairBeforeStartAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogInformation(
                "桌面分身启动前 UU 检查完成。是否尝试修复：{WasAttempted}，是否成功：{IsSuccessful}，原因：{Reason}。",
                repairResult.WasAttempted, repairResult.IsSuccessful, repairResult.FailureReason);
            RefreshState();
            _uuRemoteStartupMonitorPending = _uuRemoteSessionRepairService.IsEnabled;
            if (ConnectedState == 1 && !repairResult.DisconnectedUserSessions)
            {
                StartUuRemoteStartupMonitor();
                return;
            }

            completionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _connectionAttemptCompletionSource = completionSource;
            _lastConnectionFailure = null;
            if (repairResult.DisconnectedUserSessions && _desktopWindow?.RdpHost.ConnectedState != 0)
            {
                // WTS 已断开时 ActiveX 可能暂时仍报告已连接，强制恢复连接而不是直接返回。
                _desktopWindow?.RdpHost.ReconnectToChildSession(DefaultDesktopSize);
                RefreshState("UU 远程会话已处理，正在重新连接桌面分身");
            }
            else
            {
                ConnectCore("正在启动 BetterGI 桌面分身");
            }

            await completionSource.Task.WaitAsync(ConnectionTimeout, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 注销或退出属于主动取消，不能在修复结束后继续连接，也不弹失败对话框。
            completionSource?.TrySetCanceled(cancellationToken);
        }
        catch (TimeoutException)
        {
            if (completionSource is { Task.IsCompleted: false })
            {
                _autoLaunchBetterGiPending = false;
                _initialConnectionRetriesRemaining = 0;
                CompleteConnectionFailure(CreateConnectionTimeoutFailure());
                TryDisconnectRdpHost();
            }
        }
        catch
        {
            _autoLaunchBetterGiPending = false;
            _initialConnectionRetriesRemaining = 0;
            _uuRemoteStartupMonitorPending = false;
            completionSource?.TrySetResult(false);
            throw;
        }
        finally
        {
            if (ReferenceEquals(_connectionAttemptCompletionSource, completionSource))
            {
                _connectionAttemptCompletionSource = null;
            }
            if (cancellationToken.IsCancellationRequested)
            {
                _uuRemoteStartupMonitorPending = false;
            }
        }
    }

    public void ShowWindow()
    {
        ThrowIfDisposed();
        ShowDesktopWindow(EnsureDesktopWindow());
        RefreshState();
    }

    public void HideWindow()
    {
        ThrowIfDisposed();
        _desktopWindow?.Hide();
        RefreshState("已隐藏 BetterGI 桌面分身，RDP 连接保持不变");
    }

    public void ShowChildSessionDesktop()
    {
        ThrowIfDisposed();
        var window = EnsureDesktopWindow();
        ShowDesktopWindow(window);
        window.RdpHost.SendShowDesktopShortcut();
        RefreshState("已向 BetterGI 桌面分身发送 Win+D");
    }

    public void ShowChildSessionTaskView()
    {
        ThrowIfDisposed();
        var window = EnsureDesktopWindow();
        ShowDesktopWindow(window);
        window.RdpHost.SendTaskViewShortcut();
        RefreshState("已向 BetterGI 桌面分身发送 Win+Tab");
    }

    public void SetSmartSizing(bool enabled)
    {
        ThrowIfDisposed();
        EnsureDesktopWindow().RdpHost.SetSmartSizing(enabled);
        _config.SmartSizingEnabled = enabled;
        RefreshState(enabled ? "窗口显示模式已切换为自适应" : "窗口显示模式已切换为 1:1");
    }

    public void SetKeepAspectRatio(bool enabled)
    {
        ThrowIfDisposed();
        _config.KeepAspectRatio = enabled;
        RefreshState(enabled ? "桌面分身窗口将保持宽高比" : "桌面分身窗口不再保持宽高比");
    }

    public void SetTopmost(bool enabled)
    {
        ThrowIfDisposed();
        _config.TopmostEnabled = enabled;
        RefreshState(enabled ? "桌面分身窗口已置顶" : "桌面分身窗口已取消置顶");
    }

    public bool SetSendSystemShortcutsToRemote(bool enabled)
    {
        ThrowIfDisposed();
        if (SendSystemShortcutsToRemote == enabled)
        {
            return false;
        }

        SendSystemShortcutsToRemote = enabled;
        _config.SendSystemShortcutsToRemote = enabled;
        var window = EnsureDesktopWindow();
        window.RdpHost.SetSendSystemShortcutsToRemote(enabled);
        var target = enabled ? "桌面分身" : "本机";
        return ReconnectForRdpSettingChange(
            window,
            RdpSettingChange.SystemShortcuts,
            $"系统组合键已改为在{target}生效");
    }

    public bool SetAudioMuted(bool muted)
    {
        ThrowIfDisposed();
        if (AudioMuted == muted)
        {
            return false;
        }

        _config.AudioMuted = muted;
        var window = EnsureDesktopWindow();
        window.RdpHost.SetAudioMuted(muted);
        return ReconnectForRdpSettingChange(
            window,
            RdpSettingChange.Audio,
            muted ? "桌面分身声音已关闭" : "桌面分身声音已开启");
    }

    public void SetGameMouseModeEnabled(bool enabled)
    {
        ThrowIfDisposed();
        _instanceService.SetGameMouseModeEnabled(enabled);
        _config.GameMouseModeEnabled = enabled;
        RefreshState(enabled
            ? "已切换为游戏鼠标模式"
            : "已切换为普通鼠标模式");
    }

    public void SetWatchModeEnabled(bool enabled)
    {
        ThrowIfDisposed();
        _config.WatchModeEnabled = enabled;
        _desktopWindow?.RdpHost.SetInputBlocked(enabled);
        RefreshState(enabled
            ? "已进入观看模式，本机鼠标和键盘不会进入桌面分身"
            : "已退出观看模式，可以操作桌面分身");
    }

    public Task LaunchBetterGiAsync()
    {
        ThrowIfDisposed();
        return LaunchBetterGiCoreAsync(isAutomatic: false, _disposeCancellationTokenSource.Token);
    }

    public bool IsRelativeMouseForwardingAvailable()
    {
        return _desktopWindow?.IsVisible == true
               && _desktopWindow.RdpHost.IsInputWindowFocused();
    }

    public bool TryGetRelativeMouseCaptureBounds(out DrawingRectangle bounds)
    {
        bounds = DrawingRectangle.Empty;
        if (!IsRelativeMouseForwardingAvailable() || _desktopWindow is null)
        {
            return false;
        }

        var rdpHost = _desktopWindow.RdpHost;
        bounds = rdpHost.RectangleToScreen(rdpHost.ClientRectangle);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    public async Task LaunchExecutableAsync(string executablePath)
    {
        ThrowIfDisposed();
        var childSessionId = GetRequiredChildSessionId();

        await _launchSemaphore.WaitAsync(_disposeCancellationTokenSource.Token);
        try
        {
            ThrowIfDisposed();
            RefreshState($"正在以管理员权限启动 {System.IO.Path.GetFileName(executablePath)}");
            await ChildSessionProcessLauncher.LaunchElevatedAsync(childSessionId, executablePath);
            RefreshState(
                $"已在桌面分身（会话 {childSessionId}）中以管理员权限启动 {System.IO.Path.GetFileName(executablePath)}");
        }
        finally
        {
            _launchSemaphore.Release();
        }
    }

    /// <summary>
    /// 断开 RDP、注销桌面分身会话并隐藏桌面分身窗口。
    /// </summary>
    /// <param name="expectedSessionId">需要注销的预期会话标识；为空时注销当前桌面分身会话。</param>
    public async Task LogoffAndHideAsync(uint? expectedSessionId = null)
    {
        ThrowIfDisposed();

        await _launchSemaphore.WaitAsync(_disposeCancellationTokenSource.Token);
        try
        {
            ThrowIfDisposed();
            var activeSessionId = ChildSessionNativeMethods.TryGetChildSessionId();
            if (expectedSessionId is { } expected
                && activeSessionId is { } active
                && active != expected)
            {
                throw new InvalidOperationException(
                    $"桌面分身会话已从 {expected} 变更为 {active}，已取消注销操作。");
            }

            // 完成目标会话校验后再取消连接和重试状态，避免旧菜单项影响刚创建的新会话。
            _isLoggingOff = true;
            _autoLaunchBetterGiPending = false;
            _initialConnectionRetriesRemaining = 0;
            _connectionRetryInProgress = false;
            _pendingRdpSettingChanges = RdpSettingChange.None;
            _rdpSettingsReconnectRetriesRemaining = 0;
            _rdpSettingsReconnectRetryInProgress = false;
            _connectionAttemptCompletionSource?.TrySetResult(false);

            // 先取消并等待启动前修复和登录后观察退出，避免注销后又连接或重启 UU。
            await CancelStartupCycle();
            ThrowIfDisposed();

            TryDisconnectRdpHost();
            RefreshState("正在断开 RDP 并注销 BetterGI 桌面分身");

            var terminatedSessionId = await Task.Run(() =>
                expectedSessionId is { } targetSessionId
                    ? ChildSessionNativeMethods.TerminateChildSession(targetSessionId)
                    : ChildSessionNativeMethods.TerminateChildSession());
            _desktopWindow?.Hide();
            RefreshState(terminatedSessionId is null
                ? "当前没有桌面分身会话，桌面分身窗口已隐藏"
                : $"桌面分身会话 {terminatedSessionId.Value} 已注销，桌面分身窗口已隐藏");
        }
        finally
        {
            _isLoggingOff = false;
            _launchSemaphore.Release();
        }
    }

    public void RefreshState(string? operationMessage = null)
    {
        if (_disposed)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(operationMessage))
        {
            _lastOperationMessage = operationMessage;
        }

        try
        {
            var enabled = ChildSessionNativeMethods.IsChildSessionsEnabled();
            ChildSessionId = ChildSessionNativeMethods.TryGetChildSessionId();
            ConnectedState = _desktopWindow?.RdpHost.ConnectedState ?? 0;

            var connectionText = ConnectedState switch
            {
                0 => "未连接",
                1 => "已连接",
                2 => "正在连接",
                _ => $"未知连接状态 {ConnectedState}"
            };
            var sessionText = ChildSessionId?.ToString() ?? "无";
            var mainText = _lastOperationMessage ?? connectionText;

            StatusText =
                $"{mainText} | RDP：{connectionText} | 桌面分身会话：{sessionText} | 功能已启用：{enabled}";
        }
        catch (Exception exception) when (IsExpectedChildSessionException(exception))
        {
            StatusText = exception.GetBaseException().Message;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _statusTimer.Stop();
        _statusTimer.Tick -= OnStatusTimerTick;
        _uuRemoteSessionRepairService.RepairStateChanged -= OnUuRemoteRepairStateChanged;
        _disposeCancellationTokenSource.Cancel();
        var startupCleanup = CancelStartupCycle();
        _autoLaunchBetterGiPending = false;
        _connectionAttemptCompletionSource?.TrySetCanceled();
        _connectionAttemptCompletionSource = null;
        _initialConnectionRetriesRemaining = 0;
        _pendingRdpSettingChanges = RdpSettingChange.None;
        _rdpSettingsReconnectRetriesRemaining = 0;

        if (_desktopWindow is not null)
        {
            _desktopWindow.IsVisibleChanged -= OnDesktopWindowVisibilityChanged;
            _desktopWindow.RdpHost.ConnectionFailed -= OnRdpConnectionFailed;
            _desktopWindow.RdpHost.LoginCompleted -= OnRdpLoginCompleted;
            TryDisconnectRdpHost();
        }

        if (_instanceService.Context.IsRoot)
        {
            try
            {
                _ = ChildSessionNativeMethods.TerminateChildSession(wait: false);
            }
            catch (Exception exception) when (IsExpectedChildSessionException(exception))
            {
                // 应用正在退出，Child Session 清理失败不应阻止主程序关闭。
            }
        }

        if (_desktopWindow is not null)
        {
            _desktopWindow.AllowClose = true;
            _desktopWindow.Close();
            _desktopWindow = null;
        }

        _ = DisposeLaunchSemaphoreAsync();
        _ = DisposeStartupCycleSourceAsync(_disposeCancellationTokenSource, startupCleanup, null);
    }

    private void ConnectCore(string operationMessage)
    {
        var existingSessionId = ChildSessionNativeMethods.TryGetChildSessionId();
        var window = EnsureDesktopWindow();
        ShowDesktopWindow(window);

        if (window.RdpHost.ConnectedState == 0)
        {
            _autoLaunchBetterGiPending = existingSessionId is null;
            _initialConnectionRetriesRemaining = _autoLaunchBetterGiPending
                ? InitialConnectionRetryCount
                : 0;
            window.RdpHost.ConnectToChildSession(DefaultDesktopSize);
        }

        RefreshState(operationMessage);
    }

    private ChildSessionWindow EnsureDesktopWindow()
    {
        if (_desktopWindow is not null)
        {
            return _desktopWindow;
        }

        _desktopWindow = _serviceProvider.GetRequiredService<ChildSessionWindow>();
        _desktopWindow.IsVisibleChanged += OnDesktopWindowVisibilityChanged;
        _desktopWindow.RdpHost.ConnectionFailed += OnRdpConnectionFailed;
        _desktopWindow.RdpHost.LoginCompleted += OnRdpLoginCompleted;
        _desktopWindow.RdpHost.SetSmartSizing(_config.SmartSizingEnabled);
        _desktopWindow.RdpHost.SetSendSystemShortcutsToRemote(SendSystemShortcutsToRemote);
        _desktopWindow.RdpHost.SetAudioMuted(AudioMuted);
        return _desktopWindow;
    }

    private static void ShowDesktopWindow(Window window)
    {
        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }

    private void OnStatusTimerTick(object? sender, EventArgs e)
    {
        if (_statusTickInProgress || _disposed)
        {
            return;
        }

        _statusTickInProgress = true;
        try
        {
            RefreshState();
        }
        catch (Exception exception) when (IsExpectedChildSessionException(exception))
        {
            RefreshState(exception.GetBaseException().Message);
        }
        finally
        {
            _statusTickInProgress = false;
        }
    }

    /// <summary>排队自动启动时使用所属启动周期的令牌，注销后不能再启动到新会话中。</summary>
    private async Task LaunchBetterGiCoreAsync(bool isAutomatic, CancellationToken cancellationToken)
    {
        await _launchSemaphore.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            var childSessionId = GetRequiredChildSessionId();
            RefreshState(isAutomatic
                ? "桌面分身已加载，正在自动以管理员权限启动 BetterGI"
                : "正在以管理员权限启动 BetterGI");
            await ChildSessionProcessLauncher.LaunchBetterGiAsync(childSessionId);
            RefreshState(
                $"已在桌面分身（会话 {childSessionId}）中以管理员权限启动 BetterGI");
        }
        finally
        {
            _launchSemaphore.Release();
        }
    }

    /// <summary>
    /// 取当前桌面分身会话 ID；没有可用会话时抛出 <see cref="InvalidOperationException"/>。
    /// </summary>
    private uint GetRequiredChildSessionId()
    {
        var childSessionId = ChildSessionNativeMethods.TryGetChildSessionId();
        if (childSessionId is null)
        {
            throw new InvalidOperationException("当前没有可用的桌面分身，请先启动桌面分身。");
        }

        return childSessionId.Value;
    }

    /// <summary>
    /// 收集 Windows 环境结论，供界面在启动前提示用户，也供失败诊断使用。
    /// 这些检查全部是只读的，不会修改系统设置。
    /// </summary>
    public void RefreshEnvironmentCheck()
    {
        ThrowIfDisposed();
        _environmentIssues = ChildSessionEnvironmentCheck.Collect();

        foreach (var issue in _environmentIssues)
        {
            _logger.LogWarning(
                "桌面分身环境预检：{IssueTitle}。{IssueMessage}",
                issue.Title,
                issue.Message);
        }
    }

    private void EnsureChildSessionsEnabled()
    {
        if (!ChildSessionNativeMethods.IsChildSessionsEnabled())
        {
            ChildSessionNativeMethods.EnableChildSessions();
        }
    }

    private void OnDesktopWindowVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        RefreshState();
    }

    /// <summary>退出后不再将后台通知投递到已经关闭的窗口。</summary>
    private void OnUuRemoteRepairStateChanged(
        object? sender,
        UuRemoteRepairStateChangedEventArgs e)
    {
        if (!_disposed)
        {
            UuRemoteRepairStateChanged?.Invoke(this, e);
        }
    }

    /// <summary>一次启动只观察一次；自动重连后的登录事件不重启观察任务。</summary>
    private void StartUuRemoteStartupMonitor()
    {
        if (!_uuRemoteStartupMonitorPending
            || !_uuRemoteSessionRepairService.IsEnabled
            || _uuRemoteStartupMonitorTask is { IsCompleted: false }
            || _startupCancellationTokenSource is not { IsCancellationRequested: false }
            || _disposed)
        {
            return;
        }

        _uuRemoteStartupMonitorPending = false;
        _uuRemoteStartupMonitorTask = MonitorUuRemoteStartupAsync(_startupCancellationTokenSource.Token);
    }

    /// <summary>启动后发现首次冲突时修复，并在会话被断开后恢复 RDP；再次冲突只提示。</summary>
    private async Task MonitorUuRemoteStartupAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _uuRemoteSessionRepairService.CheckAfterStartAsync(
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!result.WasAttempted || _disposed)
            {
                return;
            }

            _logger.LogInformation(
                "桌面分身启动后的 UU 检查完成。是否成功：{IsSuccessful}，原因：{Reason}。",
                result.IsSuccessful,
                result.FailureReason);

            if (result.DisconnectedUserSessions || _desktopWindow?.RdpHost.ConnectedState != 1)
            {
                RefreshState("UU 远程会话已处理，正在重新连接桌面分身");
                _desktopWindow?.RdpHost.ReconnectToChildSession(DefaultDesktopSize);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "桌面分身启动后的 UU 远程会话监控失败");
        }
    }

    /// <summary>取消整个周期，并返回等待任务退出及释放取消源的任务。</summary>
    private Task CancelStartupCycle()
    {
        _uuRemoteStartupMonitorPending = false;
        var monitorTask = _uuRemoteStartupMonitorTask;
        var cancellationTokenSource = _startupCancellationTokenSource;
        _startupCancellationTokenSource = null;
        _uuRemoteStartupMonitorTask = null;
        cancellationTokenSource?.Cancel();
        return DisposeStartupCycleSourceAsync(cancellationTokenSource, _startupTask, monitorTask);
    }

    /// <summary>任务仍可能注册取消回调，只有启动和观察都退出后才能释放取消源。</summary>
    private static async Task DisposeStartupCycleSourceAsync(
        CancellationTokenSource? cancellationTokenSource,
        Task? startupTask,
        Task? monitorTask)
    {
        foreach (var task in new[] { startupTask, monitorTask })
        {
            if (task is null)
            {
                continue;
            }
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 启动异常由调用方提示，监控内部负责记录；这里只等待结束并观测取消。
            }
        }

        cancellationTokenSource?.Dispose();
    }

    /// <summary>注销可能仍在等待修复退出，须等持锁操作释放后再销毁信号量。</summary>
    private async Task DisposeLaunchSemaphoreAsync()
    {
        await _launchSemaphore.WaitAsync().ConfigureAwait(false);
        _launchSemaphore.Dispose();
    }

    private void OnRdpConnectionFailed(
        object? sender,
        ChildSessionConnectionFailedEventArgs e)
    {
        if (_disposed || _isLoggingOff)
        {
            return;
        }

        _lastConnectionFailure = e;

        if (_pendingRdpSettingChanges != RdpSettingChange.None)
        {
            if (_rdpSettingsReconnectRetryInProgress)
            {
                return;
            }

            if (_rdpSettingsReconnectRetriesRemaining > 0)
            {
                RetryRdpSettingsReconnectAsync(e);
                return;
            }

            _pendingRdpSettingChanges = RdpSettingChange.None;
        }

        if (_autoLaunchBetterGiPending
            && _initialConnectionRetriesRemaining > 0
            && !_connectionRetryInProgress)
        {
            RetryInitialConnectionAsync(e);
            return;
        }

        CompleteConnectionFailure(e);
    }

    // RDP 报告连接成功后稍作等待，让新 Child Session 的桌面初始化完成。
    // 使用 OnLoginComplete 等待实际登录完成，避免依赖固定时长的延时。
    private async void OnRdpLoginCompleted(object? sender, EventArgs e)
    {
        if (_disposed || _isLoggingOff)
        {
            return;
        }

        var cancellationToken = _startupCancellationTokenSource?.Token ?? _disposeCancellationTokenSource.Token;
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        _lastConnectionFailure = null;
        _initialConnectionRetriesRemaining = 0;
        _connectionRetryInProgress = false;
        _connectionAttemptCompletionSource?.TrySetResult(true);

        // 输入窗口每次连接都会新建，观看模式需要重新施加到新窗口上。
        _desktopWindow?.RdpHost.SetInputBlocked(WatchModeEnabled);

        var completedSettingChanges = _pendingRdpSettingChanges;
        if (completedSettingChanges != RdpSettingChange.None)
        {
            _pendingRdpSettingChanges = RdpSettingChange.None;
            _rdpSettingsReconnectRetriesRemaining = 0;
            _rdpSettingsReconnectRetryInProgress = false;
            RefreshState("自动重新连接完成，RDP 设置已生效");

            if (completedSettingChanges.HasFlag(RdpSettingChange.SystemShortcuts))
            {
                SystemShortcutsReconnectCompleted?.Invoke(this, EventArgs.Empty);
            }

            if (completedSettingChanges.HasFlag(RdpSettingChange.Audio))
            {
                AudioReconnectCompleted?.Invoke(this, EventArgs.Empty);
            }
        }
        else
        {
            RefreshState("桌面分身登录初始化完成");
        }

        StartUuRemoteStartupMonitor();

        if (!_autoLaunchBetterGiPending)
        {
            return;
        }

        _autoLaunchBetterGiPending = false;
        await Task.Yield();
        if (cancellationToken.IsCancellationRequested || _disposed)
        {
            return;
        }

        try
        {
            RefreshState();
            if (ConnectedState == 1 && ChildSessionId is not null)
            {
                await LaunchBetterGiCoreAsync(isAutomatic: true, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 用户已注销或重新启动，本次登录事件不能继续自动启动 BetterGI。
        }
        catch (Exception exception) when (IsExpectedChildSessionException(exception))
        {
            RefreshState($"自动启动 BetterGI 失败：{exception.GetBaseException().Message}");
        }
    }

    private async void RetryRdpSettingsReconnectAsync(
        ChildSessionConnectionFailedEventArgs firstFailure)
    {
        var cancellationToken = _startupCancellationTokenSource?.Token ?? _disposeCancellationTokenSource.Token;
        _rdpSettingsReconnectRetryInProgress = true;
        ChildSessionConnectionFailedEventArgs? retryFailure = null;
        var retryNumber = RdpSettingsReconnectRetryCount
                          - _rdpSettingsReconnectRetriesRemaining
                          + 1;
        _rdpSettingsReconnectRetriesRemaining--;
        var retryDelay = TimeSpan.FromSeconds(1 << retryNumber);

        _logger.LogWarning(
            "RDP 设置切换后的自动重连失败，将在 {RetryDelaySeconds} 秒后进行第 {RetryNumber} 次重试。"
            + "错误：{ErrorMessage}，错误代码：{ErrorCode}，扩展错误代码：{ExtendedErrorCode}",
            retryDelay.TotalSeconds,
            retryNumber,
            firstFailure.Message,
            firstFailure.ErrorCode,
            firstFailure.ExtendedErrorCode);
        RefreshState(
            $"RDP 自动重连暂未成功，{retryDelay.TotalSeconds:0} 秒后重试"
            + $"（{retryNumber}/{RdpSettingsReconnectRetryCount}）");

        try
        {
            await Task.Delay(retryDelay, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed || _pendingRdpSettingChanges == RdpSettingChange.None)
            {
                return;
            }

            RefreshState(
                $"正在重试 RDP 自动连接（{retryNumber}/{RdpSettingsReconnectRetryCount}）");
            _rdpSettingsReconnectRetryInProgress = false;
            EnsureDesktopWindow().RdpHost.ReconnectToChildSession(DefaultDesktopSize);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedChildSessionException(exception))
        {
            var actualException = exception.GetBaseException();
            var errorCode = actualException is COMException comException
                ? comException.ErrorCode
                : 0;
            retryFailure = new ChildSessionConnectionFailedEventArgs(
                $"RDP 自动重新连接失败：{actualException.Message}",
                errorCode);
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                _rdpSettingsReconnectRetryInProgress = false;
            }
        }

        if (retryFailure is not null && !cancellationToken.IsCancellationRequested)
        {
            OnRdpConnectionFailed(this, retryFailure);
        }
    }

    private async void RetryInitialConnectionAsync(ChildSessionConnectionFailedEventArgs firstFailure)
    {
        var cancellationToken = _startupCancellationTokenSource?.Token ?? _disposeCancellationTokenSource.Token;
        _connectionRetryInProgress = true;
        ChildSessionConnectionFailedEventArgs? retryFailure = null;
        var retryNumber = InitialConnectionRetryCount - _initialConnectionRetriesRemaining + 1;
        _initialConnectionRetriesRemaining--;
        var retryDelay = TimeSpan.FromSeconds(1 << (retryNumber - 1));

        _logger.LogWarning(
            "桌面分身首次初始化连接失败，将在 {RetryDelaySeconds} 秒后进行第 {RetryNumber} 次重试。"
            + "错误：{ErrorMessage}，错误代码：{ErrorCode}，扩展错误代码：{ExtendedErrorCode}",
            retryDelay.TotalSeconds,
            retryNumber,
            firstFailure.Message,
            firstFailure.ErrorCode,
            firstFailure.ExtendedErrorCode);
        RefreshState(
            $"桌面分身首次初始化尚未完成，{retryDelay.TotalSeconds:0} 秒后自动重试"
            + $"（{retryNumber}/{InitialConnectionRetryCount}）");

        try
        {
            await Task.Delay(retryDelay, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed || !_autoLaunchBetterGiPending)
            {
                return;
            }

            RefreshState();
            if (_desktopWindow is null)
            {
                return;
            }

            RefreshState(
                $"正在重试桌面分身连接（{retryNumber}/{InitialConnectionRetryCount}）");
            _connectionRetryInProgress = false;
            // OnLogonError 触发后 ActiveX 可能仍处于连接状态，必须先断开再重连。
            // 否则这里直接退出会丢失已捕获的真实错误，最终只剩外层连接超时。
            _desktopWindow.RdpHost.ReconnectToChildSession(DefaultDesktopSize);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedChildSessionException(exception))
        {
            var actualException = exception.GetBaseException();
            var errorCode = actualException is COMException comException
                ? comException.ErrorCode
                : 0;
            retryFailure = new ChildSessionConnectionFailedEventArgs(
                $"重试桌面分身连接失败：{actualException.Message}",
                errorCode);
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                _connectionRetryInProgress = false;
            }
        }

        if (retryFailure is not null && !cancellationToken.IsCancellationRequested)
        {
            OnRdpConnectionFailed(this, retryFailure);
        }
    }

    /// <summary>
    /// 连接失败的统一收尾：「重试耗尽」与「连接超时」两条路径都经由这里附加诊断信息，
    /// 再更新状态并通知订阅者。
    /// </summary>
    private void CompleteConnectionFailure(ChildSessionConnectionFailedEventArgs e)
    {
        // 统一在这里补充诊断，让「重试耗尽」和「连接超时」两条失败路径给出同样的信息。
        var diagnosed = new ChildSessionConnectionFailedEventArgs(
            $"{e.Message}{BuildFailureDiagnosis()}",
            e.ErrorCode,
            e.ExtendedErrorCode);
        _lastConnectionFailure = diagnosed;
        _autoLaunchBetterGiPending = false;
        _initialConnectionRetriesRemaining = 0;
        _uuRemoteStartupMonitorPending = false;
        _connectionAttemptCompletionSource?.TrySetResult(false);
        _logger.LogError(
            "桌面分身 RDP 连接失败：{ErrorMessage}，错误代码：{ErrorCode}，扩展错误代码：{ExtendedErrorCode}",
            diagnosed.Message,
            diagnosed.ErrorCode,
            diagnosed.ExtendedErrorCode);
        RefreshState(diagnosed.Message);
        ConnectionFailed?.Invoke(this, diagnosed);
    }

    /// <summary>
    /// 构造连接超时的失败信息，并附上 RDP ActiveX 最后报告的失败原因（如果有）。
    /// </summary>
    private ChildSessionConnectionFailedEventArgs CreateConnectionTimeoutFailure()
    {
        var timeoutMessage =
            $"桌面分身连接及登录初始化未能在 {ConnectionTimeout.TotalSeconds:0} 秒内完成。";
        var lastDiagnostic =
            _desktopWindow?.RdpHost.LastConnectionDiagnostic
            ?? _lastConnectionFailure;
        if (lastDiagnostic is null)
        {
            return new ChildSessionConnectionFailedEventArgs(
                $"{timeoutMessage}\n\nRDP ActiveX 未报告更具体的失败原因。",
                ErrorTimeout);
        }

        return new ChildSessionConnectionFailedEventArgs(
            $"{timeoutMessage}\n\nRDP ActiveX 最后报告：\n{lastDiagnostic.Message}",
            lastDiagnostic.ErrorCode,
            lastDiagnostic.ExtendedErrorCode);
    }

    /// <summary>
    /// 连接失败后的补充诊断：报告是否存在桌面分身会话，并附上环境预检结论。
    /// RDP ActiveX 自身只回报「发生内部错误」和断开原因，单独看这些信息无法定位问题。
    /// </summary>
    private string BuildFailureDiagnosis()
    {
        var builder = new StringBuilder();

        if (ChildSessionNativeMethods.TryGetChildSessionId() is null)
        {
            builder.AppendLine().AppendLine();
            builder.Append(
                "诊断：当前没有可用的桌面分身会话，说明 Windows 未能建立 RDP 会话。"
                + "该状态本身无法确定原因——系统无法创建会话与凭据未通过验证都会得到这个结果，"
                + "请结合上面的 RDP ActiveX 错误信息一并判断。");
        }

        if (_environmentIssues.Count > 0)
        {
            builder.AppendLine().AppendLine();
            builder.Append(ChildSessionEnvironmentCheck.BuildSummary(_environmentIssues));
        }

        return builder.ToString();
    }

    /// <summary>
    /// 尝试断开 RDP 主机连接。ActiveX 正在自行断开时可能返回 COM 错误，忽略后仍继续注销 Child Session。
    /// </summary>
    private void TryDisconnectRdpHost()
    {
        try
        {
            _desktopWindow?.RdpHost.DisconnectSession();
        }
        catch (Exception exception) when (exception is COMException or TargetInvocationException)
        {
            // ActiveX 正在自行断开时可能返回 COM 错误，仍可继续注销 Child Session。
        }
    }

    private static bool IsExpectedChildSessionException(Exception exception)
    {
        return exception is Win32Exception
            or COMException
            or EntryPointNotFoundException
            or TargetInvocationException
            or InvalidCastException
            or InvalidOperationException
            or FileNotFoundException
            or ArgumentException;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private bool ReconnectForRdpSettingChange(
        ChildSessionWindow window,
        RdpSettingChange settingChange,
        string operationMessage)
    {
        RefreshState();
        if (window.RdpHost.ConnectedState == 0 && ChildSessionId is null)
        {
            RefreshState($"{operationMessage}，将在下次 RDP 连接后应用");
            return false;
        }

        var previousSettingChanges = _pendingRdpSettingChanges;
        _pendingRdpSettingChanges |= settingChange;
        _rdpSettingsReconnectRetriesRemaining = RdpSettingsReconnectRetryCount;
        try
        {
            window.RdpHost.ReconnectToChildSession(DefaultDesktopSize);
        }
        catch
        {
            _pendingRdpSettingChanges = previousSettingChanges;
            if (_pendingRdpSettingChanges == RdpSettingChange.None)
            {
                _rdpSettingsReconnectRetriesRemaining = 0;
            }

            throw;
        }

        RefreshState($"{operationMessage}，正在自动重新连接 RDP");
        return true;
    }

    [Flags]
    private enum RdpSettingChange
    {
        None = 0,
        SystemShortcuts = 1,
        Audio = 2
    }
}
