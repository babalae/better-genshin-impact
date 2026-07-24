using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace BetterGenshinImpact.Service.ChildSession;

internal static class ChildSessionNativeMethods
{
    private const int ErrorNotFound = 1168;
    private const uint NoChildSessionId = uint.MaxValue;
    private static readonly IntPtr CurrentServerHandle = IntPtr.Zero;

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

    internal static void EnableChildSessions()
    {
        if (!WTSEnableChildSessions(true))
        {
            throw CreateLastWin32Exception("无法启用 RDP Child Session");
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
        IntPtr inputWindow = IntPtr.Zero;
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
        if (inputWindow == IntPtr.Zero)
        {
            return false;
        }

        _ = SetFocus(inputWindow);
        return GetFocus() == inputWindow;
    }

    private static Win32Exception CreateLastWin32Exception(string operation)
    {
        var error = Marshal.GetLastPInvokeError();
        return new Win32Exception(error, $"{operation}（Win32 错误 {error}）");
    }

    private delegate bool EnumChildWindowCallback(IntPtr window, IntPtr parameter);
}
