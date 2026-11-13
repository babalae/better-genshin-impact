using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace BetterGenshinImpact.Service.ChildSession;

/// <summary>
/// RDP 静默登录用的凭据。
/// </summary>
/// <param name="UserName">用户名。</param>
/// <param name="Domain">域，本地账户为机器名。</param>
/// <param name="Password">明文密码，仅存在于内存中。</param>
internal readonly record struct RdpCredential(string UserName, string Domain, string Password);

/// <summary>
/// 桌面分身自动登录。
///
/// 两条路，先用简单的那条：
/// 1. 连接前把用户名/密码直接交给 RDP 客户端（<c>IMsRdpClientNonScriptable::put_ClearTextPassword</c>），
///    正常情况下面向本机的凭据交换是静默的，根本不会弹窗；
/// 2. 有些环境（策略、Windows Hello、账户类型）仍会弹「Windows 安全中心」的凭据框，
///    而且有时连用户名一起要。这时用 UI Automation 把用户名和密码填进去并点确定。
///
/// 第 2 条是兜底，不是主路径——它依赖对方的 UI 结构，属于「能填就填，填不了就让人自己输」，
/// 所以有严格的重试上限，绝不无限重试。
///
/// 桌面分身只支持用**当前 Windows 账户**登录（由 Child Session 机制决定），
/// 所以这里不接受自定义账户名：密码按账户分开存，但每次只取当前账户那一条。
/// </summary>
internal sealed class ChildSessionAutoLoginService : IDisposable
{
    /// <summary>
    /// 「Windows 安全中心」凭据框的窗口类名。这个类名是实测得到的，
    /// 换系统版本可能变；变了只会导致兜底失效，不影响第 1 条路径。
    /// </summary>
    private const string CredentialDialogClassName = "Credential Dialog Xaml Host";

    /// <summary>
    /// 经典 Win32 凭据对话框的类名，靠标题关键字区分。
    /// </summary>
    private const string LegacyDialogClassName = "#32770";

    private static readonly string[] LegacyDialogTitleKeywords = ["安全", "Security", "凭据", "Credential"];
    private static readonly string[] PreferredConfirmNames =
    [
        "确定", "確定", "확인", "ОК",
        "OK", "Aceptar",
        "继续", "Continue",
        "登录", "Sign in",
        "提交", "Submit"
    ];

    private const string MicrosoftAccountPropertiesPath =
        @"Software\Microsoft\IdentityCRL\UserExtendedProperties";

    private const int Logon32LogonInteractive = 2;
    private const int Logon32ProviderDefault = 0;

    /// <summary>
    /// 一次连接最多自动填几次。凭据框反复出现通常意味着密码本身就是错的，
    /// 继续填只会把账户往锁定策略上推。
    /// </summary>
    private const int MaxFillAttempts = 1;

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan WatchDuration = TimeSpan.FromSeconds(60);

    private readonly ChildSessionConfig _config;

    /// <summary>
    /// 保护 <see cref="ChildSessionConfig.AutoLoginPasswords"/>：界面线程在存/清密码，
    /// 监听线程同时在读，两边都要走同一把锁。
    /// </summary>
    private readonly object _passwordLock = new();
    private readonly Action _saveConfig;
    private readonly ILogger _logger = ChildSessionDiagnosticsLog.Instance;

    private CancellationTokenSource? _watchCancellation;
    private IntPtr _lastFilledDialog;
    private bool _disposed;

    internal ChildSessionAutoLoginService(ChildSessionConfig config, Action saveConfig)
    {
        _config = config;
        _saveConfig = saveConfig;
    }

    /// <summary>
    /// 用户是否打开了自动登录开关。
    /// </summary>
    internal bool IsEnabled
    {
        get => _config.AutoLoginEnabled;
        set
        {
            if (_config.AutoLoginEnabled == value)
            {
                return;
            }

            _config.AutoLoginEnabled = value;
            _saveConfig();

            // 关掉开关就别再填了：监听可能正跑在连接过程中间，
            // 不停的话它还会把下一次弹出来的凭据框填掉。
            if (!value)
            {
                StopWatching();
            }
        }
    }

    /// <summary>
    /// 自动登录使用的账户名：固定为当前 Windows 账户。
    ///
    /// 桌面分身只支持用当前账户登录（这是 Child Session 的机制），
    /// 所以这里不接受自定义账户名，也不给界面提供修改入口。
    /// </summary>
    internal string UserName => Environment.UserName;

    /// <summary>
    /// 当前账户在密码表里的键。用户名不区分大小写。
    /// </summary>
    internal string AccountKey => UserName.ToLowerInvariant();

    /// <summary>
    /// 当前账户是否已经存过密码（且能解开）。
    /// 密码按账户分开存，这里只回答「当前账户」有没有。
    /// </summary>
    internal bool HasStoredPassword => !string.IsNullOrEmpty(ReadPassword());

    /// <summary>
    /// 密码校验结论。
    /// </summary>
    internal enum PasswordCheckResult
    {
        /// <summary>密码当前有效。</summary>
        Valid,

        /// <summary>用户名或密码错误。</summary>
        InvalidPassword,

        /// <summary>账户被禁用。</summary>
        AccountDisabled,

        /// <summary>密码已过期。</summary>
        PasswordExpired,

        /// <summary>账户限制（空密码、登录时段、工作站限制）。</summary>
        AccountRestricted,

        /// <summary>账户已被锁定（连续失败次数过多）。</summary>
        AccountLocked,

        /// <summary>必须先修改密码才能登录。</summary>
        PasswordMustChange,

        /// <summary>无法判定（权限不足或未知错误）。</summary>
        Unknown
    }

    /// <summary>
    /// 保存密码（DPAPI 加密后写进配置）。只影响当前账户，不动其它账户的记录。
    /// </summary>
    /// <returns>加密失败时返回 false。</returns>
    internal bool SavePassword(string plainText)
    {
        ThrowIfDisposed();
        var protectedValue = DpapiProtector.Protect(plainText);
        if (protectedValue is null)
        {
            return false;
        }

        // 密码表是「界面线程写、监听线程读」的共享字典：删键和加键必须原子完成，
        // 否则那边正在枚举键名时会直接抛「集合已修改」。
        lock (_passwordLock)
        {
            // 同一个账户可能以不同大小写存在过，先删掉忽略大小写等价的那一条，
            // 避免同一个账户堆出两份密码。
            var existing = FindAccountKey(AccountKey);
            if (existing is not null)
            {
                _config.AutoLoginPasswords.Remove(existing);
            }

            _config.AutoLoginPasswords[UserName] = protectedValue;
            _saveConfig();
        }

        return true;
    }

    /// <summary>
    /// 清除「当前账户」已保存的密码；其它账户的记录保留。
    /// </summary>
    internal void ClearPassword()
    {
        ThrowIfDisposed();
        lock (_passwordLock)
        {
            var existing = FindAccountKey(AccountKey);
            if (existing is not null && _config.AutoLoginPasswords.Remove(existing))
            {
                _saveConfig();
            }
        }
    }

    /// <summary>
    /// 校验一组凭据当前是否有效。
    ///
    /// 用 <c>LogonUser</c> 做一次**交互式**登录验证（LOGON32_LOGON_INTERACTIVE）：
    /// 不产生界面、不创建进程、不加载用户配置文件。
    ///
    /// 为什么不用网络登录（NETWORK）：那只比对 LSA 里的本地验证器。微软账户在云端改密后，
    /// 本机缓存仍是旧密码，网络登录会对新密码返回 1326——看起来像密码错了。
    /// 交互式登录会先比对缓存、不匹配则**联网校验并回写缓存**，实测改密后走这条路
    /// 既能得到真实结论，顺手把缓存也修好了。
    /// </summary>
    internal PasswordCheckResult VerifyPassword(string password, out int errorCode)
    {
        var (userName, domain) = ResolveLogonIdentity();
        errorCode = 0;

        if (LogonUserW(
                userName,
                domain,
                password,
                Logon32LogonInteractive,
                Logon32ProviderDefault,
                out var token))
        {
            if (token != IntPtr.Zero)
            {
                _ = CloseHandle(token);
            }

            return PasswordCheckResult.Valid;
        }

        errorCode = Marshal.GetLastPInvokeError();
        return errorCode switch
        {
            1326 => PasswordCheckResult.InvalidPassword,
            1909 => PasswordCheckResult.AccountLocked,
            1907 => PasswordCheckResult.PasswordMustChange,
            1331 => PasswordCheckResult.AccountDisabled,
            1330 => PasswordCheckResult.PasswordExpired,
            1327 or 1385 => PasswordCheckResult.AccountRestricted,
            _ => PasswordCheckResult.Unknown
        };
    }

    /// <summary>
    /// 取出一组可用于 RDP 静默登录的凭据。开关关闭或没存密码时返回 false。
    /// </summary>
    internal bool TryGetCredential(out RdpCredential credential)
    {
        credential = default;
        if (!IsEnabled)
        {
            return false;
        }

        var password = ReadPassword();
        if (string.IsNullOrEmpty(password))
        {
            return false;
        }

        // 用与校验完全相同的身份：凭据走 Domain 属性、校验走另一个域时，
        // 域成员机器上会出现「验得过却连不上」。
        var (userName, domain) = ResolveLogonIdentity();
        credential = new RdpCredential(userName, domain ?? string.Empty, password);
        return true;
    }

    /// <summary>
    /// 开始监听凭据弹窗。每次连接尝试调用一次。
    /// </summary>
    internal void StartWatching()
    {
        if (_disposed || !HasStoredPassword)
        {
            return;
        }

        if (!IsEnabled)
        {
            return;
        }

        // 已经在监听就直接返回：状态轮询会反复调用，重启会把重试次数清零。
        if (_watchCancellation is not null)
        {
            return;
        }

        _lastFilledDialog = IntPtr.Zero;

        var cancellation = new CancellationTokenSource();
        _watchCancellation = cancellation;
        _ = Task.Run(() => WatchAsync(cancellation));
    }

    /// <summary>
    /// 停止监听。连接结束或失败时调用。
    /// </summary>
    internal void StopWatching()
    {
        var cancellation = _watchCancellation;
        _watchCancellation = null;
        if (cancellation is null)
        {
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        cancellation.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopWatching();
    }

    private async Task WatchAsync(CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        try
        {
            await WatchCoreAsync(token);
        }
        finally
        {
            // 超时退出也要摘掉引用：否则下一次 StartWatching 会以为「还在监听」直接返回，
            // 这个进程里就再也起不来了。停止时由 StopWatching 负责取消与释放。
            if (ReferenceEquals(_watchCancellation, cancellation))
            {
                _watchCancellation = null;
            }
        }
    }

    private async Task WatchCoreAsync(CancellationToken token)
    {
        var deadline = DateTime.UtcNow + WatchDuration;
        var attempts = 0;

        _logger.LogInformation("自动登录：开始监听 Windows 凭据弹窗。");

        while (!token.IsCancellationRequested
               && attempts < MaxFillAttempts
               && DateTime.UtcNow < deadline)
        {
            try
            {
                await Task.Delay(PollInterval, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (token.IsCancellationRequested)
            {
                return;
            }

            if (!TryFillCredentialDialog())
            {
                continue;
            }

            attempts++;
            _logger.LogInformation(
                "自动登录：已填写凭据弹窗（第 {Attempts} 次，上限 {MaxAttempts}）。",
                attempts,
                MaxFillAttempts);

            if (attempts >= MaxFillAttempts)
            {
                _logger.LogWarning("自动登录：已达到本次连接的重试上限，停止自动填写。");
            }
        }
    }

    /// <summary>
    /// 找到凭据弹窗就填一次。没有弹窗或填不进去返回 false。
    /// </summary>
    private bool TryFillCredentialDialog()
    {
        // 监听到一半用户把开关关了：这次就不填了。
        if (!IsEnabled)
        {
            return false;
        }

        var handle = FindCredentialDialog();
        if (handle == IntPtr.Zero || handle == _lastFilledDialog)
        {
            return false;
        }

        var password = ReadPassword();
        if (string.IsNullOrEmpty(password))
        {
            return false;
        }

        try
        {
            var dialog = AutomationElement.FromHandle(handle);
            if (dialog is null)
            {
                return false;
            }

            var edits = dialog.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));

            AutomationElement? passwordField = null;
            AutomationElement? userNameField = null;

            foreach (AutomationElement edit in edits)
            {
                try
                {
                    if (!edit.Current.IsEnabled)
                    {
                        continue;
                    }

                    if (edit.Current.IsPassword)
                    {
                        passwordField ??= edit;
                    }
                    else
                    {
                        userNameField ??= edit;
                    }
                }
                catch (ElementNotAvailableException)
                {
                    // 界面在遍历过程中重建了，这一轮直接放弃，下次轮询再来。
                    return false;
                }
            }

            if (passwordField is null)
            {
                return false;
            }

            // 用解析出来的登录身份填：微软账户要在凭据框里写邮箱，本地账户写用户名。
            var (resolvedUserName, resolvedDomain) = ResolveLogonIdentity();

            // 非微软账户时框里带「域\用户」：本地账户的域是机器名、域账户是域，
            // 同名不同域是两个账户，只填后半段可能登到别人头上。微软账户例外——框里要的是邮箱。
            var dialogUserName = resolvedUserName;
            if (!string.IsNullOrEmpty(resolvedDomain)
                && !string.Equals(resolvedDomain, "MicrosoftAccount", StringComparison.OrdinalIgnoreCase))
            {
                dialogUserName = resolvedDomain + "\\" + resolvedUserName;
            }

            // 用户名只在字段为空时填，用户自己输过的不覆盖。
            // 这里不校验「框里显示的用户名是否等于当前账户」：安全中心的凭据框显示的
            // 用户名什么都可能（尤其只剩密码框时），不一致也照常填密码登录。
            if (userNameField is not null)
            {
                TrySetValue(userNameField, dialogUserName, onlyWhenEmpty: true);
            }

            if (!TrySetValue(passwordField, password, onlyWhenEmpty: false))
            {
                return false;
            }

            // 密码已经写进去了，这个对话框就到此为止：即使下面点不到确认按钮，
            // 也不能再回来重填一次——那会盖掉用户自己的修改。
            _lastFilledDialog = handle;

            // 密码写入成功就算「填过这一次」：确认按钮点不上只是少点一下，
            // 不影响「本次连接的填写额度」已经被用掉——否则用户手动提交后新弹的框
            // 会被无限重填，绕过一次连接最多填一次的上限。
            if (!TryInvokeConfirm(dialog))
            {
                _logger.LogWarning("自动登录：已填入密码，但没找到可点击的确认按钮，请手动确认。");
            }

            return true;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                              or NotSupportedException
                                              or COMException)
        {
            _logger.LogWarning(exception, "自动登录：填写凭据弹窗失败。");
            return false;
        }
    }

    /// <summary>
    /// 找当前存在的凭据弹窗。现代系统上是 XAML 版，部分配置下走经典 Win32 对话框，两种都要认。
    /// </summary>
    private static IntPtr FindCredentialDialog()
    {
        var handle = WindowNativeMethods.FindWindowByClassName(CredentialDialogClassName);
        if (handle != IntPtr.Zero)
        {
            return handle;
        }

        foreach (var keyword in LegacyDialogTitleKeywords)
        {
            handle = WindowNativeMethods.FindWindowByClassName(LegacyDialogClassName, keyword);
            if (handle != IntPtr.Zero)
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    private static bool TrySetValue(AutomationElement element, string value, bool onlyWhenEmpty)
    {
        try
        {
            if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern)
                || pattern is not ValuePattern valuePattern)
            {
                return false;
            }

            if (valuePattern.Current.IsReadOnly)
            {
                return false;
            }

            if (onlyWhenEmpty && !string.IsNullOrWhiteSpace(valuePattern.Current.Value))
            {
                return true;
            }

            valuePattern.SetValue(value);
            return true;
        }
        catch (Exception exception) when (exception is ElementNotAvailableException
                                              or InvalidOperationException
                                              or ElementNotEnabledException)
        {
            return false;
        }
    }

    /// <summary>
    /// 点凭据框里的确认按钮。
    ///
    /// 只点「认得出来」的确认按钮：AutomationId 是 OkButton，或文案在确认名单里。
    /// 不做「只剩一个可用按钮就点」的兜底——取消按钮的文案随系统语言变化，
    /// 认不出来时那个唯一的按钮往往正是「取消」，点下去等于替用户放弃登录，
    /// 而调用方还会把它记成「已经填过」，这一轮就再也不会重试。
    /// </summary>
    private static bool TryInvokeConfirm(AutomationElement dialog)
    {
        var buttons = dialog.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));

        AutomationElement? target = null;

        foreach (AutomationElement button in buttons)
        {
            try
            {
                if (!button.Current.IsEnabled)
                {
                    continue;
                }

                if (string.Equals(button.Current.AutomationId, "OkButton", StringComparison.OrdinalIgnoreCase)
                    || Array.Exists(
                        PreferredConfirmNames,
                        candidate => string.Equals(candidate, button.Current.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    target = button;
                    break;
                }
            }
            catch (ElementNotAvailableException)
            {
                return false;
            }
        }

        if (target is null)
        {
            return false;
        }

        try
        {
            if (!target.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern)
                || pattern is not InvokePattern invokePattern)
            {
                return false;
            }

            invokePattern.Invoke();
            return true;
        }
        catch (Exception exception) when (exception is ElementNotAvailableException
                                              or InvalidOperationException
                                              or ElementNotEnabledException)
        {
            return false;
        }
    }

    /// <summary>
    /// 决定 <c>LogonUser</c> 用哪个用户名/域：微软账户用 UPN + MicrosoftAccount，本地账户用当前域/机器名。
    ///
    /// 桌面分身只允许当前账户登录，所以只解析「当前登录身份」，不接受自定义账户。
    /// </summary>
    private static (string UserName, string? Domain) ResolveLogonIdentity()
    {
        // 当前账户是微软账户时要用邮箱 + MicrosoftAccount：本地用户名（C:\Users 下那个）不是它。
        if (TryGetMicrosoftAccountEmail(out var email))
        {
            return (email, "MicrosoftAccount");
        }

        // 域成员机器上 UserDomainName 是域、本地账户时是机器名，
        // 直接用它才能和系统里的登录身份对上（写死机器名会让域账户验证失败）。
        return (Environment.UserName, Environment.UserDomainName);
    }

    /// <summary>
    /// 尝试读取当前账户的微软账户邮箱。
    ///
    /// 该邮箱是 <c>runas /u:MicrosoftAccount\&lt;邮箱&gt;</c> 与 <c>LogonUser</c> 需要的用户名，
    /// 本地用户名（C:\Users 下那个）并不是它。
    /// </summary>
    private static bool TryGetMicrosoftAccountEmail(out string email)
    {
        email = string.Empty;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(MicrosoftAccountPropertiesPath);
            if (key is null)
            {
                return false;
            }

            // 与本机登录账户的绑定靠注册表名，取第一个带 @ 的。
            // 一台机器上登记多个身份时注册表顺序不保证，取错就会拿正确密码去撞别的账户
            // （表现是「密码明明是对的却报密码错」），所以至少留一条诊断线索。
            var candidates = 0;
            foreach (var name in key.GetSubKeyNames())
            {
                if (!name.Contains('@'))
                {
                    continue;
                }

                candidates++;
                if (email.Length == 0)
                {
                    email = name;
                }
            }

            if (candidates > 1)
            {
                ChildSessionDiagnosticsLog.Instance.LogWarning(
                    null,
                    "本机登记了 {Count} 个微软账户，已按第一个（{Email}）登录；若提示密码错误，请关闭自动登录后手动输入。",
                    candidates,
                    email);
            }

            if (email.Length > 0)
            {
                return true;
            }
        }
        catch (Exception exception) when (exception is SecurityException
                                              or UnauthorizedAccessException
                                              or IOException)
        {
            ChildSessionDiagnosticsLog.Instance.LogDebug(exception, "读取微软账户邮箱失败。");
        }

        return false;
    }

    private string? ReadPassword()
    {
        string? protectedValue;
        lock (_passwordLock)
        {
            var key = FindAccountKey(AccountKey);
            if (key is null || !_config.AutoLoginPasswords.TryGetValue(key, out protectedValue)
                || string.IsNullOrWhiteSpace(protectedValue))
            {
                return null;
            }
        }

        return DpapiProtector.Unprotect(protectedValue);
    }

    /// <summary>
    /// 在密码表里找与给定账户名忽略大小写相等的键。
    /// </summary>
    private string? FindAccountKey(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        foreach (var key in _config.AutoLoginPasswords.Keys)
        {
            if (string.Equals(key, userName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return key;
            }
        }

        return null;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ChildSessionAutoLoginService));
        }
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LogonUserW(
        string userName,
        string? domain,
        string password,
        int logonType,
        int logonProvider,
        out IntPtr token);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}

/// <summary>
/// 按类名（可再按标题关键字）找顶层窗口。凭据框是本进程 RDP 控件弹出来的子窗口，
/// 用 UI Automation 只能先拿到 HWND。
/// </summary>
internal static class WindowNativeMethods
{
    private const int MaxClassNameLength = 256;

    internal static IntPtr FindWindowByClassName(string className, string? titleKeyword = null)
    {
        var found = IntPtr.Zero;

        _ = EnumWindows(
            (handle, _) =>
            {
                if (!IsWindowVisible(handle))
                {
                    return true;
                }

                var buffer = new StringBuilder(MaxClassNameLength);
                if (GetClassName(handle, buffer, MaxClassNameLength) == 0)
                {
                    return true;
                }

                if (!string.Equals(buffer.ToString(), className, StringComparison.Ordinal))
                {
                    return true;
                }

                if (titleKeyword is not null)
                {
                    var titleBuffer = new StringBuilder(512);
                    if (GetWindowText(handle, titleBuffer, titleBuffer.Capacity) == 0)
                    {
                        return true;
                    }

                    if (!titleBuffer.ToString().Contains(titleKeyword, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                found = handle;
                return false;
            },
            IntPtr.Zero);

        return found;
    }

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr param);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr handle, StringBuilder className, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder text, int maxCount);

}
