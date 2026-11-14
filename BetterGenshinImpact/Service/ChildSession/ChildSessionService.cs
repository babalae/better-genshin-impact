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

    /// <summary>
    /// 设置切换后的自动重连次数。
    ///
    /// 断开之后立刻重连，经常被服务端用「访问被拒绝」顶回来，隔几秒再连就能成功。
    /// 重试不够时，用户看到的是「第一次必定失败、手动再点一次才行」，所以给足三次（2s / 4s / 8s）。
    /// </summary>
    private const int RdpSettingsReconnectRetryCount = 3;
    private const int ErrorTimeout = 1460;

    private readonly IServiceProvider _serviceProvider;
    private readonly InstanceService _instanceService;
    private readonly ChildSessionConfig _config;
    private readonly ChildSessionAutoLoginService _autoLogin;
    private readonly ILogger<ChildSessionService> _logger;
    private readonly DispatcherTimer _statusTimer;
    private readonly SemaphoreSlim _launchSemaphore = new(1, 1);
    private readonly CancellationTokenSource _disposeCancellationTokenSource = new();

    private ChildSessionWindow? _desktopWindow;
    private bool _autoLaunchBetterGiPending;
    private TaskCompletionSource<bool>? _connectionAttemptCompletionSource;
    private ChildSessionConnectionFailedEventArgs? _lastConnectionFailure;
    private int _initialConnectionRetriesRemaining;
    private bool _connectionRetryInProgress;
    private RdpSettingChange _pendingRdpSettingChanges;
    private int _rdpSettingsReconnectRetriesRemaining;

    /// <summary>
    /// 设置重连重试的代际号。注销/释放时自增，让还在 <c>Task.Delay</c> 里等的那次重试作废——
    /// 否则它醒来后会把刚断开的分身又连回来。
    /// 只作废这一次重试，不清 <see cref="_pendingRdpSettingChanges"/>：
    /// 待应用的设置要留给下一次成功登录去收尾。
    /// </summary>
    private int _settingsReconnectGeneration;

    /// <summary>
    /// 注销与释放会自增它。启动流程里有 await（等远程桌面服务起来可能要几秒），
    /// 期间用户点了注销或程序在退出，这一段就该作废，不能再把分身连回来。
    /// </summary>
    private int _lifecycleGeneration;
    private bool _rdpSettingsReconnectRetryInProgress;
    private bool _statusTickInProgress;
    private bool _disposed;
    private string? _lastOperationMessage;
    private IReadOnlyList<ChildSessionEnvironmentCheck.Issue> _environmentIssues = [];

    public event EventHandler? StateChanged;

    public event EventHandler<ChildSessionConnectionFailedEventArgs>? ConnectionFailed;

    public event EventHandler? SystemShortcutsReconnectCompleted;

    public event EventHandler? AudioReconnectCompleted;

    public string StatusText { get; private set; } = "桌面分身尚未启动";

    public bool IsDesktopVisible => _desktopWindow?.IsVisible == true;

    public int ConnectedState { get; private set; }

    public uint? ChildSessionId { get; private set; }

    public bool SendSystemShortcutsToRemote { get; private set; } = true;

    public bool IsGameMouseModeEnabled => _instanceService.IsGameMouseModeEnabled;

    public bool TopmostEnabled => _config.TopmostEnabled;

    public bool SmartSizingEnabled => _config.SmartSizingEnabled;

    public bool KeepAspectRatio => _config.KeepAspectRatio;

    public bool AudioMuted => _config.AudioMuted;

    public bool WatchModeEnabled => _config.WatchModeEnabled;

    /// <summary>
    /// 是否在连接时自动填写登录凭据。
    /// </summary>
    internal bool AutoLoginEnabled
    {
        get => _autoLogin.IsEnabled;
        set => _autoLogin.IsEnabled = value;
    }

    /// <summary>
    /// 全屏时浮层工具栏是否固定显示（不随鼠标离开自动收起）。写入设置，与窗口置顶无关。
    /// </summary>
    internal bool FullscreenBarPinned
    {
        get => _config.FullscreenBarPinned;
        set => _config.FullscreenBarPinned = value;
    }

    /// <summary>
    /// 自动登录使用的账户名：固定为当前 Windows 账户（桌面分身只支持当前账户登录）。
    /// </summary>
    internal string AutoLoginAccountName => _autoLogin.UserName;

    /// <summary>
    /// 当前账户是否已经存过密码。
    /// </summary>
    internal bool HasStoredAutoLoginPassword => _autoLogin.HasStoredPassword;

    /// <summary>
    /// 保存当前账户的登录密码（DPAPI 加密后写入配置）。
    /// </summary>
    internal bool SaveAutoLoginPassword(string password)
    {
        ThrowIfDisposed();
        return _autoLogin.SavePassword(password);
    }

    /// <summary>
    /// 清除当前账户已保存的密码。
    /// </summary>
    internal void ClearAutoLoginPassword()
    {
        ThrowIfDisposed();
        _autoLogin.ClearPassword();
    }

    /// <summary>
    /// 校验密码是否有效（交互式登录验证，不产生界面）。
    /// </summary>
    internal ChildSessionAutoLoginService.PasswordCheckResult VerifyAutoLoginPassword(
        string password,
        out int errorCode)
    {
        ThrowIfDisposed();
        return _autoLogin.VerifyPassword(password, out errorCode);
    }

    /// <summary>
    /// 状态行里描述远程桌面服务的那一段：正常返回空串，异常时追加「 | TermService：未启动/已禁用」。
    ///
    /// 只在「已停止 / 已禁用」时出现——这是「点了启动也没反应」最常见的原因，
    /// 放在状态行里比藏在诊断面板更容易被看见，而且正常时不占位。
    /// </summary>
    private static string DescribeTermServiceForStatus()
    {
        var (state, startType) = ChildSessionEnvironmentCheck.QueryTermService();

        if (startType == "Disabled")
        {
            return " | TermService：已禁用";
        }

        return state is "Stopped" or "StopPending" ? " | TermService：未启动" : string.Empty;
    }

    /// <summary>
    /// 用户点「启动」时，把「已停止」的远程桌面服务拉起来。
    ///
    /// 只处理「启动类型正常、但服务没在运行」这一种情况：被禁用的服务不在这里动，
    /// 那需要改启动类型，属于用户的显式选择，交给诊断面板处理。
    /// </summary>
    /// <returns>本次是否真的把服务启动起来了。</returns>
    internal async Task<bool> TryStartTermServiceIfStoppedAsync()
    {
        // 查询与启动都在工作线程上做：TryStart 内部会用 Sleep 轮询等 SCM，
        // 直接放在 UI 线程上会让窗口在服务启动期间整段卡住。
        var startRequested = await Task.Run(() =>
        {
            var (state, startType) = ChildSessionEnvironmentCheck.QueryTermService();
            if (startType == "Disabled" || state is not ("Stopped" or "StopPending"))
            {
                return false;
            }

            if (ChildSessionServiceNativeMethods.TryStart("TermService", out var error))
            {
                return true;
            }

            _logger.LogWarning("启动远程桌面服务失败：{Error}", error);
            return false;
        });

        if (!startRequested)
        {
            return false;
        }

        // 服务启动是异步的：等它真的进入 Running，界面上的状态才不会停在「未启动」。
        const int timeoutSeconds = 5;
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (ChildSessionServiceNativeMethods.TryQueryState("TermService", out var current)
                && current == 4)
            {
                _logger.LogInformation("远程桌面服务已自动启动。");
                return true;
            }

            await Task.Delay(200);
        }

        _logger.LogWarning("远程桌面服务启动后 {Seconds} 秒内未进入运行状态。", timeoutSeconds);
        return false;
    }

    /// <summary>
    /// 给 RDP 控件取凭据的回调。
    /// </summary>
    private RdpCredential? TryGetAutoLoginCredential()
    {
        return _autoLogin.TryGetCredential(out var credential) ? credential : null;
    }

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

    /// <summary>
    /// 系统是否已安装并启用 RDP Wrapper，供用户主动诊断时查询，不参与启动流程。
    /// </summary>
    public bool IsRdpWrapperEnabled()
    {
        // 不能只匹配 rdpwrap.dll：SuperRDP 等改版用的是别的 DLL 名。
        // 判定统一为「ServiceDll 不等于原生 termsrv.dll 就算被接管」。
        return ChildSessionEnvironmentCheck.IsServiceDllHijacked(
            ChildSessionEnvironmentCheck.ReadServiceDll());
    }

    /// <summary>
    /// 最近一次主动环境检查是否发现了可能影响桌面分身的环境设置。
    /// </summary>
    public bool HasEnvironmentIssue => _environmentIssues.Count > 0;

    /// <summary>
    /// 最近一次主动环境检查的结论文本，供界面提示与问题排查使用。
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
        IConfigService configService,
        ILogger<ChildSessionService> logger)
    {
        _serviceProvider = serviceProvider;
        _instanceService = instanceService;
        _config = configService.Get().ChildSessionConfig;
        SendSystemShortcutsToRemote = _config.SendSystemShortcutsToRemote;
        _logger = logger;

        // 密码表存在配置对象里，但它是普通字典，改动不会触发配置跟踪，
        // 所以这里显式给一个保存回调，由自动登录服务在存取密码后调用。
        _autoLogin = new ChildSessionAutoLoginService(_config, configService.Save);
        _statusTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _statusTimer.Tick += OnStatusTimerTick;
        _statusTimer.Start();
        RefreshState();
    }

    /// <summary>
    /// 启动桌面分身：确保 Child Sessions 已启用，然后建立 RDP 连接并等待登录完成。
    /// 启动过程不做环境检查，由用户主动打开诊断面板后再检查。
    /// 已经处于已连接状态时直接返回。
    /// </summary>
    public async Task StartAsync()
    {
        ThrowIfDisposed();
        var generation = _lifecycleGeneration;

        // 用户点「启动」即视为同意拉起远程桌面服务；被禁用的服务不在这里动，
        // 那需要改启动类型，属于用户的显式选择。
        var serviceStarted = await TryStartTermServiceIfStoppedAsync();

        // 等服务的这几秒里可能已经注销或退出：不能接着把分身连回来。
        if (_disposed || generation != _lifecycleGeneration)
        {
            return;
        }

        if (serviceStarted)
        {
            RefreshState("已自动启动远程桌面服务");
        }

        EnsureChildSessionsEnabled();
        RefreshState();

        if (ConnectedState == 1)
        {
            return;
        }

        var completionSource = _connectionAttemptCompletionSource;
        if (completionSource is null || completionSource.Task.IsCompleted)
        {
            completionSource = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _connectionAttemptCompletionSource = completionSource;
            _lastConnectionFailure = null;

            try
            {
                ConnectCore("正在启动 BetterGI 桌面分身");
            }
            catch
            {
                _autoLaunchBetterGiPending = false;
                _initialConnectionRetriesRemaining = 0;

                // 连接在建立阶段就抛了（例如控件拿不到 SecuredSettings2），
                // 监听这时已经启动了却再也不会有人来停。
                _autoLogin.StopWatching();

                completionSource.TrySetResult(false);
                _connectionAttemptCompletionSource = null;
                throw;
            }
        }

        try
        {
            await completionSource.Task.WaitAsync(
                ConnectionTimeout,
                _disposeCancellationTokenSource.Token);
        }
        catch (TimeoutException)
        {
            if (!completionSource.Task.IsCompleted)
            {
                _autoLaunchBetterGiPending = false;
                _initialConnectionRetriesRemaining = 0;
                CompleteConnectionFailure(CreateConnectionTimeoutFailure());
                TryDisconnectRdpHost();
            }
        }
        finally
        {
            if (ReferenceEquals(_connectionAttemptCompletionSource, completionSource))
            {
                _connectionAttemptCompletionSource = null;
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

    /// <summary>
    /// 当前的输入模式。
    ///
    /// 观看模式与游戏鼠标/普通鼠标是互斥的三种状态：观看模式下键鼠根本不进分身，
    /// 再细分鼠标模式没有意义，所以界面上用一个按钮循环切换。
    /// </summary>
    internal ChildSessionInputMode InputMode =>
        WatchModeEnabled ? ChildSessionInputMode.Watch
        : IsGameMouseModeEnabled ? ChildSessionInputMode.GameMouse
        : ChildSessionInputMode.Normal;

    /// <summary>
    /// 切到下一个输入模式：观看模式 → 普通鼠标 → 游戏鼠标 → 观看模式。
    /// </summary>
    internal ChildSessionInputMode CycleInputMode()
    {
        ThrowIfDisposed();
        var next = InputMode switch
        {
            ChildSessionInputMode.Watch => ChildSessionInputMode.Normal,
            ChildSessionInputMode.Normal => ChildSessionInputMode.GameMouse,
            _ => ChildSessionInputMode.Watch
        };

        SetInputMode(next);
        return next;
    }

    /// <summary>
    /// 切到指定输入模式：三种状态互斥，设置一个就复位另外两个。
    /// </summary>
    internal void SetInputMode(ChildSessionInputMode mode)
    {
        ThrowIfDisposed();

        SetGameMouseModeEnabled(mode == ChildSessionInputMode.GameMouse);
        SetWatchModeEnabled(mode == ChildSessionInputMode.Watch);
    }

    public Task LaunchBetterGiAsync()
    {
        ThrowIfDisposed();
        return LaunchBetterGiCoreAsync(isAutomatic: false);
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

        await _launchSemaphore.WaitAsync();
        try
        {
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

    public async Task LogoffAndHideAsync()
    {
        ThrowIfDisposed();
        _autoLaunchBetterGiPending = false;
        _initialConnectionRetriesRemaining = 0;
        _pendingRdpSettingChanges = RdpSettingChange.None;
        _rdpSettingsReconnectRetriesRemaining = 0;

        // 作废在途的自动重连：普通断开不会让它停下，等「会话释放」结束后
        // 它会把刚注销的分身又连回来。
        _settingsReconnectGeneration++;
        _lifecycleGeneration++;
        _connectionAttemptCompletionSource?.TrySetResult(false);

        await _launchSemaphore.WaitAsync();
        try
        {
            TryDisconnectRdpHost();
            RefreshState("正在断开 RDP 并注销 BetterGI 桌面分身");

            var terminatedSessionId = await Task.Run(ChildSessionNativeMethods.TerminateChildSession);
            _desktopWindow?.Hide();
            RefreshState(terminatedSessionId is null
                ? "当前没有桌面分身会话，桌面分身窗口已隐藏"
                : $"桌面分身会话 {terminatedSessionId.Value} 已注销，桌面分身窗口已隐藏");
        }
        finally
        {
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

            // 远程桌面服务正常时不占用这一行；只有在「没启动 / 被禁用」时才追加一段，
            // 让用户在点启动之前就能看出是环境问题、而不是连接问题。
            StatusText =
                $"{mainText} | RDP：{connectionText} | 分身会话：{sessionText} | 子会话：{(enabled ? "开" : "关")}"
                + DescribeTermServiceForStatus();
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
        _lifecycleGeneration++;
        _settingsReconnectGeneration++;
        _statusTimer.Stop();
        _statusTimer.Tick -= OnStatusTimerTick;
        _disposeCancellationTokenSource.Cancel();
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

        _autoLogin.Dispose();
        _launchSemaphore.Dispose();
        _disposeCancellationTokenSource.Dispose();
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

            // 先开监听再连：凭据框在连接过程中就会弹出来，晚一步就错过了。
            _autoLogin.StartWatching();
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
        _desktopWindow.RdpHost.SetCredentialProvider(TryGetAutoLoginCredential);
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

    private async Task LaunchBetterGiCoreAsync(bool isAutomatic)
    {
        await _launchSemaphore.WaitAsync();
        try
        {
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
    /// 用户主动请求检查时收集 Windows 环境结论，供问题诊断使用。
    /// 这些检查全部是只读的，不会修改系统设置。
    /// </summary>
    public void RefreshEnvironmentCheck()
    {
        ThrowIfDisposed();
        _environmentIssues = ChildSessionEnvironmentCheck.Collect();

        foreach (var issue in _environmentIssues)
        {
            _logger.LogWarning(
                "桌面分身环境检查：{IssueTitle}。{IssueSummary}",
                issue.Title,
                issue.Summary);
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

    private void OnRdpConnectionFailed(
        object? sender,
        ChildSessionConnectionFailedEventArgs e)
    {
        _lastConnectionFailure = e;

        // 凭据不对时重试没有意义：每次重试都会用同一个密码再撞一次，
        // 只会把账户往锁定策略上推（锁定阈值常见的就 3～5 次）。直接判失败，交给用户改密码。
        if (e.IsLogonFailure)
        {
            _autoLogin.StopWatching();
            _pendingRdpSettingChanges = RdpSettingChange.None;
            _rdpSettingsReconnectRetriesRemaining = 0;

            // OnLogonError 之后控件可能仍停在「已连接」（状态 1/2），而之后每次连接都会
            // 因为状态非 0 直接跳过——用户改完密码再点启动就毫无反应。断开必须做，
            // 但要排到当前事件回调之后，不能在这里重入控件。
            _desktopWindow?.RdpHost.DisconnectSessionDeferred();

            CompleteConnectionFailure(e);
            return;
        }

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
        _lastConnectionFailure = null;
        _initialConnectionRetriesRemaining = 0;
        _connectionRetryInProgress = false;
        _connectionAttemptCompletionSource?.TrySetResult(true);

        // 登录已经成功，凭据框不会再出现，没必要继续占着轮询。
        _autoLogin.StopWatching();

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

        if (!_autoLaunchBetterGiPending)
        {
            return;
        }

        _autoLaunchBetterGiPending = false;
        await Task.Yield();
        if (_disposed)
        {
            return;
        }

        try
        {
            RefreshState();
            if (ConnectedState == 1 && ChildSessionId is not null)
            {
                await LaunchBetterGiCoreAsync(isAutomatic: true);
            }
        }
        catch (Exception exception) when (IsExpectedChildSessionException(exception))
        {
            RefreshState($"自动启动 BetterGI 失败：{exception.GetBaseException().Message}");
        }
    }

    private async void RetryRdpSettingsReconnectAsync(
        ChildSessionConnectionFailedEventArgs firstFailure)
    {
        _rdpSettingsReconnectRetryInProgress = true;
        ChildSessionConnectionFailedEventArgs? retryFailure = null;
        var retryNumber = RdpSettingsReconnectRetryCount
                          - _rdpSettingsReconnectRetriesRemaining
                          + 1;
        _rdpSettingsReconnectRetriesRemaining--;
        var retryDelay = TimeSpan.FromSeconds(1 << retryNumber);

        // 记下代际：等待期间可能注销、关闭或重新发起连接，那次重试就不该再发起了。
        var generation = _settingsReconnectGeneration;

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
            await Task.Delay(retryDelay, _disposeCancellationTokenSource.Token);
            if (_disposed
                || generation != _settingsReconnectGeneration
                || _pendingRdpSettingChanges == RdpSettingChange.None)
            {
                return;
            }

            RefreshState(
                $"正在重试 RDP 自动连接（{retryNumber}/{RdpSettingsReconnectRetryCount}）");
            _rdpSettingsReconnectRetryInProgress = false;
            EnsureDesktopWindow().RdpHost.ReconnectToChildSession(DefaultDesktopSize);
        }
        catch (OperationCanceledException) when (_disposed)
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
            _rdpSettingsReconnectRetryInProgress = false;
        }

        if (retryFailure is not null)
        {
            OnRdpConnectionFailed(this, retryFailure);
        }
    }

    private async void RetryInitialConnectionAsync(ChildSessionConnectionFailedEventArgs firstFailure)
    {
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
            await Task.Delay(retryDelay, _disposeCancellationTokenSource.Token);
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
        catch (OperationCanceledException) when (_disposed)
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
            _connectionRetryInProgress = false;
        }

        if (retryFailure is not null)
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
        _autoLogin.StopWatching();
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
    /// 连接失败后的补充诊断：报告是否存在桌面分身会话，并附上已有的主动检查结论。
    /// 此处只读取缓存，不触发环境检查。
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
        _autoLogin.StopWatching();
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
            // 改设置会导致重新连接，凭据框有机会再弹一次（例如刚改过 Windows 密码）。
            _autoLogin.StartWatching();
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

/// <summary>
/// 桌面分身的输入模式，三种互斥。
/// </summary>
public enum ChildSessionInputMode
{
    /// <summary>观看模式：本机键鼠不进分身，只显示画面。</summary>
    Watch,

    /// <summary>普通鼠标：本机键鼠正常操作分身。</summary>
    Normal,

    /// <summary>游戏鼠标：把相对鼠标信息转发进分身，供游戏使用。</summary>
    GameMouse
}

