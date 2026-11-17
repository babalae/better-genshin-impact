using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace BetterGenshinImpact.Service.ChildSession;

/// <summary>
/// 只读 WTS 会话查询与会话断开操作，不读取或修改系统安全策略。
/// </summary>
internal static class UuRemoteNativeMethods
{
    /// <summary>WTS_INFO_CLASS 中的 WTSUserName。</summary>
    private const int WtsInfoUserName = 5;

    /// <summary>WTS_INFO_CLASS 中的 WTSDomainName。</summary>
    private const int WtsInfoDomainName = 7;

    /// <summary>取得物理控制台会话，切换期间可能返回 uint.MaxValue。</summary>
    [DllImport("kernel32.dll")]
    internal static extern uint WTSGetActiveConsoleSessionId();

    /// <summary>枚举本机 WTS 会话。</summary>
    [DllImport("wtsapi32.dll", EntryPoint = "WTSEnumerateSessionsW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSEnumerateSessions(
        IntPtr serverHandle, int reserved, int version, out IntPtr sessionInfo, out int count);

    /// <summary>读取指定会话的 WTS 信息。</summary>
    [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformation(
        IntPtr serverHandle, int sessionId, int infoClass, out IntPtr buffer, out int bytesReturned);

    /// <summary>释放 WTS 分配的缓冲区。</summary>
    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);

    /// <summary>只断开会话连接，不注销会话。</summary>
    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSDisconnectSession(
        IntPtr serverHandle, int sessionId, [MarshalAs(UnmanagedType.Bool)] bool wait);

    /// <summary>请求断开会话，并保留 Win32 错误用于诊断。</summary>
    internal static bool DisconnectSession(uint sessionId, out int errorCode)
    {
        if (WTSDisconnectSession(IntPtr.Zero, (int)sessionId, false))
        {
            errorCode = 0;
            return true;
        }

        errorCode = Marshal.GetLastPInvokeError();
        return false;
    }

    /// <summary>枚举会话并解析用户身份；无法确认身份的活动会话由上层保守拒绝修复。</summary>
    internal static IReadOnlyList<WindowsSessionInfo> EnumerateSessions()
    {
        if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out var sessionInfo, out var count))
        {
            var error = Marshal.GetLastPInvokeError();
            throw new Win32Exception(error, $"无法枚举 Windows 会话（Win32 错误 {error}）。");
        }

        try
        {
            var sessions = new WindowsSessionInfo[count];
            var structSize = Marshal.SizeOf<WtsSessionInfo>();
            var userSids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < count; index++)
            {
                var item = Marshal.PtrToStructure<WtsSessionInfo>(IntPtr.Add(sessionInfo, index * structSize));
                var userName = QueryString(item.SessionId, WtsInfoUserName);
                var domainName = QueryString(item.SessionId, WtsInfoDomainName);
                var accountName = string.IsNullOrWhiteSpace(domainName) ? userName : $"{domainName}\\{userName}";
                if (!userSids.TryGetValue(accountName, out var userSid))
                {
                    userSid = ResolveSid(userName, domainName);
                    userSids.Add(accountName, userSid);
                }

                sessions[index] = new WindowsSessionInfo(
                    (uint)item.SessionId,
                    ToSessionState(item.State),
                    item.WinStationName,
                    userSid,
                    userName,
                    domainName);
            }

            return sessions;
        }
        finally
        {
            WTSFreeMemory(sessionInfo);
        }
    }

    /// <summary>仅将 WTS 的用户名、域名字符串信息解码为 UTF-16，遵守返回的缓冲区长度。</summary>
    private static string QueryString(int sessionId, int infoClass)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, sessionId, infoClass, out var buffer, out var bytesReturned))
        {
            return string.Empty;
        }

        try
        {
            return buffer == IntPtr.Zero || bytesReturned < sizeof(char)
                ? string.Empty
                : (Marshal.PtrToStringUni(buffer, bytesReturned / sizeof(char)) ?? string.Empty).TrimEnd('\0');
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    /// <summary>WTS 没有用户 SID 信息类；通过明确的账户名解析 SID，失败时保留空值。</summary>
    private static string ResolveSid(string userName, string domainName)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(domainName))
        {
            // 不能用未限定的用户名解析 SID，同名的本地或域账户可能属于不同用户。
            return string.Empty;
        }

        try
        {
            var account = new NTAccount(domainName, userName);
            return ((SecurityIdentifier)account.Translate(typeof(SecurityIdentifier))).Value;
        }
        catch (SystemException)
        {
            // 域账户离线或无法解析时，上层只允许明确匹配完整账户名的会话。
            return string.Empty;
        }
    }

    /// <summary>映射 WTS_CONNECTSTATE_CLASS，未知值不视为活动会话。</summary>
    private static WindowsSessionState ToSessionState(int state) => state switch
    {
        0 => WindowsSessionState.Active,
        1 => WindowsSessionState.Connected,
        2 => WindowsSessionState.ConnectQuery,
        3 => WindowsSessionState.Shadow,
        4 => WindowsSessionState.Disconnected,
        5 => WindowsSessionState.Idle,
        6 => WindowsSessionState.Listen,
        7 => WindowsSessionState.Reset,
        8 => WindowsSessionState.Down,
        9 => WindowsSessionState.Init,
        _ => WindowsSessionState.Unknown
    };

    /// <summary>WTSEnumerateSessionsW 返回的 WTS_SESSION_INFOW。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct WtsSessionInfo
    {
        /// <summary>会话标识。</summary>
        public int SessionId;

        /// <summary>窗口站名称。</summary>
        [MarshalAs(UnmanagedType.LPWStr)]
        public string WinStationName;

        /// <summary>WTS_CONNECTSTATE_CLASS 数值。</summary>
        public int State;
    }
}
