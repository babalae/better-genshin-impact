using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Threading;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Helpers.Ui;

public sealed class PopupTopmostFixer : IDisposable
{
    private const int WmWindowPosChanging = 0x0046;
    private const uint SwpNoZOrder = 0x0004u;

    private readonly Dispatcher _dispatcher;
    private static readonly nint _insertAfter = -1;

    private readonly HashSet<Popup> _trackedPopups = new();
    private readonly Dictionary<FrameworkElement, Popup> _childToPopup = new();
    private readonly Dictionary<Popup, HwndSource> _popupHwndSources = new();
    private readonly Dictionary<nint, HwndSourceHook> _popupHwndHooks = new();

    private DispatcherTimer? _popupTopmostTimer;
    private bool _isDisposed;

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos
    {
        public nint Hwnd;
        public nint HwndInsertAfter;
        public int X;
        public int Y;
        public int Cx;
        public int Cy;
        public uint Flags;
    }

    public PopupTopmostFixer(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void Attach(Popup popup)
    {
        ThrowIfDisposed();

        popup.Opened -= PopupOnOpened;
        popup.Closed -= PopupOnClosed;
        popup.Opened += PopupOnOpened;
        popup.Closed += PopupOnClosed;
    }

    public void Detach(Popup popup)
    {
        if (_isDisposed)
        {
            return;
        }

        popup.Opened -= PopupOnOpened;
        popup.Closed -= PopupOnClosed;

        if (popup.IsOpen)
        {
            PopupOnClosed(popup, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        foreach (var popup in _trackedPopups)
        {
            popup.Opened -= PopupOnOpened;
            popup.Closed -= PopupOnClosed;
        }

        foreach (var hwndSource in _popupHwndSources.Values)
        {
            if (_popupHwndHooks.TryGetValue(hwndSource.Handle, out var hook))
            {
                hwndSource.RemoveHook(hook);
            }
        }

        _trackedPopups.Clear();
        _childToPopup.Clear();
        _popupHwndSources.Clear();
        _popupHwndHooks.Clear();

        StopPopupTopmostTimer();
    }

    private void PopupOnOpened(object? sender, EventArgs e)
    {
        if (_isDisposed || sender is not Popup popup)
        {
            return;
        }

        _trackedPopups.Add(popup);
        StartPopupTopmostTimer();

        _dispatcher.BeginInvoke(() =>
        {
            EnsurePopupTopmost(popup);
            TryAttachPopupHwndHook(popup);
        }, DispatcherPriority.Loaded);

        if (popup.Child is not FrameworkElement child)
        {
            return;
        }

        _childToPopup[child] = popup;

        child.LostKeyboardFocus -= PopupChildOnLostKeyboardFocus;
        child.LostFocus -= PopupChildOnLostFocus;
        child.LostKeyboardFocus += PopupChildOnLostKeyboardFocus;
        child.LostFocus += PopupChildOnLostFocus;
    }

    private void PopupOnClosed(object? sender, EventArgs e)
    {
        if (_isDisposed || sender is not Popup popup)
        {
            return;
        }

        _trackedPopups.Remove(popup);
        TryDetachPopupHwndHook(popup);

        if (popup.Child is FrameworkElement child)
        {
            _childToPopup.Remove(child);
            child.LostKeyboardFocus -= PopupChildOnLostKeyboardFocus;
            child.LostFocus -= PopupChildOnLostFocus;
        }

        if (_trackedPopups.Count == 0)
        {
            StopPopupTopmostTimer();
        }
    }

    private void PopupChildOnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_isDisposed || sender is not FrameworkElement child)
        {
            return;
        }

        _dispatcher.BeginInvoke(() =>
        {
            if (_childToPopup.TryGetValue(child, out var popup))
            {
                EnsurePopupTopmost(popup);
            }
        }, DispatcherPriority.Background);
    }

    private void PopupChildOnLostFocus(object sender, RoutedEventArgs e)
    {
        if (_isDisposed || sender is not FrameworkElement child)
        {
            return;
        }

        _dispatcher.BeginInvoke(() =>
        {
            if (_childToPopup.TryGetValue(child, out var popup))
            {
                EnsurePopupTopmost(popup);
            }
        }, DispatcherPriority.Background);
    }

    private void StartPopupTopmostTimer()
    {
        if (_popupTopmostTimer != null)
        {
            return;
        }

        _popupTopmostTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _popupTopmostTimer.Tick += PopupTopmostTimerOnTick;
        _popupTopmostTimer.Start();
    }

    private void StopPopupTopmostTimer()
    {
        if (_popupTopmostTimer == null)
        {
            return;
        }

        _popupTopmostTimer.Stop();
        _popupTopmostTimer.Tick -= PopupTopmostTimerOnTick;
        _popupTopmostTimer = null;
    }

    private void PopupTopmostTimerOnTick(object? sender, EventArgs e)
    {
        if (_isDisposed || _trackedPopups.Count == 0)
        {
            StopPopupTopmostTimer();
            return;
        }

        foreach (var popup in _trackedPopups)
        {
            EnsurePopupTopmost(popup);
        }
    }

    private void TryAttachPopupHwndHook(Popup popup)
    {
        if (_isDisposed || !popup.IsOpen || popup.Child == null)
        {
            return;
        }

        if (PresentationSource.FromVisual(popup.Child) is not HwndSource hwndSource)
        {
            return;
        }

        if (_popupHwndHooks.ContainsKey(hwndSource.Handle))
        {
            _popupHwndSources[popup] = hwndSource;
            return;
        }

        HwndSourceHook hook = PopupWindowHwndHook;
        hwndSource.AddHook(hook);
        _popupHwndHooks[hwndSource.Handle] = hook;
        _popupHwndSources[popup] = hwndSource;
    }

    private void TryDetachPopupHwndHook(Popup popup)
    {
        if (_isDisposed)
        {
            return;
        }

        if (!_popupHwndSources.TryGetValue(popup, out var hwndSource))
        {
            return;
        }

        if (_popupHwndHooks.TryGetValue(hwndSource.Handle, out var hook))
        {
            hwndSource.RemoveHook(hook);
            _popupHwndHooks.Remove(hwndSource.Handle);
        }

        _popupHwndSources.Remove(popup);
    }

    private nint PopupWindowHwndHook(nint hWnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmWindowPosChanging && lParam != 0)
        {
            var pos = Marshal.PtrToStructure<WindowPos>(lParam);
            pos.HwndInsertAfter = _insertAfter;
            pos.Flags &= ~SwpNoZOrder;
            Marshal.StructureToPtr(pos, lParam, true);
        }

        return default;
    }

    private void EnsurePopupTopmost(Popup popup)
    {
        if (_isDisposed || !popup.IsOpen || popup.Child == null)
        {
            return;
        }

        if (PresentationSource.FromVisual(popup.Child) is not HwndSource hwndSource)
        {
            return;
        }

        var insertAfter = (IntPtr)_insertAfter;
        var flags =
            User32.SetWindowPosFlags.SWP_NOMOVE
            | User32.SetWindowPosFlags.SWP_NOSIZE
            | User32.SetWindowPosFlags.SWP_NOACTIVATE
            | User32.SetWindowPosFlags.SWP_NOOWNERZORDER
            | User32.SetWindowPosFlags.SWP_SHOWWINDOW;

        _ = User32.SetWindowPos(hwndSource.Handle, insertAfter, 0, 0, 0, 0, flags);
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(PopupTopmostFixer));
        }
    }
}
