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
    /// <summary>
    /// 桌面分身的固定远端分辨率。全屏时用它和显示器分辨率比对，决定 1:1 还是自适应。
    /// </summary>
    internal static readonly DrawingSize DefaultDesktopSize = new(1920, 1080);
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

    /// <summary>本次连接发起时刻，用于在日志里给出「从点启动到画面出来」的耗时。</summary>
    private DateTime? _connectStartedAt;

    /// <summary>
    /// 会话正在等用户修改 Windows 密码。
    ///
    /// 这种情况下 RDP 画面已经渲染、用户可以点击操作，连接本身是通的，只是登录流程
    /// 停在改密这一步。它不该按「连接超时」判失败——那会 TryDisconnectRdpHost，
    /// 把用户正在操作的改密界面一起关掉。
    /// </summary>
    private volatile bool _awaitingPasswordChange;

    /// <summary>
    /// 本轮启动是否已经重建过 RDP 控件。
    ///
    /// 「由于协议错误，连接失败」这类失败常出在控件内部状态机卡死（上一次连接异常收尾），
    /// 之后无论重试多少次都会以同样方式失败，只有重建控件（窗口）才能恢复——
    /// 否则用户只能重启程序。每轮启动只重建一次，避免掩盖真正的环境问题。
    /// </summary>
    private bool _controlRecreatedForAttempt;

    /// <summary>
    /// 正在重新附着的已有会话号。
    ///
    /// UU 强效切换掐断 RDP 后重连，ActiveX 可能不再发 OnLoginComplete，但会话本身
    /// 还 Active、用户也没注销。记住这个会话号，就能用系统状态确认它其实已经可用。
    /// </summary>
    private uint? _reconnectingSessionId;

    /// <summary>
    /// 控件重建期间暂存的「正在重新附着的会话号」。
    ///
    /// RecreateDesktopWindow 会清掉 _reconnectingSessionId（新控件不该继承旧状态），
    /// 但重建后重连的仍是同一个已有会话：登录完成的两个系统状态兜底都依赖这个号，
    /// 丢掉就会退回「只能等外层超时」的老问题。
    /// </summary>
    private uint? _reconnectingSessionIdAfterRecreate;

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
    /// 保存当前账户的登录密码（写入 Windows 凭据管理器）。
    /// </summary>
    internal bool SaveAutoLoginPassword(string password)
    {
        ThrowIfDisposed();
        return _autoLogin.SavePassword(password);
    }

    /// <summary>
    /// 清除当前账户已保存的密码。
    /// </summary>
    internal bool ClearAutoLoginPassword()
    {
        ThrowIfDisposed();
        return _autoLogin.ClearPassword();
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
        // QueryTermService 要开注册表键 + 查 SCM，若服务正在启动/停止会卡住。
        // 这里读的是工作线程定期刷新的缓存，避免每次状态刷新都同步查一次。
        return _termServiceStatusSuffix;
    }

    /// <summary>TermService 状态行的缓存后缀。只在工作线程刷新，UI 线程只读。</summary>
    private static volatile string _termServiceStatusSuffix = string.Empty;

    /// <summary>状态 tick 计数，用于给 TermService 缓存做节流刷新（每 10 个 tick ≈ 5 秒）。</summary>
    private int _termServiceStatusTickCounter;

    /// <summary>
    /// 是否有一次 TermService 状态查询还在跑。
    ///
    /// QueryTermService 在服务启动/停止期间会卡住，而刷新由 5 秒一次的 tick 触发：
    /// 不加互斥就会堆叠出多个并发查询，先发起的后返回时用旧状态覆盖新状态。
    /// </summary>
    private int _termServiceRefreshInProgress;

    /// <summary>
    /// 在工作线程刷新一次 TermService 状态缓存。启动/停止服务后调用，
    /// 让状态行尽快反映最新状态。
    /// </summary>
    private void RefreshTermServiceStatusCache()
    {
        // 上一次查询还没回来就跳过本轮：堆叠的查询会互相覆盖，且旧结果可能后到。
        if (Interlocked.Exchange(ref _termServiceRefreshInProgress, 1) != 0)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                var (state, startType) = ChildSessionEnvironmentCheck.QueryTermService();
                _termServiceStatusSuffix = startType == "Disabled"
                    ? " | TermService：已禁用"
                    : state is "Stopped" or "StopPending" ? " | TermService：未启动" : string.Empty;
            }
            catch (Exception exception) when (IsExpectedChildSessionException(exception))
            {
                // 读不到就保持上一次的快照，下次再试。
                _logger.LogDebug(exception, "刷新 TermService 状态失败。");
            }
            finally
            {
                Volatile.Write(ref _termServiceRefreshInProgress, 0);
            }
        });
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
            // StartPending：服务已经在启动中，别再发一次 StartService，等它进 Running 即可。
            if (state == "StartPending")
            {
                var startDeadline = DateTime.UtcNow.AddSeconds(10);
                while (DateTime.UtcNow < startDeadline
                       && ChildSessionServiceNativeMethods.TryQueryState("TermService", out var pending)
                       && pending == 2) // 2 = SERVICE_START_PENDING
                {
                    Thread.Sleep(200);
                }

                var (settledState, _) = ChildSessionEnvironmentCheck.QueryTermService();
                return settledState == "Running";
            }

            if (startType == "Disabled" || state is not ("Stopped" or "StopPending"))
            {
                return false;
            }

            // 服务还在停止中时，SCM 会认为它「已在运行」而让 StartService 返回 1056，
            // 之后又没人再拉一次，服务就停在 Stopped。先等它停稳再启动。
            if (state == "StopPending")
            {
                var stopDeadline = DateTime.UtcNow.AddSeconds(10);
                while (DateTime.UtcNow < stopDeadline
                       && ChildSessionServiceNativeMethods.TryQueryState("TermService", out var pending)
                       && pending != 1) // 1 = SERVICE_STOPPED
                {
                    Thread.Sleep(200);
                }
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
                RefreshTermServiceStatusCache();
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
        RefreshTermServiceStatusCache();
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

        // 用户重新发起启动：允许再重建一次控件。
        _controlRecreatedForAttempt = false;

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
                if (_awaitingPasswordChange)
                {
                    // 用户正在改密界面里操作，时间不可控。连接是通的（画面可交互），
                    // 只是登录没完成：不判失败、不断开，等他走完；登录成功会由
                    // OnRdpLoginCompleted 收尾。
                    _logger.LogInformation("连接等待超时，但会话正在等待用户修改密码，继续等待。");
                    return;
                }

                // 已有会话重连：ActiveX 没发登录完成事件，但会话其实已经可用。
                // 这时按超时断开，会把用户已登录的桌面和里面的程序一起清掉。
                if (ConnectedState == 1
                    && _reconnectingSessionId is { } timedOutSession
                    && ChildSessionId == timedOutSession
                    && ChildSessionNativeMethods.IsSessionLoggedOnAndActive(timedOutSession))
                {
                    _logger.LogInformation(
                        "连接等待超时，但会话 {SessionId} 已恢复且已登录，按登录完成收尾。",
                        timedOutSession);
                    OnRdpLoginCompleted(this, EventArgs.Empty);
                    return;
                }

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
        // 切回控制模式时 SetInputBlocked 会把焦点还给输入窗口：观看模式期间
        // 被系统移走的焦点不会自动回来，画面看着像还在观看模式。
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
    ///
    /// 直接写配置与输入窗口状态，最后只刷一次状态行：分开走 SetGameMouseModeEnabled /
    /// SetWatchModeEnabled 会各刷一次，第一次刷出的还是中间态（例如切到观看模式时
    /// 先关游戏鼠标，此时 InputMode 读到的是普通鼠标），订阅方会看到两个不一致的通知。
    /// </summary>
    internal void SetInputMode(ChildSessionInputMode mode)
    {
        ThrowIfDisposed();

        var gameMouse = mode == ChildSessionInputMode.GameMouse;
        _instanceService.SetGameMouseModeEnabled(gameMouse);
        _config.GameMouseModeEnabled = gameMouse;

        var watch = mode == ChildSessionInputMode.Watch;
        _config.WatchModeEnabled = watch;
        // 切回控制模式时 SetInputBlocked 会把焦点还给输入窗口：观看模式期间
        // 被系统移走的焦点不会自动回来，画面看着像还在观看模式。
        _desktopWindow?.RdpHost.SetInputBlocked(watch);

        // 切到控制类模式（普通/游戏）时要显式还焦点：SetInputBlocked 只在
        // 「观看模式解除」时才还，从普通切游戏时它不会动焦点，而游戏模式
        // 没有焦点就不转发相对鼠标——游戏里视角不转。
        if (!watch)
        {
            _desktopWindow?.RdpHost.EnsureInputFocus();
        }

        RefreshState(mode switch
        {
            ChildSessionInputMode.Watch => "已进入观看模式，本机鼠标和键盘不会进入桌面分身",
            ChildSessionInputMode.GameMouse => "已切换为游戏鼠标模式",
            _ => "已切换为普通鼠标模式"
        });
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

        // 关窗会触发全屏清理，把全屏期间自动切到游戏鼠标的输入模式还原回来。
        // 但 Close() 在下面、那时 _disposed 已经置位，SetInputMode 会被 ThrowIfDisposed
        // 拦住（还会顺带弹一个错误框）。所以先在这里、标记之前把这一步做掉。
        _desktopWindow?.RestoreInputModeBeforeShutdown();

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
        // 每次新连接都重置：上一次「等用户改密」的状态不能带过来，
        // 否则新一轮连接超时会被误判成「还在改密」而永远不报失败。
        _awaitingPasswordChange = false;

        var existingSessionId = ChildSessionNativeMethods.TryGetChildSessionId();
        var window = EnsureDesktopWindow();
        ShowDesktopWindow(window);

        if (window.RdpHost.ConnectedState == 0)
        {
            _reconnectingSessionId = existingSessionId;
            _autoLaunchBetterGiPending = existingSessionId is null;
            _initialConnectionRetriesRemaining = _autoLaunchBetterGiPending
                ? InitialConnectionRetryCount
                : 0;

            _connectStartedAt = DateTime.UtcNow;
            _logger.LogInformation(
                "开始连接桌面分身（会话 {SessionId}）。",
                existingSessionId?.ToString() ?? "(新建)");
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

    /// <summary>
    /// 是否正处在连接流程里（RDP 正在连接，或有一次连接尝试在途）。
    ///
    /// 不能只看 <see cref="ConnectedState"/>：Connect 是异步发起的，刚调用完的那一小段
    /// 时间里状态还没变成 2，只看它会误判成「不在连接中」而提前收掉连接中的状态。
    /// 设置变更后的自动重连也要算进来——那段时间状态同样是 0，但它确实还会再连一次。
    /// </summary>
    private void OnStatusTimerTick(object? sender, EventArgs e)
    {
        if (_statusTickInProgress || _disposed)
        {
            return;
        }

        _statusTickInProgress = true;
        try
        {
            // TermService 状态缓存按节流刷新（每 5 秒一次）：外部工具停掉/禁用服务后，
            // 状态行能在几秒内跟上，而不是永远停在旧快照。
            if (++_termServiceStatusTickCounter >= 10)
            {
                _termServiceStatusTickCounter = 0;
                RefreshTermServiceStatusCache();
            }

            RefreshState();

            // 改密等待期间连接断了（用户在改密界面点了取消）：这次登录尝试到此为止。
            // 只看 RDP 是否断开——用户取消后 WTS 会话可能还留着，按会话是否存在判断
            // 会让等待一直挂着，状态行也停在「等待修改密码」。
            if (_awaitingPasswordChange && ConnectedState == 0)
            {
                _awaitingPasswordChange = false;

                // 这两个是 ConnectCore 为「自动拉起 BetterGI」留的状态：本次尝试已经结束，
                // 不清的话下一轮连接会带着上次的自动启动意图和剩余重试次数。
                _autoLaunchBetterGiPending = false;
                _initialConnectionRetriesRemaining = 0;
                _connectionAttemptCompletionSource?.TrySetResult(false);
                RefreshState("已取消登录，未完成密码修改");
            }

            // 重新附着已有会话时，ActiveX 可能不再发 OnLoginComplete。只对同一个
            // 已有会话用系统状态兜底：RDP 已连接 + 会话号没变 + WTS 显示已登录，
            // 三者都满足就按登录完成收尾。新建会话仍只认登录事件。
            // 「等改密」时不走这条：那种情况会话也是 Active，但登录并没有完成。
            if (_connectionAttemptCompletionSource is { Task.IsCompleted: false }
                && !_awaitingPasswordChange
                && ConnectedState == 1
                && _reconnectingSessionId is { } reconnecting
                && ChildSessionId == reconnecting
                && ChildSessionNativeMethods.IsSessionLoggedOnAndActive(reconnecting))
            {
                _logger.LogInformation(
                    "桌面分身会话 {SessionId} 已恢复连接，按登录完成收尾。",
                    reconnecting);
                OnRdpLoginCompleted(this, EventArgs.Empty);
            }

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

        // 控件本身需要重建才能继续（典型是残留的旧密码清不掉）：换个新控件就好了，
        // 不必让用户重启程序。每轮启动只重建一次，避免反复重建。
        if (e.RecoverableByRecreatingHost)
        {
            if (!_controlRecreatedForAttempt)
            {
                _controlRecreatedForAttempt = true;
                _logger.LogInformation("RDP 控件需要重建才能继续（{Message}），重建后重连。", e.Message);

                RecreateDesktopWindow();
                _autoLaunchBetterGiPending = true;
                _initialConnectionRetriesRemaining = 1;
                RetryInitialConnectionAsync(e);
                return;
            }

            // 重建过一次还是同样问题：只能如实报告。
        }

        // 凭据不对时重试没有意义：每次重试都会用同一个密码再撞一次，
        // 只会把账户往锁定策略上推（锁定阈值常见的就 3～5 次）。直接判失败，交给用户改密码。
        if (e.IsLogonFailure)
        {
            // 密码「过期、必须修改」（LOGON_FAILED_UPDATE_PASSWORD = 1）时 Windows 会在
            // 会话里渲染改密界面——画面可见、可点击，说明连接通了，只是登录没完成。
            // 这种不能断开（界面会被关掉），也不该判失败、不该按连接超时处理：
            // 记下「等改密」，让启动等待跳过超时判定，用户走完流程会由登录完成事件收尾。
            // 1 = LOGON_FAILED_UPDATE_PASSWORD（会话内改密）；
            // 0xC0000224 = STATUS_PASSWORD_MUST_CHANGE（部分服务器直接给这个 NTSTATUS）。
            //
            // 还必须排除断开事件：那条路径的 ErrorCode 是 disconnectReason（1 表示「本地无错」），
            // 配合扩展原因 10/768 也会让 IsLogonFailure 为 true，但那不是会话内改密界面。
            // 登录事件（OnLogonError）不带扩展码，断开事件必带，用这个区分。
            if (e.ExtendedErrorCode is null
                && (e.ErrorCode == 1 || e.ErrorCode == unchecked((int)0xC0000224)))
            {
                _awaitingPasswordChange = true;
                _logger.LogInformation("客户端要求修改 Windows 密码，保持连接等待用户在改密界面操作。");
                RefreshState("等待在会话中修改 Windows 密码");
                return;
            }

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

        // 重试已经耗尽。连接阶段的失败（非凭据类）里，有一类来自 RDP 控件内部状态机
        // 卡死：表现是稳定复现的「协议错误」，重试多少次都一样，只能重建控件。
        // 每轮启动只做一次，避免把真正的环境问题（服务未启用等）也拖进循环。
        if (!_controlRecreatedForAttempt && !e.IsLogonFailure)
        {
            _controlRecreatedForAttempt = true;
            _logger.LogWarning(
                "连接失败且重试已耗尽，重建 RDP 控件后再试一次（错误：{Message}）", e.Message);

            RecreateDesktopWindow();
            _autoLaunchBetterGiPending = true;
            _initialConnectionRetriesRemaining = 1;
            RetryInitialConnectionAsync(e);
            return;
        }

        CompleteConnectionFailure(e);
    }

    /// <summary>
    /// 丢弃当前的桌面分身窗口并重建。
    ///
    /// ChildSessionWindow 注册为 Transient，重新取一次就是全新的窗口和 ActiveX 控件，
    /// 能把卡住的状态机一起丢掉。只解绑事件、调用 Close()，不手动 Dispose RdpHost——
    /// 窗口的 OnClosed 里已经处理了释放，重复释放会抛异常。
    /// </summary>
    private void RecreateDesktopWindow()
    {
        var old = _desktopWindow;
        if (old is null)
        {
            return;
        }

        old.RdpHost.ConnectionFailed -= OnRdpConnectionFailed;
        old.RdpHost.LoginCompleted -= OnRdpLoginCompleted;
        old.IsVisibleChanged -= OnDesktopWindowVisibilityChanged;

        _reconnectingSessionIdAfterRecreate = _reconnectingSessionId;
        _desktopWindow = null;
        _reconnectingSessionId = null;
        _awaitingPasswordChange = false;

        try
        {
            // 窗口自己会把 Close() 拦下来（OnClosing 默认 e.Cancel = true），
            // 这是「用户点关闭转为隐藏」的设计，重建时必须显式放行。
            old.AllowClose = true;
            old.Close();
        }
        catch (Exception exception) when (IsExpectedChildSessionException(exception))
        {
            _logger.LogWarning(exception, "关闭旧的桌面分身窗口失败，继续重建。");
        }
    }

    // RDP 报告连接成功后稍作等待，让新 Child Session 的桌面初始化完成。
    // 使用 OnLoginComplete 等待实际登录完成，避免依赖固定时长的延时。
    private async void OnRdpLoginCompleted(object? sender, EventArgs e)
    {
        _awaitingPasswordChange = false;
        _reconnectingSessionId = null;

        // 从「开始连接」到登录完成的总耗时：用户感知的黑屏时间基本就是这一段。
        if (_connectStartedAt is { } startedAt)
        {
            _logger.LogInformation(
                "桌面分身登录完成，耗时 {Seconds:0.0} 秒。",
                (DateTime.UtcNow - startedAt).TotalSeconds);
            _connectStartedAt = null;
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
            // 只有「仍属于自己这一代」的重试才有资格清标志：新一代已经被
            // ReconnectForRdpSettingChange 起头并可能正在等第二轮，这里无脑清掉会让
            // 后续的连接失败绕过「重试进行中」的判断，多排一轮重试。
            if (generation == _settingsReconnectGeneration)
            {
                _rdpSettingsReconnectRetryInProgress = false;
            }
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

            // 控件重建分支会先丢弃旧窗口再进到这里，此时 _desktopWindow 必然为空。
            // 不能直接返回——那样重建完什么都不做，启动只能靠外层等待超时收场。
            // 为空就新建一个（ChildSessionWindow 是 Transient，拿到的就是全新控件）。
            var window = EnsureDesktopWindow();
            ShowDesktopWindow(window);

            // 重建时被清掉的「正在重新附着的会话号」要接回来：新控件连的还是同一个
            // 已有会话，登录完成的系统状态兜底靠它才生效。
            if (_reconnectingSessionIdAfterRecreate is { } reattaching)
            {
                _reconnectingSessionId = reattaching;
                _reconnectingSessionIdAfterRecreate = null;
            }

            RefreshState(
                $"正在重试桌面分身连接（{retryNumber}/{InitialConnectionRetryCount}）");
            _connectionRetryInProgress = false;
            // OnLogonError 触发后 ActiveX 可能仍处于连接状态，必须先断开再重连。
            // 否则这里直接退出会丢失已捕获的真实错误，最终只剩外层连接超时。
            window.RdpHost.ReconnectToChildSession(DefaultDesktopSize);
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
            e.ExtendedErrorCode)
        {
            // 凭据失败标记要原样带过来：这里重建的是对外事件，漏掉它会把
            // 「凭据被拒」降级成普通失败，订阅方（状态行/诊断）就不知道该提示改密码了。
            IsLogonFailure = e.IsLogonFailure
        };
        _lastConnectionFailure = diagnosed;
        _autoLaunchBetterGiPending = false;
        _initialConnectionRetriesRemaining = 0;
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
    /// 连接失败后的补充诊断：报告当前是否存在可用的桌面分身会话。
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
        var previousRetriesRemaining = _rdpSettingsReconnectRetriesRemaining;
        _pendingRdpSettingChanges |= settingChange;
        _rdpSettingsReconnectRetriesRemaining = RdpSettingsReconnectRetryCount;

        // 新的设置变更会让在途的那次重试作废：否则它醒来后看到 RdpSettingChange 非空，
        // 会把这次刚发起的连接断开重来（ReconnectToChildSession 在已连接状态下会
        // DisconnectCore），表现成「改两次设置后连接来回抽搐」。
        var generationBeforeInvalidation = _settingsReconnectGeneration;
        var retryInProgressBeforeInvalidation = _rdpSettingsReconnectRetryInProgress;
        _settingsReconnectGeneration++;
        _rdpSettingsReconnectRetryInProgress = false;

        try
        {
            window.RdpHost.ReconnectToChildSession(DefaultDesktopSize);
        }
        catch
        {
            // 这次发起本身失败了：把「作废在途重试」也回滚掉，否则那个重试已经
            // 被我们弄死，而这次又没连起来，待应用的设置就再也没人负责重连了。
            _settingsReconnectGeneration = generationBeforeInvalidation;
            _rdpSettingsReconnectRetryInProgress = retryInProgressBeforeInvalidation;
            _pendingRdpSettingChanges = previousSettingChanges;
            // 预算要跟着一起回滚：在途的另一次重连可能正处在最后一次重试，
            // 不恢复的话它下一次失败会凭空多出 RdpSettingsReconnectRetryCount 次机会。
            _rdpSettingsReconnectRetriesRemaining = previousRetriesRemaining;

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

