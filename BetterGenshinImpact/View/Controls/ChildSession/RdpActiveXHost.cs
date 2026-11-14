using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using BetterGenshinImpact.Service.ChildSession;
using Microsoft.Extensions.Logging;
using DrawingSize = System.Drawing.Size;

namespace BetterGenshinImpact.View.Controls.ChildSession;

internal sealed class RdpActiveXHost : AxHost
{
    // Windows 10+ 自带的非脚本化 RDP ActiveX 控件（MsRdpClient10）。
    private const string RdpClientClsid = "A0C63C30-F08D-4AB4-907C-34905D770C7D";
    private const short VariantFalse = 0;
    private const short VariantTrue = -1;
    private const int RedirectAudioToClient = 0;
    private const int DisableRemoteAudio = 2;
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(1);

    private static readonly RemoteKey LeftWindowsKey = new(0x5B, IsExtended: true);
    private static readonly RemoteKey DKey = new(0x20, IsExtended: false);
    private static readonly RemoteKey TabKey = new(0x0F, IsExtended: false);
    private ConnectionPointCookie? _eventCookie;
    private RdpEventSink? _eventSink;
    private bool _connectionAttemptInProgress;
    private bool _credentialApplied;
    private bool _connectionFailureReported;
    private ChildSessionConnectionFailedEventArgs? _lastConnectionDiagnostic;
    private bool _disconnectRequested;
    private bool _sendSystemShortcutsToRemote = true;
    private bool _audioMuted;
    private DrawingSize? _pendingReconnectDesktopSize;

    /// <summary>
    /// 待重连的代际号。断开或开始新一次连接时自增，让已经排上、
    /// 还在等「会话释放」的那次重连作废——否则它醒来后照样去连，
    /// 把刚刚发起的断开又接回来。
    /// </summary>
    private int _reconnectGeneration;
    private bool _smartSizingEnabled = true;
    private bool _inputBlocked;

    /// <summary>
    /// 自动登录凭据的来源。为 null 表示不做自动登录，走原来的交互式输入。
    /// </summary>
    private Func<RdpCredential?>? _credentialProvider;

    internal event EventHandler<ChildSessionConnectionFailedEventArgs>? ConnectionFailed;

    internal event EventHandler? LoginCompleted;

    internal ChildSessionConnectionFailedEventArgs? LastConnectionDiagnostic =>
        _lastConnectionDiagnostic;

    internal RdpActiveXHost()
        : base(RdpClientClsid)
    {
        Dock = DockStyle.Fill;
    }

    internal int ConnectedState
    {
        get
        {
            if (!IsHandleCreated)
            {
                return 0;
            }

            return Convert.ToInt32(
                GetComProperty(GetRequiredOcx(), "Connected"),
                CultureInfo.InvariantCulture);
        }
    }

    internal void ConnectToChildSession(DrawingSize desktopSize)
    {
        if (ConnectedState != 0)
        {
            return;
        }

        // 新的一次连接开始：把还没执行的那次重连作废，免得它醒来后再连一遍。
        _reconnectGeneration++;
        _pendingReconnectDesktopSize = null;

        ChildSessionNativeMethods.ClearRdpInputWindowCache(Handle);
        var client = GetRequiredOcx();
        var width = Math.Clamp(desktopSize.Width, 200, 8192);
        var height = Math.Clamp(desktopSize.Height, 200, 8192);

        SetComProperty(client, "Server", "localhost");
        SetComProperty(client, "DesktopWidth", width);
        SetComProperty(client, "DesktopHeight", height);
        SetComProperty(client, "ColorDepth", 32);
        SetComProperty(client, "ConnectingText", "正在创建 BetterGI 桌面分身...");
        SetComProperty(client, "DisconnectedText", "BetterGI 桌面分身已断开");

        var securedSettings = GetComProperty(client, "SecuredSettings2")
            ?? throw new COMException("RDP ActiveX 未返回 SecuredSettings2。");
        RunComStep("设置系统组合键发送位置", () =>
            SetComProperty(
                securedSettings,
                "KeyboardHookMode",
                _sendSystemShortcutsToRemote ? 1 : 0));
        RunComStep("设置远程音频重定向", () =>
            SetComProperty(
                securedSettings,
                "AudioRedirectionMode",
                _audioMuted ? DisableRemoteAudio : RedirectAudioToClient));

        var advancedSettings = GetComProperty(client, "AdvancedSettings7")
            ?? throw new COMException("RDP ActiveX 未返回 AdvancedSettings7。");
        RunComStep("设置 RDP 连接端口", () =>
            SetComProperty(
                advancedSettings,
                "RDPPort",
                ChildSessionNativeMethods.GetConfiguredRdpPort()));
        RunComStep("启用 CredSSP", () =>
            SetComProperty(advancedSettings, "EnableCredSspSupport", true));
        RunComStep("启用远程 Windows 键", () =>
            SetComProperty(advancedSettings, "EnableWindowsKey", 1));
        RunComStep("设置显示缩放", () =>
            SetComProperty(advancedSettings, "SmartSizing", _smartSizingEnabled));

        object connectToChildSession = true;
        RunComStep("设置 ConnectToChildSession", () =>
        {
            var extendedSettings = (IMsRdpExtendedSettings)client;
            TrySetExtendedProperty(extendedSettings, "EnableZoom", true);
            extendedSettings.set_Property("ConnectToChildSession", ref connectToChildSession);
        });

        // 先清掉上一次的失败标记再交凭据：交凭据本身可能失败（旧密码清不掉），
        // 那时要靠 ReportConnectionFailure 把原因报出去，标记还留着就会被吞掉。
        _connectionAttemptInProgress = true;
        _connectionFailureReported = false;
        _lastConnectionDiagnostic = null;
        _disconnectRequested = false;

        // 凭据要在 Connect() 之前交给控件：登录阶段才会用到它们。
        if (!ApplyCredentials(client))
        {
            _connectionAttemptInProgress = false;
            return;
        }

        try
        {
            RunComStep("调用 RDP Connect()", () => InvokeComMethod(client, "Connect"));
        }
        catch
        {
            _connectionAttemptInProgress = false;
            throw;
        }
    }

    internal void SendShowDesktopShortcut()
    {
        KeyStroke[] strokes =
        [
            new KeyStroke(LeftWindowsKey, IsKeyUp: false),
            new KeyStroke(DKey, IsKeyUp: false),
            new KeyStroke(DKey, IsKeyUp: true),
            new KeyStroke(LeftWindowsKey, IsKeyUp: true)
        ];

        SendShortcut(strokes, "Win+D");
    }

    internal void SendTaskViewShortcut()
    {
        KeyStroke[] strokes =
        [
            new KeyStroke(LeftWindowsKey, IsKeyUp: false),
            new KeyStroke(TabKey, IsKeyUp: false),
            new KeyStroke(TabKey, IsKeyUp: true),
            new KeyStroke(LeftWindowsKey, IsKeyUp: true)
        ];

        SendShortcut(strokes, "Win+Tab");
    }

    internal void SetSmartSizing(bool enabled)
    {
        _smartSizingEnabled = enabled;
        if (!IsHandleCreated)
        {
            return;
        }

        var advancedSettings = GetComProperty(GetRequiredOcx(), "AdvancedSettings7")
            ?? throw new COMException("RDP ActiveX 未返回 AdvancedSettings7。");
        RunComStep("设置显示缩放", () =>
            SetComProperty(advancedSettings, "SmartSizing", enabled));
    }

    internal void SetSendSystemShortcutsToRemote(bool enabled)
    {
        _sendSystemShortcutsToRemote = enabled;
    }

    internal void SetAudioMuted(bool muted)
    {
        _audioMuted = muted;
    }

    /// <summary>
    /// 设置自动登录凭据的来源。每次连接都会重新取一次：
    /// 用户可能在两次连接之间改了密码。
    /// </summary>
    internal void SetCredentialProvider(Func<RdpCredential?> provider)
    {
        _credentialProvider = provider;
    }

    /// <summary>
    /// 屏蔽或恢复本机进入桌面分身的鼠标键盘输入。期望状态会被记住，
    /// 每次连接登录后由 ChildSessionService 重新施加。
    /// </summary>
    internal void SetInputBlocked(bool blocked)
    {
        _inputBlocked = blocked;
        if (!IsHandleCreated)
        {
            return;
        }

        ChildSessionNativeMethods.SetRdpInputWindowEnabled(Handle, !blocked);
    }

    internal void ReconnectToChildSession(DrawingSize desktopSize)
    {
        if (_disconnectRequested || _pendingReconnectDesktopSize.HasValue)
        {
            _pendingReconnectDesktopSize = desktopSize;
            return;
        }

        if (ConnectedState == 0)
        {
            // 直接连也作废在途的那次「等会话释放」重连：
            // 等这边失败（控件回 0）时它会醒过来，把新发起的一次顶掉。
            _reconnectGeneration++;
            ConnectToChildSession(desktopSize);
            return;
        }

        _pendingReconnectDesktopSize = desktopSize;
        try
        {
            if (!DisconnectCore())
            {
                _pendingReconnectDesktopSize = null;
                ConnectToChildSession(desktopSize);
            }
        }
        catch
        {
            _pendingReconnectDesktopSize = null;
            throw;
        }
    }

    internal void DisconnectSession()
    {
        // 作废在途的重连：它可能正停在「等会话释放」上，等完就会把分身又连回来。
        _reconnectGeneration++;
        _pendingReconnectDesktopSize = null;
        _ = DisconnectCore();
    }

    /// <summary>
    /// 断开连接，但推迟到当前 ActiveX 事件回调结束之后执行。
    ///
    /// 登录失败这类回调是控件同步发出来的，在那里面直接调 <c>Disconnect</c> 属于
    /// 在事件处理里重入控件，容易拿到 COM 错误；排到消息队列末尾更稳。
    /// </summary>
    internal void DisconnectSessionDeferred()
    {
        // 与 DisconnectSession 一样先作废在途的重连。
        _reconnectGeneration++;
        _pendingReconnectDesktopSize = null;

        if (!IsHandleCreated || IsDisposed || Disposing)
        {
            _ = DisconnectCore();
            return;
        }

        try
        {
            BeginInvoke(new Action(DisconnectSafely));
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void DisconnectSafely()
    {
        try
        {
            _ = DisconnectCore();
        }
        catch (Exception exception) when (exception is COMException
                                              or TargetInvocationException
                                              or InvalidOperationException)
        {
            ChildSessionDiagnosticsLog.Instance.LogDebug(exception, "断开 RDP 连接失败。");
        }
    }

    private bool DisconnectCore()
    {
        if (IsHandleCreated)
        {
            ChildSessionNativeMethods.ClearRdpInputWindowCache(Handle);
        }

        if (ConnectedState == 0)
        {
            return false;
        }

        _disconnectRequested = true;
        try
        {
            InvokeComMethod(GetRequiredOcx(), "Disconnect");
            return true;
        }
        catch
        {
            _disconnectRequested = false;
            throw;
        }
    }

    internal bool IsInputWindowFocused()
    {
        return ConnectedState == 1
               && ChildSessionNativeMethods.IsRdpInputWindowFocused(Handle);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (IsHandleCreated)
        {
            ChildSessionNativeMethods.ClearRdpInputWindowCache(Handle);
        }

        base.OnHandleDestroyed(e);
    }

    protected override void CreateSink()
    {
        base.CreateSink();

        _eventSink = new RdpEventSink(this);
        _eventCookie = new ConnectionPointCookie(
            GetOcx(),
            _eventSink,
            typeof(IMsTscAxEvents));
    }

    protected override void DetachSink()
    {
        try
        {
            _eventCookie?.Disconnect();
            _eventCookie = null;
            _eventSink = null;
        }
        finally
        {
            base.DetachSink();
        }
    }

    private void SendShortcut(KeyStroke[] strokes, string displayName)
    {
        if (ConnectedState != 1)
        {
            throw new InvalidOperationException(
                $"桌面分身尚未完全连接，无法发送 {displayName}。");
        }

        // 组合键由调用方显式发起，发送期间短暂恢复输入窗口，发完立刻回到屏蔽状态。
        if (_inputBlocked)
        {
            ChildSessionNativeMethods.SetRdpInputWindowEnabled(Handle, true);
        }

        try
        {
            if (!ChildSessionNativeMethods.TryFocusRdpInputWindow(Handle))
            {
                throw new InvalidOperationException("无法将键盘焦点切换到桌面分身。");
            }

            RunComStep($"向 Child Session 发送 {displayName}", () => SendKeyStrokes(strokes));
        }
        finally
        {
            if (_inputBlocked)
            {
                ChildSessionNativeMethods.SetRdpInputWindowEnabled(Handle, false);
            }
        }
    }

    private void SendKeyStrokes(KeyStroke[] strokes)
    {
        var keyUpStates = new short[strokes.Length];
        var keyData = new int[strokes.Length];

        for (var index = 0; index < strokes.Length; index++)
        {
            var stroke = strokes[index];
            keyUpStates[index] = stroke.IsKeyUp ? VariantTrue : VariantFalse;
            keyData[index] = CreateRdpScanCode(stroke.Key);
        }

        var nonScriptableClient = (IMsRdpClientNonScriptable)GetRequiredOcx();
        nonScriptableClient.SendKeys(
            strokes.Length,
            ref keyUpStates[0],
            ref keyData[0]);
    }

    private static int CreateRdpScanCode(RemoteKey key)
    {
        const int extendedScanCodeFlag = 0x0100;
        return key.ScanCode | (key.IsExtended ? extendedScanCodeFlag : 0);
    }

    private void OnLoginComplete()
    {
        _connectionAttemptInProgress = false;
        _connectionFailureReported = false;
        _lastConnectionDiagnostic = null;
        LoginCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void OnDisconnected(int disconnectReason)
    {
        var failedWhileConnecting = _connectionAttemptInProgress;
        _connectionAttemptInProgress = false;

        if (_disconnectRequested)
        {
            _disconnectRequested = false;
            if (_pendingReconnectDesktopSize.HasValue && !IsDisposed && !Disposing)
            {
                try
                {
                    BeginInvoke(new Action(ConnectPendingReconnect));
                }
                catch (InvalidOperationException)
                {
                    _pendingReconnectDesktopSize = null;
                }
            }
            return;
        }

        var extendedDisconnectReason = TryGetExtendedDisconnectReason();
        if (_connectionFailureReported
            || (!failedWhileConnecting
                && IsNormalDisconnect(disconnectReason, extendedDisconnectReason)))
        {
            return;
        }

        var errorDescription = TryGetErrorDescription(
            disconnectReason,
            extendedDisconnectReason);
        var failureTitle = failedWhileConnecting
            ? "RDP 连接失败"
            : "RDP 连接意外断开";
        var message = string.IsNullOrWhiteSpace(errorDescription)
            ? $"{failureTitle}。\n\n断开原因：{FormatErrorCode(disconnectReason)}\n扩展原因：{FormatErrorCode(extendedDisconnectReason)}"
            : $"{failureTitle}：{errorDescription}\n\n断开原因：{FormatErrorCode(disconnectReason)}\n扩展原因：{FormatErrorCode(extendedDisconnectReason)}";

        ReportConnectionFailure(
            message,
            disconnectReason,
            extendedDisconnectReason,
            isLogonFailure: failedWhileConnecting
                            && (ChildSessionLogonFailure.IsRejectedDisconnectCode(disconnectReason)
                                || ChildSessionLogonFailure.IsRejectedDisconnectCode(extendedDisconnectReason)));
    }

    private async void ConnectPendingReconnect()
    {
        var generation = _reconnectGeneration;

        await Task.Delay(ReconnectDelay);

        // 等待期间可能被断开或被新的一次连接取代，这时不能再连。
        if (generation != _reconnectGeneration || IsDisposed || Disposing)
        {
            return;
        }

        // 控件回到「已断开」不代表上一轮连接在系统里已经收尾。这时立刻重连，
        // 服务端会用「访问被拒绝」把新连接顶回来——改设置后「第一次必定失败、再点一次才行」
        // 就是这么来的。等会话真的不再是 Active 再连。
        await WaitForChildSessionReleasedAsync(TimeSpan.FromSeconds(10));
        if (generation != _reconnectGeneration || IsDisposed || Disposing)
        {
            return;
        }

        // 尺寸要等到这时才取：等待期间用户可能又改了一次设置，那次的尺寸会写进同一个字段。
        var desktopSize = _pendingReconnectDesktopSize;
        _pendingReconnectDesktopSize = null;
        if (!desktopSize.HasValue)
        {
            return;
        }

        try
        {
            ConnectToChildSession(desktopSize.Value);
        }
        catch (Exception exception) when (exception is COMException
                                              or TargetInvocationException
                                              or InvalidOperationException)
        {
            var actualException = exception.GetBaseException();
            var errorCode = actualException is COMException comException
                ? comException.ErrorCode
                : 0;
            ReportConnectionFailure(
                $"RDP 自动重新连接失败：{actualException.Message}",
                errorCode);
        }
    }

    /// <summary>
    /// 等分身会话离开 Active 状态。没有会话（已经注销）也算可以连了。
    /// </summary>
    private static async Task WaitForChildSessionReleasedAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var sessionId = ChildSessionNativeMethods.TryGetChildSessionId();
            if (sessionId is null || !ChildSessionNativeMethods.IsSessionActive(sessionId.Value))
            {
                return;
            }

            await Task.Delay(200);
        }
    }

    private void OnFatalError(int errorCode)
    {
        _connectionAttemptInProgress = false;
        if (_disconnectRequested)
        {
            return;
        }

        ReportConnectionFailure(
            $"RDP 客户端发生致命错误：{GetFatalErrorDescription(errorCode)}\n\n错误代码：{FormatErrorCode(errorCode)}",
            errorCode);
    }

    private void OnLogonError(int errorCode)
    {
        if (_disconnectRequested)
        {
            return;
        }

        var message =
            $"RDP 登录阶段：{GetLogonErrorDescription(errorCode)}\n\n错误代码：{FormatErrorCode(errorCode)}";
        if (IsNonTerminalLogonEvent(errorCode))
        {
            _lastConnectionDiagnostic =
                new ChildSessionConnectionFailedEventArgs(message, errorCode);
            return;
        }

        _connectionAttemptInProgress = false;
        ReportConnectionFailure(
            message.Replace("RDP 登录阶段：", "RDP 登录失败：", StringComparison.Ordinal),
            errorCode,
            isLogonFailure: ChildSessionLogonFailure.IsRejectedLogonCode(errorCode));
    }

    private int TryGetExtendedDisconnectReason()
    {
        try
        {
            return Convert.ToInt32(
                GetComProperty(GetRequiredOcx(), "ExtendedDisconnectReason"),
                CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is COMException
                                              or TargetInvocationException
                                              or InvalidOperationException)
        {
            return 0;
        }
    }

    private string? TryGetErrorDescription(
        int disconnectReason,
        int extendedDisconnectReason)
    {
        try
        {
            return Convert.ToString(
                InvokeComMethod(
                    GetRequiredOcx(),
                    "GetErrorDescription",
                    disconnectReason,
                    extendedDisconnectReason),
                CultureInfo.CurrentCulture);
        }
        catch (Exception exception) when (exception is COMException
                                              or TargetInvocationException
                                              or InvalidOperationException)
        {
            return null;
        }
    }

    private void ReportConnectionFailure(
        string message,
        int errorCode,
        int? extendedErrorCode = null,
        bool isLogonFailure = false)
    {
        if (_connectionFailureReported)
        {
            return;
        }

        _connectionFailureReported = true;
        ConnectionFailed?.Invoke(
            this,
            new ChildSessionConnectionFailedEventArgs(
                message,
                errorCode,
                extendedErrorCode)
            {
                IsLogonFailure = isLogonFailure
            });
    }

    private static bool IsNormalDisconnect(
        int disconnectReason,
        int extendedDisconnectReason)
    {
        // 1～3 分别表示本地主动断开、远端用户断开和服务器主动断开，并非连接错误。
        // 扩展原因 1、2 分别表示 API 主动断开和 API 主动注销。
        return disconnectReason is 1 or 2 or 3
               && extendedDisconnectReason is 0 or 1 or 2;
    }

    private static string FormatErrorCode(int errorCode)
    {
        return $"{errorCode} (0x{unchecked((uint)errorCode):X8})";
    }

    private static string GetFatalErrorDescription(int errorCode)
    {
        return errorCode switch
        {
            0 => "发生未知错误。",
            1 => "发生内部错误（1）。",
            2 => "内存不足。",
            3 => "无法创建 RDP 窗口。",
            4 => "发生内部错误（2）。",
            5 => "RDP 客户端进入了无效状态。",
            6 => "发生内部错误（4）。",
            7 => "建立客户端连接时发生不可恢复的错误。",
            100 => "Windows 套接字初始化失败。",
            _ => "发生未识别的致命错误。"
        };
    }

    private static string GetLogonErrorDescription(int errorCode)
    {
        return errorCode switch
        {
            -7 => "Winlogon 正在显示“拒绝断开现有会话”对话框。",
            -6 => "Winlogon 正在显示“无权限”对话框。",
            -5 => "Winlogon 正在显示会话争用选项。",
            -4 => "Winlogon 正在显示重新连接选项。",
            -3 => "Winlogon 已静默终止登录。",
            -1 => "访问被拒绝。",
            0 => "登录凭据无效。",
            1 => "密码已过期，必须先修改密码。",
            2 => "登录或登录后的处理发生错误。",
            3 => "RDP 客户端正在显示登录警告。",
            unchecked((int)0xC000006D) => "用户名或身份验证信息无效。",
            unchecked((int)0xC000006E) => "身份验证受到用户账户限制。",
            unchecked((int)0xC0000224) => "密码已过期，必须先修改密码。",
            _ => "登录阶段发生未识别的错误或事件。"
        };
    }

    private static bool IsNonTerminalLogonEvent(int errorCode)
    {
        // 这些代码表示登录仍在继续或 ActiveX 正在显示可交互选项，不应提前判定连接失败。
        return errorCode is -5 or -4 or -2 or 3;
    }

    private object GetRequiredOcx()
    {
        if (!IsHandleCreated)
        {
            // WindowsFormsHost 在未连接空白态会被折叠，普通 CreateControl() 会因不可见而跳过创建。
            // 读取 Handle 会忽略可见性并强制 AxHost 创建底层窗口及 ActiveX 实例。
            _ = Handle;
        }

        return GetOcx()
               ?? throw new InvalidOperationException(
                   "RDP ActiveX 控件尚未完成初始化，无法访问 COM 实例。");
    }

    private static object? GetComProperty(object target, string propertyName)
    {
        return target.GetType().InvokeMember(
            propertyName,
            BindingFlags.GetProperty,
            binder: null,
            target,
            args: null,
            CultureInfo.InvariantCulture);
    }

    private static void SetComProperty(object target, string propertyName, object value)
    {
        target.GetType().InvokeMember(
            propertyName,
            BindingFlags.SetProperty,
            binder: null,
            target,
            [value],
            CultureInfo.InvariantCulture);
    }

    private static void TrySetExtendedProperty(
        IMsRdpExtendedSettings extendedSettings,
        string propertyName,
        object value)
    {
        try
        {
            extendedSettings.set_Property(propertyName, ref value);
        }
        catch (COMException)
        {
            // 旧版 MsTscAx 可能不支持该扩展属性，继续使用原有显示行为。
        }
    }

    private static object? InvokeComMethod(object target, string methodName, params object[]? args)
    {
        return target.GetType().InvokeMember(
            methodName,
            BindingFlags.InvokeMethod,
            binder: null,
            target,
            args,
            CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 把自动登录凭据交给 RDP 控件。
    ///
    /// 每一步都单独兜住异常：凭据设不上只是退回「让用户自己输」，
    /// 不该因为一个可选属性不支持就让整次连接失败。
    /// </summary>
    /// <returns>false 表示这次连接不能再继续（控件里还留着上一次的密码却清不掉）。</returns>
    private bool ApplyCredentials(object client)
    {
        var credential = _credentialProvider?.Invoke();
        if (credential is not { } value
            || string.IsNullOrWhiteSpace(value.UserName)
            || string.IsNullOrEmpty(value.Password))
        {
            // 本次不自动登录（开关关着、没存密码，或用户刚清掉密码）。
            //
            // 控件是跨连接复用的：上一次设进去的密码不会自己消失。这里必须主动清掉，
            // 否则「关掉自动登录再重连」仍会被旧密码静默登进去——用户会以为开关没生效。
            // 清的是控件内的凭据状态，不是用户保存的密码；保存的密码在设置里原样保留，
            // 下次打开自动登录还会用它。
            // ResetPassword 在「已连接」时会失败（E_FAIL），这里是 Connect() 之前调用的。
            if (!TryClearStoredPassword(client))
            {
                // 清不掉就不能连：连上去会被上次的密码直接登进去，
                // 「关掉自动登录」就成了摆设。让用户重启 BetterGI（控件才会重建）比静默登录好。
                // 标成凭据失败：服务侧据此跳过重试——同一个控件清不掉，再连几次也一样。
                ReportConnectionFailure(
                    "自动登录已关闭，但无法清除上一次连接留在 RDP 控件里的密码。\n\n" +
                    "请重启 BetterGI 后再连接。",
                    0,
                    isLogonFailure: true);
                return false;
            }

            return true;
        }

        ChildSessionDiagnosticsLog.Instance.LogInformation(
            "自动登录：本次连接使用已保存的凭据（账户 {UserName}，域 {Domain}）。",
            value.UserName,
            string.IsNullOrEmpty(value.Domain) ? "(空)" : value.Domain);

        try
        {
            SetComProperty(client, "UserName", value.UserName);
        }
        catch (Exception exception) when (IsCredentialStepException(exception))
        {
            ChildSessionDiagnosticsLog.Instance.LogWarning(
                exception,
                "设置 RDP 登录用户名失败，将退回手动输入。");
        }

        try
        {
            // 域为空也要显式写一次：控件复用，上一次可能是 MicrosoftAccount，
            // 不清掉会把本地账户套上微软账户的域，登录必然失败。
            SetComProperty(client, "Domain", value.Domain ?? string.Empty);
        }
        catch (Exception exception) when (IsCredentialStepException(exception))
        {
            ChildSessionDiagnosticsLog.Instance.LogWarning(
                exception,
                "设置 RDP 登录域失败，将退回手动输入。");
        }

        try
        {
            // 密码只经过 RDP 的凭据通道，不写入任何持久化存储。
            ((IMsRdpClientNonScriptable)client).put_ClearTextPassword(value.Password);
            _credentialApplied = true;
        }
        catch (Exception exception) when (IsCredentialStepException(exception))
        {
            // 密码本身已验证正确，这里只是写不进控件、原因未知；不主动清旧密码，
            // 直接退回手动输入（凭据框由自动登录兜底再填一次）。
            ChildSessionDiagnosticsLog.Instance.LogWarning(
                exception,
                "密码正确，但因未知原因保存失败，将退回手动输入。");
        }

        // 其余各步失败都只是「退回手动输入」，连接本身还可以继续。
        return true;
    }

    /// <summary>
    /// 清掉控件里上一次设进去的密码。从未设过密码时视为成功。
    /// </summary>
    /// <returns>true 表示控件里不再有任何残留密码。</returns>
    private bool TryClearStoredPassword(object client)
    {
        if (!_credentialApplied)
        {
            return true;
        }

        try
        {
            ((IMsRdpClientNonScriptable)client).ResetPassword();
            _credentialApplied = false;
            return true;
        }
        catch (Exception exception) when (IsCredentialStepException(exception))
        {
            ChildSessionDiagnosticsLog.Instance.LogDebug(exception, "清除上一次的 RDP 密码失败。");
            return false;
        }
    }

    private static bool IsCredentialStepException(Exception exception) =>
        exception.GetBaseException() is COMException
            or InvalidCastException
            or TargetInvocationException
            or MissingMemberException
            or InvalidOperationException;

    private static void RunComStep(string stepName, Action action)
    {        try
        {
            action();
        }
        catch (Exception exception)
        {
            var actualException = exception.GetBaseException();
            if (actualException is COMException comException)
            {
                throw new COMException(
                    $"{stepName}失败：{comException.Message}",
                    comException.ErrorCode);
            }

            throw;
        }
    }

    [ComImport]
    [Guid("336D5562-EFA8-482E-8CB3-C5C0FC7A7DB6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    [TypeLibType(TypeLibTypeFlags.FDispatchable)]
    private interface IMsTscAxEvents
    {
        [DispId(1)]
        void OnConnecting();

        [DispId(2)]
        void OnConnected();

        [DispId(3)]
        void OnLoginComplete();

        [DispId(4)]
        void OnDisconnected([In] int disconnectReason);

        [DispId(5)]
        void OnEnterFullScreenMode();

        [DispId(6)]
        void OnLeaveFullScreenMode();

        [DispId(7)]
        void OnChannelReceivedData(
            [In, MarshalAs(UnmanagedType.BStr)] string channelName,
            [In, MarshalAs(UnmanagedType.BStr)] string data);

        [DispId(8)]
        void OnRequestGoFullScreen();

        [DispId(9)]
        void OnRequestLeaveFullScreen();

        [DispId(10)]
        void OnFatalError([In] int errorCode);

        [DispId(11)]
        void OnWarning([In] int warningCode);

        [DispId(12)]
        void OnRemoteDesktopSizeChange([In] int width, [In] int height);

        [DispId(13)]
        void OnIdleTimeoutNotification();

        [DispId(14)]
        void OnRequestContainerMinimize();

        [DispId(15)]
        void OnConfirmClose([Out] out bool allowClose);

        [DispId(16)]
        void OnReceivedTSPublicKey(
            [In, MarshalAs(UnmanagedType.BStr)] string publicKey,
            [Out] out bool continueLogon);

        [DispId(17)]
        void OnAutoReconnecting(
            [In] int disconnectReason,
            [In] int attemptCount,
            [Out] out AutoReconnectContinueState continueStatus);

        [DispId(18)]
        void OnAuthenticationWarningDisplayed();

        [DispId(19)]
        void OnAuthenticationWarningDismissed();

        [DispId(20)]
        void OnRemoteProgramResult(
            [In, MarshalAs(UnmanagedType.BStr)] string remoteProgram,
            [In] RemoteProgramResult error,
            [In] bool isExecutable);

        [DispId(21)]
        void OnRemoteProgramDisplayed(
            [In] bool displayed,
            [In] uint displayInformation);

        [DispId(29)]
        void OnRemoteWindowDisplayed(
            [In] bool displayed,
            [In] ref RemotableHandle windowHandle,
            [In] RemoteWindowDisplayedAttribute windowAttribute);

        [DispId(22)]
        void OnLogonError([In] int errorCode);

        [DispId(23)]
        void OnFocusReleased([In] int direction);

        [DispId(24)]
        void OnUserNameAcquired(
            [In, MarshalAs(UnmanagedType.BStr)] string userName);

        [DispId(26)]
        void OnMouseInputModeChanged([In] bool isRelativeMouseMode);

        [DispId(28)]
        void OnServiceMessageReceived(
            [In, MarshalAs(UnmanagedType.BStr)] string serviceMessage);

        [DispId(30)]
        void OnConnectionBarPullDown();

        [DispId(32)]
        void OnNetworkStatusChanged(
            [In] uint qualityLevel,
            [In] int bandwidth,
            [In] int roundTripTime);

        [DispId(35)]
        void OnDevicesButtonPressed();

        [DispId(33)]
        void OnAutoReconnected();

        [DispId(34)]
        void OnAutoReconnecting2(
            [In] int disconnectReason,
            [In] bool networkAvailable,
            [In] int attemptCount,
            [In] int maxAttemptCount);
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class RdpEventSink(RdpActiveXHost owner) : IMsTscAxEvents
    {
        public void OnConnecting()
        {
        }

        public void OnConnected()
        {
        }

        public void OnLoginComplete()
        {
            owner.OnLoginComplete();
        }

        public void OnDisconnected(int disconnectReason)
        {
            owner.OnDisconnected(disconnectReason);
        }

        public void OnEnterFullScreenMode()
        {
        }

        public void OnLeaveFullScreenMode()
        {
        }

        public void OnChannelReceivedData(string channelName, string data)
        {
        }

        public void OnRequestGoFullScreen()
        {
        }

        public void OnRequestLeaveFullScreen()
        {
        }

        public void OnFatalError(int errorCode)
        {
            owner.OnFatalError(errorCode);
        }

        public void OnWarning(int warningCode)
        {
        }

        public void OnRemoteDesktopSizeChange(int width, int height)
        {
        }

        public void OnIdleTimeoutNotification()
        {
        }

        public void OnRequestContainerMinimize()
        {
        }

        public void OnConfirmClose(out bool allowClose)
        {
            allowClose = true;
        }

        public void OnReceivedTSPublicKey(
            string publicKey,
            out bool continueLogon)
        {
            continueLogon = true;
        }

        public void OnAutoReconnecting(
            int disconnectReason,
            int attemptCount,
            out AutoReconnectContinueState continueStatus)
        {
            continueStatus = AutoReconnectContinueState.Automatic;
        }

        public void OnAuthenticationWarningDisplayed()
        {
        }

        public void OnAuthenticationWarningDismissed()
        {
        }

        public void OnRemoteProgramResult(
            string remoteProgram,
            RemoteProgramResult error,
            bool isExecutable)
        {
        }

        public void OnRemoteProgramDisplayed(
            bool displayed,
            uint displayInformation)
        {
        }

        public void OnRemoteWindowDisplayed(
            bool displayed,
            ref RemotableHandle windowHandle,
            RemoteWindowDisplayedAttribute windowAttribute)
        {
        }

        public void OnLogonError(int errorCode)
        {
            owner.OnLogonError(errorCode);
        }

        public void OnFocusReleased(int direction)
        {
        }

        public void OnUserNameAcquired(string userName)
        {
        }

        public void OnMouseInputModeChanged(bool isRelativeMouseMode)
        {
        }

        public void OnServiceMessageReceived(string serviceMessage)
        {
        }

        public void OnConnectionBarPullDown()
        {
        }

        public void OnNetworkStatusChanged(
            uint qualityLevel,
            int bandwidth,
            int roundTripTime)
        {
        }

        public void OnDevicesButtonPressed()
        {
        }

        public void OnAutoReconnected()
        {
            owner.OnLoginComplete();
        }

        public void OnAutoReconnecting2(
            int disconnectReason,
            bool networkAvailable,
            int attemptCount,
            int maxAttemptCount)
        {
        }
    }

    private enum AutoReconnectContinueState
    {
        Automatic,
        Stop,
        Manual
    }

    private enum RemoteProgramResult
    {
        Ok,
        Locked,
        ProtocolError,
        NotInWhitelist,
        NetworkPathDenied,
        FileNotFound,
        Failure,
        HookNotLoaded
    }

    private enum RemoteWindowDisplayedAttribute
    {
        None,
        WindowDisplayed,
        ShellIconDisplayed
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RemotableHandle
    {
        internal int Context;
        internal RemotableHandleUnion Value;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct RemotableHandleUnion
    {
        [FieldOffset(0)]
        internal int InProcessHandle;

        [FieldOffset(0)]
        internal int RemoteHandle;
    }

    [ComImport]
    [Guid("302D8188-0052-4807-806A-362B628F9AC5")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMsRdpExtendedSettings
    {
        void set_Property(
            [In, MarshalAs(UnmanagedType.BStr)] string propertyName,
            [In, MarshalAs(UnmanagedType.Struct)] ref object value);

        [return: MarshalAs(UnmanagedType.Struct)]
        object get_Property([In, MarshalAs(UnmanagedType.BStr)] string propertyName);
    }

    [ComImport]
    [Guid("2F079C4C-87B2-4AFD-97AB-20CDB43038AE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMsRdpClientNonScriptable
    {
        void put_ClearTextPassword([In, MarshalAs(UnmanagedType.BStr)] string value);

        void put_PortablePassword([In, MarshalAs(UnmanagedType.BStr)] string value);

        [return: MarshalAs(UnmanagedType.BStr)]
        string get_PortablePassword();

        void put_PortableSalt([In, MarshalAs(UnmanagedType.BStr)] string value);

        [return: MarshalAs(UnmanagedType.BStr)]
        string get_PortableSalt();

        void put_BinaryPassword([In, MarshalAs(UnmanagedType.BStr)] string value);

        [return: MarshalAs(UnmanagedType.BStr)]
        string get_BinaryPassword();

        void put_BinarySalt([In, MarshalAs(UnmanagedType.BStr)] string value);

        [return: MarshalAs(UnmanagedType.BStr)]
        string get_BinarySalt();

        void ResetPassword();

        void NotifyRedirectDeviceChange(nuint wParam, nint lParam);

        void SendKeys(
            int numKeys,
            [In] ref short keyUpStates,
            [In] ref int keyData);
    }

    private readonly record struct RemoteKey(int ScanCode, bool IsExtended);

    private readonly record struct KeyStroke(RemoteKey Key, bool IsKeyUp);
}
