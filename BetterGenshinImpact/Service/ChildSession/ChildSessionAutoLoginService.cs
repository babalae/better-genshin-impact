using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Win32;
using Meziantou.Framework.Win32;
using Microsoft.Extensions.Logging;

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
/// 连接前把用户名/密码直接交给 RDP 客户端（<c>IMsRdpClientNonScriptable::put_ClearTextPassword</c>），
/// 正常情况下面向本机的凭据交换是静默的，根本不会弹窗。
///
/// 密码保存在 **Windows 凭据管理器**（按账户区分），不写入配置文件。
/// 少数环境仍会弹「Windows 安全中心」凭据框，此时只把它拉到前台让用户手动输入，
/// 不再自动填写密码。
///
/// 桌面分身只支持用**当前 Windows 账户**登录（由 Child Session 机制决定），
/// 所以这里不接受自定义账户名：密码按账户分开存，但每次只取当前账户那一条。
/// </summary>
internal sealed class ChildSessionAutoLoginService : IDisposable
{
    /// <summary>
    /// 「Windows 安全中心」凭据框的窗口类名，用于全屏时把它拉到前台。
    /// </summary>
    private const string CredentialDialogClassName = "Credential Dialog Xaml Host";

    /// <summary>
    /// 经典 Win32 凭据对话框的类名，靠标题关键字区分。
    /// </summary>
    private const string LegacyDialogClassName = "#32770";

    private static readonly string[] LegacyDialogTitleKeywords = ["安全", "Security", "凭据", "Credential"];

    private const int Logon32LogonInteractive = 2;
    private const int Logon32ProviderDefault = 0;

    private readonly ChildSessionConfig _config;
    private readonly Action _saveConfig;
    private readonly ILogger _logger = ChildSessionDiagnosticsLog.Instance;

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
    /// 保存密码。交给 Windows 凭据管理器，**不写入配置文件**。
    /// 凭据名按账户区分，只影响当前账户。
    /// </summary>
    /// <returns>写入失败时返回 false。</returns>
    internal bool SavePassword(string plainText)
    {
        ThrowIfDisposed();
        if (string.IsNullOrEmpty(plainText))
        {
            return false;
        }

        try
        {
            CredentialManagerHelper.SaveCredential(
                GetCredentialTargetName(),
                UserName,
                plainText,
                "BetterGI 桌面分身自动登录",
                CredentialPersistence.LocalMachine);
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "自动登录：保存密码到 Windows 凭据管理器失败。");
            return false;
        }
    }

    /// <summary>
    /// 清除「当前账户」已保存的密码；其它账户的凭据保留。
    /// </summary>
    /// <returns>凭据确实被删掉时返回 true。</returns>
    internal bool ClearPassword()
    {
        ThrowIfDisposed();
        CredentialManagerHelper.DeleteCredential(GetCredentialTargetName());

        // Helper 内部吞掉异常且不返回结果，这里回读确认：删不掉时要让调用方知道，
        // 否则界面会显示「已清除」，下次连接又拿旧密码去登录。
        if (HasStoredPassword)
        {
            _logger.LogWarning("自动登录：清除已保存的密码失败，凭据管理器里仍有该账户的记录。");
            return false;
        }

        return true;
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
        errorCode = 0;

        // 身份总是能解析出来：本地/域账户用「用户名 + 域」，微软账户用 UPN + NULL 域（LogonUserW 文档规定 UPN 必须配 NULL）。
        var (userName, domain) = ResolveLogonIdentity();

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

        // RDP 登录用本地身份即可：微软账户用「本地用户名 + 机器名域」同样能登录
        // （本地用户名就是 C:\Users 下那个文件夹名）。不需要解析邮箱——那是 LogonUser
        // 校验密码才需要的，跟 RDP 登录是两条路。
        credential = new RdpCredential(
            Environment.UserName,
            Environment.UserDomainName,
            password);
        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
    }

    /// <summary>
    /// 找当前存在的凭据弹窗。现代系统上是 XAML 版，部分配置下走经典 Win32 对话框，两种都要认。
    /// 全屏窗口的置顶管理也要用它主动探测弹窗，故设为 internal。
    /// </summary>
    internal static IntPtr FindCredentialDialog()
    {
        // 现代凭据框（XAML 版）在 Windows 10/11 上由 CredentialUIBroker.exe 宿主，
        // 不是本进程创建的 —— 按本进程过滤会把真正的 RDP 凭据框排除掉，全屏时把它
        // 拉到前台也会失效。
        //
        // 但类名是任何进程都能注册的：同账户下别的程序可以造一个同名、同样有密码框的
        // 窗口，等着我们把密码填进去。所以这里额外校验宿主进程的可执行文件路径。
        // 校验直接放进枚举：只取第一个同名窗口的话，别人先造一个同名的假窗口
        // 就能把真正的凭据框挡在外面，全屏时把它拉到前台会失效。
        var handle = WindowNativeMethods.FindWindowByClassName(
            CredentialDialogClassName,
            accept: IsCredentialDialogForThisApp);
        if (handle != IntPtr.Zero)
        {
            return handle;
        }

        // 经典 Win32 凭据框走的是 #32770 —— 这是所有对话框的通用类名（UAC、别的
        // RDP 客户端、各种登录框），必须限定本进程，否则会把密码填进别人的框里。
        // 经典样式的凭据框是本进程内的 RDP ActiveX 弹的，过滤不会误伤。
        var ownProcessId = Environment.ProcessId;
        foreach (var keyword in LegacyDialogTitleKeywords)
        {
            handle = WindowNativeMethods.FindWindowByClassName(
                LegacyDialogClassName,
                keyword,
                processId: ownProcessId);
            if (handle != IntPtr.Zero)
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// 这个凭据框是不是给我们这次 RDP 连接弹的。
    ///
    /// 判据是宿主进程必须是系统的 CredentialUIBroker.exe（见
    /// <see cref="IsCredentialBrokerWindow"/>）。不能靠 owner 窗口判断——现代凭据框
    /// 由 CredentialUIBroker 独立弹出，不挂在本进程窗口链上，<c>GW_OWNER</c> 拿不到
    /// owner；按 owner 过滤会把真实的 RDP 凭据框也拒掉，全屏拉前台直接失效。
    /// 防伪造靠宿主路径校验：伪造进程无法把自己的可执行文件路径伪装成
    /// System32\CredentialUIBroker.exe。
    /// </summary>
    private static bool IsCredentialDialogForThisApp(IntPtr window)
    {
        return WindowNativeMethods.IsCredentialBrokerWindow(window);
    }

    /// <summary>
    /// 决定 <c>LogonUser</c> 用哪个用户名/域。
    ///
    /// 判据是当前登录身份本身（<c>GetUserNameEx(NameUserPrincipal)</c> 的 UPN），而不是
    /// 注册表里的微软账户残留：本地账户读不到含 @ 的 UPN，直接回落到「用户名 + 当前域」；
    /// 微软账户的 UPN 就是邮箱，用它 + NULL 域（LogonUserW 文档规定 UPN 必须配 NULL）。
    ///
    /// 桌面分身只允许当前账户登录，所以只解析「当前登录身份」，不接受自定义账户。
    /// </summary>
    private static (string UserName, string? Domain) ResolveLogonIdentity()
    {
        // 微软账户的 UPN 就是邮箱（例如 batvbs@qq.com）。但要和域账户区分开：
        // 域账户也有 UPN（alex@corp.example），同样走 UPN + NULL，不能进微软账户分支。
        // 判据是「是不是域成员」：域账户登录时 UserDomainName 是域名（≠ 机器名），
        // 微软账户和本地账户登录时 UserDomainName 都是机器名。
        if (!string.Equals(
                Environment.UserDomainName,
                Environment.MachineName,
                StringComparison.OrdinalIgnoreCase))
        {
            // 域成员机器：一律按域身份（DOMAIN\user）登录，不走微软账户分支。
            return (Environment.UserName, Environment.UserDomainName);
        }

        // 非域机器：本地账户的 UPN 查询返回 机器名\用户名（不含 @）或失败；
        // 微软账户返回邮箱（含 @）。
        if (WindowNativeMethods.TryGetCurrentUserPrincipal(out var upn)
            && upn.Contains('@'))
        {
            // LogonUserW 的文档规定：用户名用 UPN 格式（User@DNSDomain）时，domain 必须为 NULL。
            // 传 "MicrosoftAccount" 是未定义行为，即使实测能通过也不该依赖。
            return (upn, null);
        }

        // 本地账户：用户名 + 机器名。
        return (Environment.UserName, Environment.UserDomainName);
    }

    /// <summary>
    /// 从 Windows 凭据管理器读出当前账户的密码。
    /// </summary>
    private string? ReadPassword()
    {
        return CredentialManagerHelper.ReadCredential(GetCredentialTargetName())?.Password;
    }

    /// <summary>
    /// 当前账户对应的凭据名。按账户区分，避免多账户互相覆盖。
    /// </summary>
    private string GetCredentialTargetName()
    {
        return $"BetterGI:ChildSession:AutoLogin:{AccountKey}";
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
/// 按类名（可再按标题关键字）找顶层窗口。凭据框由独立进程宿主，只能按窗口句柄识别。
/// </summary>
internal static class WindowNativeMethods
{
    private const int MaxClassNameLength = 256;

    /// <summary>
    /// <c>GetUserNameEx</c> 的格式代码：用户主体名（UPN，微软账户即邮箱）。
    /// </summary>
    private const int NameUserPrincipal = 8;

    /// <summary>
    /// 现代凭据框的宿主程序。它在 System32 下，由系统启动并托管凭据 UI。
    /// </summary>
    internal static readonly string CredentialBrokerPath =
        Path.Combine(Environment.SystemDirectory, "CredentialUIBroker.exe");

    /// <summary>
    /// 现代凭据框的宿主是不是系统的 CredentialUIBroker.exe。
    ///
    /// 只比进程名不够：同账户的进程可以把自己的可执行文件也命名成这个名字。
    /// 这里比完整的可执行文件路径。读不到路径（权限不足等）一律当作「不是」——
    /// 宁可这次认不出来，也不能把来路不明的窗口当成凭据框。
    /// </summary>
    internal static bool IsCredentialBrokerWindow(IntPtr window)
    {
        GetWindowThreadProcessId(window, out var processId);
        if (processId == 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            return string.Equals(
                process.MainModule?.FileName,
                CredentialBrokerPath,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException
                                              or InvalidOperationException
                                              or NotSupportedException
                                              or System.ComponentModel.Win32Exception)
        {
            ChildSessionDiagnosticsLog.Instance.LogDebug(
                exception, "读取凭据框宿主进程失败，本次不认作凭据框。");
            return false;
        }
    }

    /// <summary>
    /// 取当前登录身份的 UPN（微软账户就是邮箱）。
    ///
    /// 本地账户返回的是 <c>机器名\用户名</c>，不含 <c>@</c>，按失败处理。
    /// </summary>
    internal static bool TryGetCurrentUserPrincipal(out string upn)
    {
        upn = string.Empty;

        var buffer = new StringBuilder(512);
        var size = buffer.Capacity;
        if (!GetUserNameEx(NameUserPrincipal, buffer, ref size) || size <= 0)
        {
            return false;
        }

        var value = buffer.ToString(0, Math.Min(size, buffer.Length));
        if (!value.Contains('@'))
        {
            return false;
        }

        upn = value;
        return true;
    }


    /// <summary>设置窗口位置时不改变尺寸。</summary>
    private const uint SwpNoSize = 0x0001;

    /// <summary>设置窗口位置时不改变水平/垂直坐标。</summary>
    private const uint SwpNoMove = 0x0002;

    /// <summary>设置窗口位置时不激活窗口。</summary>
    private const uint SwpNoActivate = 0x0010;

    /// <summary><c>ShowWindow</c> 的 SW_RESTORE：把最小化的窗口还原并显示。</summary>
    internal const int SwRestore = 9;

    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNotopmost = new(-2);

    /// <summary>
    /// 把窗口移入/移出置顶带（WS_EX_TOPMOST）。
    ///
    /// 用 Win32 直接改而不是走 WPF 的 Topmost 依赖属性：凭据框是模态的，UI 线程会卡在
    /// 它的消息循环里，WPF 的 Dispatcher 派发不了属性赋值；SetWindowPos 不经过 Dispatcher，
    /// 任何时候都能立刻生效。
    /// </summary>
    internal static bool SetWindowTopmost(IntPtr window, bool topmost)
    {
        return SetWindowPos(
            window,
            topmost ? HwndTopmost : HwndNotopmost,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    /// <summary>请求把窗口设为前台窗口。</summary>
    internal static bool SetForegroundWindow(IntPtr window) => SetForegroundWindowCore(window);

    /// <summary>按 <paramref name="command"/> 显示窗口（例如 SW_RESTORE）。</summary>
    internal static bool ShowWindow(IntPtr window, int command) => ShowWindowCore(window, command);

    /// <summary>把窗口提到所在 Z 序带的顶部（不会越过置顶窗口）。</summary>
    internal static bool BringWindowToTop(IntPtr window) => BringWindowToTopCore(window);

    /// <summary>窗口句柄是否仍然有效。</summary>
    internal static bool IsWindow(IntPtr window) => window != IntPtr.Zero && IsWindowCore(window);

    internal static IntPtr FindWindowByClassName(
        string className,
        string? titleKeyword = null,
        int? processId = null,
        Func<IntPtr, bool>? accept = null)
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

                // 只认指定进程的窗口：`#32770` 这类通用类名同时属于别的程序
                // （mstsc、OneDrive、VPN 客户端……），不过滤会把本程序的凭据填到别人框里。
                if (processId is not null)
                {
                    GetWindowThreadProcessId(handle, out var windowProcessId);
                    if (windowProcessId != processId.Value)
                    {
                        return true;
                    }
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

                // 类名可以重复：第一个同名窗口未必是要找的那个（同账户的进程也能注册
                // 同名类名）。交给人审一下，不合格就继续枚举下一个。
                if (accept is not null && !accept(handle))
                {
                    return true;
                }

                found = handle;
                return false;
            },
            IntPtr.Zero);

        return found;
    }


    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr param);

    [DllImport("secur32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    // GetUserNameEx 返回的是 BOOLEAN（单字节），不是 4 字节 BOOL：按 Bool 读会把高位的脏数据
    // 当成 true，失败也当成功。必须用 U1。
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool GetUserNameEx(int nameFormat, StringBuilder userName, ref int userNameSize);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr handle, out int processId);

    /// <summary><c>GetWindow</c> 的 GW_OWNER：取弹出该窗口的 owner（模态对话框一般有）。</summary>
    internal const uint GwOwner = 4;

    [DllImport("user32.dll")]
    internal static extern IntPtr GetWindow(IntPtr handle, uint command);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr handle, StringBuilder className, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder text, int maxCount);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    // 注意 EntryPoint：C# 方法名带了 Core 后缀避免与上面的包装方法重名，
    // 但原生入口没有这个后缀，必须显式指回 user32.dll 里的真实名字。
    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindowCore(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "ShowWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowCore(IntPtr window, int command);

    [DllImport("user32.dll", EntryPoint = "BringWindowToTop")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTopCore(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "IsWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowCore(IntPtr window);
}
