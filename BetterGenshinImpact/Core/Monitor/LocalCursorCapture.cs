using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;

namespace BetterGenshinImpact.Core.Monitor;

/// <summary>
/// 在本地会话中约束并隐藏鼠标。所有调用应来自同一个 UI 线程，
/// 以便成对恢复 ShowCursor 的显示计数。
/// </summary>
internal sealed class LocalCursorCapture(ILogger logger) : IDisposable
{
    private const int MaxCursorHideAttempts = 64;
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    private NativeRect _previousClipRect;
    private Rectangle _appliedBounds;
    private bool _hasPreviousClipRect;
    private bool _isCaptureSessionActive;
    private bool _isRestrictionApplied;
    private bool _isDisposed;
    private int _cursorHideCallCount;

    internal void Capture(Rectangle bounds)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bounds),
                bounds,
                "鼠标限制区域必须具有有效宽度和高度。");
        }

        if (!_isCaptureSessionActive)
        {
            var clipRectRead = NativeMethods.GetClipCursor(out _previousClipRect);
            if (!clipRectRead)
            {
                logger.LogWarning(
                    "读取现有鼠标限制区域失败，Win32Error: {Win32Error}",
                    Marshal.GetLastWin32Error());
            }

            _hasPreviousClipRect =
                clipRectRead && !IsVirtualDesktopBounds(_previousClipRect);
            _isCaptureSessionActive = true;
        }

        if (_isRestrictionApplied && _appliedBounds == bounds)
        {
            return;
        }

        var clipRect = NativeRect.FromRectangle(bounds);
        if (!NativeMethods.ClipCursor(ref clipRect))
        {
            var exception = new Win32Exception(
                Marshal.GetLastWin32Error(),
                "限制本地鼠标到桌面分身窗口失败");
            if (!_isRestrictionApplied)
            {
                Release();
            }
            throw exception;
        }

        _appliedBounds = bounds;
        if (!_isRestrictionApplied)
        {
            HideCursor();
            _isRestrictionApplied = true;
        }
    }

    internal void ReleaseTemporarily()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (!_isCaptureSessionActive || !_isRestrictionApplied)
        {
            return;
        }

        if (!NativeMethods.ReleaseClipCursor(IntPtr.Zero))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "临时解除本地鼠标限制失败");
        }

        var centerX = _appliedBounds.Left + _appliedBounds.Width / 2;
        var centerY = _appliedBounds.Top + _appliedBounds.Height / 2;
        if (!NativeMethods.SetCursorPos(centerX, centerY))
        {
            logger.LogWarning(
                "将本地鼠标移动到桌面分身控件中心失败，Win32Error: {Win32Error}",
                Marshal.GetLastWin32Error());
        }

        RestoreCursorVisibility();
        _isRestrictionApplied = false;
        _appliedBounds = Rectangle.Empty;
    }

    internal void Release()
    {
        if (!_isCaptureSessionActive)
        {
            return;
        }

        var restored = _hasPreviousClipRect
            ? NativeMethods.ClipCursor(ref _previousClipRect)
            : NativeMethods.ReleaseClipCursor(IntPtr.Zero);
        if (!restored)
        {
            logger.LogWarning(
                "恢复本地鼠标限制区域失败，Win32Error: {Win32Error}",
                Marshal.GetLastWin32Error());
        }

        RestoreCursorVisibility();
        _hasPreviousClipRect = false;
        _isCaptureSessionActive = false;
        _isRestrictionApplied = false;
        _appliedBounds = Rectangle.Empty;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        Release();
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }

    private void HideCursor()
    {
        var displayCount = 0;
        do
        {
            displayCount = NativeMethods.ShowCursor(false);
            _cursorHideCallCount++;
        }
        while (displayCount >= 0 && _cursorHideCallCount < MaxCursorHideAttempts);

        if (displayCount >= 0)
        {
            logger.LogWarning(
                "隐藏本地鼠标时 ShowCursor 显示计数仍为 {DisplayCount}",
                displayCount);
        }
    }

    private void RestoreCursorVisibility()
    {
        while (_cursorHideCallCount > 0)
        {
            _ = NativeMethods.ShowCursor(true);
            _cursorHideCallCount--;
        }
    }

    private static bool IsVirtualDesktopBounds(NativeRect rect)
    {
        var left = NativeMethods.GetSystemMetrics(SmXVirtualScreen);
        var top = NativeMethods.GetSystemMetrics(SmYVirtualScreen);
        var width = NativeMethods.GetSystemMetrics(SmCxVirtualScreen);
        var height = NativeMethods.GetSystemMetrics(SmCyVirtualScreen);
        return rect.Left == left
               && rect.Top == top
               && rect.Right == left + width
               && rect.Bottom == top + height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;

        public int Top;

        public int Right;

        public int Bottom;

        internal static NativeRect FromRectangle(Rectangle rectangle)
        {
            return new NativeRect
            {
                Left = rectangle.Left,
                Top = rectangle.Top,
                Right = rectangle.Right,
                Bottom = rectangle.Bottom
            };
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetClipCursor(out NativeRect rect);

        [DllImport("user32.dll", EntryPoint = "ClipCursor", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ClipCursor(ref NativeRect rect);

        [DllImport("user32.dll", EntryPoint = "ClipCursor", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ReleaseClipCursor(nint rect);

        [DllImport("user32.dll")]
        internal static extern int ShowCursor(
            [MarshalAs(UnmanagedType.Bool)] bool show);

        [DllImport("user32.dll")]
        internal static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetCursorPos(int x, int y);
    }
}
