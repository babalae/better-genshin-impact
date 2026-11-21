using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace BetterGenshinImpact.Service.ChildSession;

internal static class ChildSessionNativeMethods
{
    private const int DefaultRdpPort = 3389;
    private const int ErrorNotFound = 1168;
    private const string RdpTcpRegistryPath =
        @"SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp";
    private const string TermServiceParametersRegistryPath =
        @"SYSTEM\CurrentControlSet\Services\TermService\Parameters";
    private const string RdpWrapperLibraryName = "rdpwrap.dll";
    private const uint NoChildSessionId = uint.MaxValue;

    /// <summary>WTS_INFO_CLASS.WTSConnectState。</summary>
    private const int WtsConnectState = 8;

    /// <summary>WTS_CONNECTSTATE_CLASS.WTSActive。</summary>
    private const int WtsActive = 0;
    /// <summary>WTS_INFO_CLASS.WTSUserName：会话当前登录的用户名。</summary>
    private const int WtsUserName = 5;

    private static readonly IntPtr CurrentServerHandle = IntPtr.Zero;
    private static readonly ConcurrentDictionary<IntPtr, IntPtr> RdpInputWindows = new();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSEnableChildSessions(
        [MarshalAs(UnmanagedType.Bool)] bool enable);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSIsChildSessionsEnabled(
        [MarshalAs(UnmanagedType.Bool)] out bool enabled);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSGetChildSessionId(out uint sessionId);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSLogoffSession(
        IntPtr serverHandle,
        uint sessionId,
        [MarshalAs(UnmanagedType.Bool)] bool wait);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformationW(
        IntPtr serverHandle,
        uint sessionId,
        int wtsInfoClass,
        out IntPtr buffer,
        out int bytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(
        IntPtr parentWindow,
        EnumChildWindowCallback callback,
        IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(
        IntPtr window,
        StringBuilder text,
        int maximumLength);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsChild(IntPtr parentWindow, IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnableWindow(
        IntPtr window,
        [MarshalAs(UnmanagedType.Bool)] bool enabled);

    internal static void EnableChildSessions()
    {
        if (!WTSEnableChildSessions(true))
        {
            throw CreateLastWin32Exception("无法启用 RDP Child Session");
        }
    }

    internal static int GetConfiguredRdpPort()
    {
        try
        {
            using var localMachine = RegistryKey.OpenBaseKey(
                RegistryHive.LocalMachine,
                RegistryView.Registry64);
            using var rdpTcpKey = localMachine.OpenSubKey(RdpTcpRegistryPath);
            var configuredPort = rdpTcpKey?.GetValue("PortNumber");
            return configuredPort is int port and > 0 and <= ushort.MaxValue
                ? port
                : DefaultRdpPort;
        }
        catch (Exception exception) when (exception is SecurityException
                                              or UnauthorizedAccessException
                                              or IOException)
        {
            return DefaultRdpPort;
        }
    }

    internal static bool IsRdpWrapperEnabled()
    {
        try
        {
            using var localMachine = RegistryKey.OpenBaseKey(
                RegistryHive.LocalMachine,
                RegistryView.Registry64);
            using var termServiceParametersKey =
                localMachine.OpenSubKey(TermServiceParametersRegistryPath);
            var serviceLibraryPath =
                termServiceParametersKey?.GetValue("ServiceDll") as string;
            return serviceLibraryPath?.Contains(
                RdpWrapperLibraryName,
                StringComparison.OrdinalIgnoreCase) == true;
        }
        catch (Exception exception) when (exception is SecurityException
                                              or UnauthorizedAccessException
                                              or IOException)
        {
            return false;
        }
    }

    internal static bool IsChildSessionsEnabled()
    {
        if (!WTSIsChildSessionsEnabled(out var enabled))
        {
            throw CreateLastWin32Exception("无法读取 RDP Child Session 状态");
        }

        return enabled;
    }

    internal static uint? TryGetChildSessionId()
    {
        return WTSGetChildSessionId(out var sessionId) && sessionId != NoChildSessionId
            ? sessionId
            : null;
    }

    /// <summary>
    /// 查询子会话号，并区分「查询本身失败」。
    ///
    /// 返回值是查询是否成功；成功时 <paramref name="sessionId"/> 为 null 表示确实没有会话。
    /// 等会话释放时两者不能混为一谈：查询失败就当「没有会话」会立刻重连，
    /// 正好撞上那段等待要避免的「访问被拒绝」。
    /// </summary>
    internal static bool TryQueryChildSessionId(out uint? sessionId)
    {
        sessionId = null;
        if (!WTSGetChildSessionId(out var id))
        {
            return false;
        }

        sessionId = id == NoChildSessionId ? null : id;
        return true;
    }

    /// <summary>
    /// 指定会话是否处于 Active。
    ///
    /// 用来判断「上一次连接在系统里是否真的收尾了」：RDP 控件回到已断开，
    /// 不代表会话已经从 Active 退出来，这时立刻重连会被服务端用「访问被拒绝」顶回来。
    /// 读不到状态时按「不活跃」处理——调用方只是在重连前等一下，等不到还有超时兜底。
    /// </summary>
    /// <summary>
    /// 会话是否既 Active、又已经完成登录（用户名非空）。
    ///
    /// 用于「重新附着已有会话」的判定：UU 强效切换会掐断 RDP，但 Child Session 本身
    /// 还在、用户也没注销。重连后 ActiveX 可能不再发 OnLoginComplete，这时只能靠
    /// 系统状态确认它其实已经可用——只看 Active 不够（登录未完成时也可能 Active，
    /// 例如停在改密界面）。
    /// </summary>
    internal static bool IsSessionLoggedOnAndActive(uint sessionId)
    {
        if (!IsSessionActive(sessionId))
        {
            return false;
        }

        var buffer = IntPtr.Zero;

        try
        {
            if (!WTSQuerySessionInformationW(
                    CurrentServerHandle,
                    sessionId,
                    WtsUserName,
                    out buffer,
                    out var bytesReturned)
                || buffer == IntPtr.Zero)
            {
                return false;
            }

            // 空串也要占 2 字节（结尾的空字符），所以「大于一字符」才算真的有用户名。
            return bytesReturned > sizeof(char)
                   && !string.IsNullOrWhiteSpace(Marshal.PtrToStringUni(buffer));
        }
        catch (Exception exception) when (exception is DllNotFoundException
                                              or EntryPointNotFoundException)
        {
            return false;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                try
                {
                    WTSFreeMemory(buffer);
                }
                catch (DllNotFoundException)
                {
                }
            }
        }
    }

    /// <summary>
    /// 查询会话是否 Active，并区分「查询失败」。
    ///
    /// 返回值是「查询本身是否成功」：成功时 <paramref name="active"/> 给出真实状态。
    /// 调用方（等会话释放）必须把失败当成「还不知道」，继续有界轮询；
    /// 当成「已释放」会立刻重连，正好撞上那段等待想避免的「访问被拒绝」。
    /// </summary>
    internal static bool TryQuerySessionActive(uint sessionId, out bool active)
    {
        active = false;
        var buffer = IntPtr.Zero;

        try
        {
            if (!WTSQuerySessionInformationW(
                    CurrentServerHandle,
                    sessionId,
                    WtsConnectState,
                    out buffer,
                    out var bytesReturned)
                || buffer == IntPtr.Zero)
            {
                return false;
            }

            active = bytesReturned >= sizeof(int) && Marshal.ReadInt32(buffer) == WtsActive;
            return true;
        }
        catch (Exception exception) when (exception is DllNotFoundException
                                              or EntryPointNotFoundException)
        {
            return false;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                try
                {
                    WTSFreeMemory(buffer);
                }
                catch (DllNotFoundException)
                {
                }
            }
        }
    }

    internal static bool IsSessionActive(uint sessionId)
    {
        var buffer = IntPtr.Zero;

        try
        {
            if (!WTSQuerySessionInformationW(
                    CurrentServerHandle,
                    sessionId,
                    WtsConnectState,
                    out buffer,
                    out var bytesReturned)
                || buffer == IntPtr.Zero)
            {
                return false;
            }

            return bytesReturned >= sizeof(int) && Marshal.ReadInt32(buffer) == WtsActive;
        }
        catch (Exception exception) when (exception is DllNotFoundException
                                              or EntryPointNotFoundException)
        {
            return false;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                try
                {
                    WTSFreeMemory(buffer);
                }
                catch (DllNotFoundException)
                {
                }
            }
        }
    }

    internal static uint? TerminateChildSession()
    {
        return TerminateChildSession(wait: true);
    }

    internal static uint? TerminateChildSession(bool wait)
    {
        if (!WTSGetChildSessionId(out var childSessionId))
        {
            var error = Marshal.GetLastPInvokeError();
            if (error == ErrorNotFound)
            {
                return null;
            }

            throw new Win32Exception(
                error,
                $"无法取得 RDP Child Session ID（Win32 错误 {error}）");
        }

        if (childSessionId == NoChildSessionId)
        {
            return null;
        }

        if (!WTSLogoffSession(CurrentServerHandle, childSessionId, wait))
        {
            throw CreateLastWin32Exception($"无法注销 Child Session {childSessionId}");
        }

        return childSessionId;
    }

    internal static bool TryFocusRdpInputWindow(IntPtr rdpHostWindow)
    {
        var inputWindow = FindRdpInputWindow(rdpHostWindow);
        if (inputWindow == IntPtr.Zero)
        {
            return false;
        }

        _ = SetFocus(inputWindow);
        return GetFocus() == inputWindow;
    }

    internal static bool IsRdpInputWindowFocused(IntPtr rdpHostWindow)
    {
        var inputWindow = FindRdpInputWindow(rdpHostWindow);
        return inputWindow != IntPtr.Zero && GetFocus() == inputWindow;
    }

    /// <summary>
    /// 禁用或恢复 RDP 的输入窗口。禁用后本机鼠标与键盘都不会进入桌面分身，
    /// 画面渲染不受影响。输入窗口尚未创建时不做任何处理。
    /// </summary>
    internal static void SetRdpInputWindowEnabled(IntPtr rdpHostWindow, bool enabled)
    {
        var inputWindow = FindRdpInputWindow(rdpHostWindow);
        if (inputWindow == IntPtr.Zero)
        {
            ChildSessionDiagnosticsLog.Instance.LogWarning(
                "没找到 RDP 的输入窗口，本次{Action}没有生效。",
                enabled ? "恢复输入" : "屏蔽输入");
            return;
        }

        _ = EnableWindow(inputWindow, enabled);
    }

    internal static void ClearRdpInputWindowCache(IntPtr rdpHostWindow)
    {
        RdpInputWindows.TryRemove(rdpHostWindow, out _);
    }

    private static IntPtr FindRdpInputWindow(IntPtr rdpHostWindow)
    {
        if (rdpHostWindow == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        if (RdpInputWindows.TryGetValue(rdpHostWindow, out var cachedInputWindow))
        {
            if (IsWindow(cachedInputWindow) && IsChild(rdpHostWindow, cachedInputWindow))
            {
                return cachedInputWindow;
            }

            RdpInputWindows.TryRemove(rdpHostWindow, out _);
        }

        var inputWindow = IntPtr.Zero;
        EnumChildWindowCallback callback = (window, _) =>
        {
            const int windowTextCapacity = 256;
            var windowText = new StringBuilder(windowTextCapacity);
            _ = GetWindowText(window, windowText, windowText.Capacity);
            if (!string.Equals(
                    windowText.ToString(),
                    "Input Capture Window",
                    StringComparison.Ordinal))
            {
                return true;
            }

            inputWindow = window;
            return false;
        };

        _ = EnumChildWindows(rdpHostWindow, callback, IntPtr.Zero);
        if (inputWindow != IntPtr.Zero)
        {
            RdpInputWindows[rdpHostWindow] = inputWindow;
        }
        return inputWindow;
    }

    private static Win32Exception CreateLastWin32Exception(string operation)
    {
        var error = Marshal.GetLastPInvokeError();
        return new Win32Exception(error, $"{operation}（Win32 错误 {error}）");
    }

    private delegate bool EnumChildWindowCallback(IntPtr window, IntPtr parameter);
}
