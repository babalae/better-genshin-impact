using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Threading;
using BetterGenshinImpact.Core.Config;
using Microsoft.Xaml.Behaviors;
using Vanara.PInvoke;
using DrawingRectangle = System.Drawing.Rectangle;
using FormsScreen = System.Windows.Forms.Screen;

namespace BetterGenshinImpact.View.Behavior;

public sealed class WindowResizeBehavior : Behavior<FrameworkElement>
{
    private const int MinimumVisibleWidth = 120;
    private const int MinimumVisibleTitleBarHeight = 32;

    public static readonly DependencyProperty TargetWidthProperty =
        DependencyProperty.Register(
            nameof(TargetWidth),
            typeof(double),
            typeof(WindowResizeBehavior),
            new PropertyMetadata(double.NaN));

    public static readonly DependencyProperty AspectRatioProperty =
        DependencyProperty.Register(
            nameof(AspectRatio),
            typeof(double),
            typeof(WindowResizeBehavior),
            new PropertyMetadata(double.NaN));

    public static readonly DependencyProperty ResizeRequestProperty =
        DependencyProperty.Register(
            nameof(ResizeRequest),
            typeof(int),
            typeof(WindowResizeBehavior),
            new PropertyMetadata(0, OnResizeRequestChanged));

    public static readonly DependencyProperty IsSmallWindowModeProperty =
        DependencyProperty.Register(
            nameof(IsSmallWindowMode),
            typeof(bool),
            typeof(WindowResizeBehavior),
            new PropertyMetadata(false));

    public static readonly DependencyProperty NormalWindowPositionProperty =
        DependencyProperty.Register(
            nameof(NormalWindowPosition),
            typeof(WindowPositionConfig),
            typeof(WindowResizeBehavior));

    public static readonly DependencyProperty SmallWindowPositionProperty =
        DependencyProperty.Register(
            nameof(SmallWindowPosition),
            typeof(WindowPositionConfig),
            typeof(WindowResizeBehavior));

    private Window? _window;
    private DispatcherTimer? _savePositionTimer;
    private bool _activeSmallWindowMode;
    private bool _isApplyingPlacement;

    public double TargetWidth
    {
        get => (double)GetValue(TargetWidthProperty);
        set => SetValue(TargetWidthProperty, value);
    }

    public double AspectRatio
    {
        get => (double)GetValue(AspectRatioProperty);
        set => SetValue(AspectRatioProperty, value);
    }

    public int ResizeRequest
    {
        get => (int)GetValue(ResizeRequestProperty);
        set => SetValue(ResizeRequestProperty, value);
    }

    public bool IsSmallWindowMode
    {
        get => (bool)GetValue(IsSmallWindowModeProperty);
        set => SetValue(IsSmallWindowModeProperty, value);
    }

    public WindowPositionConfig? NormalWindowPosition
    {
        get => (WindowPositionConfig?)GetValue(NormalWindowPositionProperty);
        set => SetValue(NormalWindowPositionProperty, value);
    }

    public WindowPositionConfig? SmallWindowPosition
    {
        get => (WindowPositionConfig?)GetValue(SmallWindowPositionProperty);
        set => SetValue(SmallWindowPositionProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.Loaded += OnAssociatedObjectLoaded;
        AssociatedObject.Unloaded += OnAssociatedObjectUnloaded;

        if (AssociatedObject.IsLoaded)
        {
            AttachWindow();
        }
    }

    protected override void OnDetaching()
    {
        AssociatedObject.Loaded -= OnAssociatedObjectLoaded;
        AssociatedObject.Unloaded -= OnAssociatedObjectUnloaded;
        DetachWindow();
        base.OnDetaching();
    }

    private static void OnResizeRequestChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (Equals(e.OldValue, e.NewValue))
        {
            return;
        }

        ((WindowResizeBehavior)dependencyObject).ResizeWindow();
    }

    private void OnAssociatedObjectLoaded(object sender, RoutedEventArgs e)
    {
        AttachWindow();
    }

    private void OnAssociatedObjectUnloaded(object sender, RoutedEventArgs e)
    {
        DetachWindow();
    }

    private void AttachWindow()
    {
        if (_window is not null)
        {
            return;
        }

        _window = Window.GetWindow(AssociatedObject);
        if (_window is null)
        {
            return;
        }

        _activeSmallWindowMode = IsSmallWindowMode;
        _window.LocationChanged += OnWindowLocationChanged;
        _window.IsVisibleChanged += OnWindowIsVisibleChanged;
        _window.Closing += OnWindowClosing;
        _savePositionTimer = new DispatcherTimer(DispatcherPriority.Background, _window.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _savePositionTimer.Tick += OnSavePositionTimerTick;
        RestoreWindowPosition(_activeSmallWindowMode);
    }

    private void DetachWindow()
    {
        SaveActiveWindowPosition();
        if (_savePositionTimer is not null)
        {
            _savePositionTimer.Stop();
            _savePositionTimer.Tick -= OnSavePositionTimerTick;
            _savePositionTimer = null;
        }

        if (_window is not null)
        {
            _window.LocationChanged -= OnWindowLocationChanged;
            _window.IsVisibleChanged -= OnWindowIsVisibleChanged;
            _window.Closing -= OnWindowClosing;
        }

        _isApplyingPlacement = false;
        _window = null;
    }

    private void ResizeWindow()
    {
        AttachWindow();
        if (_window is null)
        {
            return;
        }

        if (_window.WindowState != WindowState.Normal)
        {
            _window.WindowState = WindowState.Normal;
        }

        _savePositionTimer?.Stop();
        SaveWindowPosition(_activeSmallWindowMode);
        _activeSmallWindowMode = IsSmallWindowMode;
        _isApplyingPlacement = true;

        _ = _window.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(ResizeWindowCore));
    }

    private void ResizeWindowCore()
    {
        if (_window is null)
        {
            return;
        }

        if (double.IsFinite(TargetWidth) &&
            TargetWidth > 0 &&
            double.IsFinite(AspectRatio) &&
            AspectRatio > 0 &&
            AssociatedObject.ActualWidth > 0 &&
            AssociatedObject.ActualHeight > 0)
        {
            var nonContentWidth = Math.Max(0, _window.ActualWidth - AssociatedObject.ActualWidth);
            var nonContentHeight = Math.Max(0, _window.ActualHeight - AssociatedObject.ActualHeight);
            var targetWindowWidth = Math.Max(_window.MinWidth, TargetWidth);
            if (double.IsFinite(_window.MaxWidth))
            {
                targetWindowWidth = Math.Min(targetWindowWidth, _window.MaxWidth);
            }

            var targetContentWidth = Math.Max(1, targetWindowWidth - nonContentWidth);
            var targetWindowHeight = Math.Max(
                _window.MinHeight,
                targetContentWidth / AspectRatio + nonContentHeight);
            if (double.IsFinite(_window.MaxHeight))
            {
                targetWindowHeight = Math.Min(targetWindowHeight, _window.MaxHeight);
            }

            _window.Width = targetWindowWidth;
            _window.Height = targetWindowHeight;
        }

        _ = _window.Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(CompleteResize));
    }

    private void CompleteResize()
    {
        try
        {
            _window?.UpdateLayout();
            RestoreWindowPosition(_activeSmallWindowMode);
        }
        finally
        {
            _isApplyingPlacement = false;
        }
    }

    private void OnWindowLocationChanged(object? sender, EventArgs e)
    {
        if (_isApplyingPlacement || _window?.WindowState != WindowState.Normal || _savePositionTimer is null)
        {
            return;
        }

        _savePositionTimer.Stop();
        _savePositionTimer.Start();
    }

    private void OnWindowIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is false)
        {
            SaveActiveWindowPosition();
        }
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        SaveActiveWindowPosition();
    }

    private void OnSavePositionTimerTick(object? sender, EventArgs e)
    {
        _savePositionTimer?.Stop();
        SaveActiveWindowPosition();
    }

    private void SaveActiveWindowPosition()
    {
        _savePositionTimer?.Stop();
        SaveWindowPosition(_activeSmallWindowMode);
    }

    private void SaveWindowPosition(bool isSmallWindowMode)
    {
        if (_isApplyingPlacement ||
            _window?.WindowState != WindowState.Normal ||
            !TryGetWindowBounds(out var bounds))
        {
            return;
        }

        UpdateStoredPosition(isSmallWindowMode, bounds.Left, bounds.Top);
    }

    private void RestoreWindowPosition(bool isSmallWindowMode)
    {
        if (!TryGetWindowBounds(out var windowBounds))
        {
            return;
        }

        var storedPosition = isSmallWindowMode ? SmallWindowPosition : NormalWindowPosition;
        var desiredLeft = storedPosition?.Left ?? windowBounds.Left;
        var desiredTop = storedPosition?.Top ?? windowBounds.Top;
        var correctedPosition = EnsureVisiblePosition(
            desiredLeft,
            desiredTop,
            windowBounds.Width,
            windowBounds.Height);

        var handle = new WindowInteropHelper(_window!).Handle;
        if (handle != 0 &&
            (windowBounds.Left != correctedPosition.Left || windowBounds.Top != correctedPosition.Top))
        {
            var flags = User32.SetWindowPosFlags.SWP_NOSIZE |
                        User32.SetWindowPosFlags.SWP_NOZORDER |
                        User32.SetWindowPosFlags.SWP_NOACTIVATE |
                        User32.SetWindowPosFlags.SWP_NOOWNERZORDER;
            User32.SetWindowPos(
                handle,
                default,
                correctedPosition.Left,
                correctedPosition.Top,
                0,
                0,
                flags);
        }

        if (storedPosition is not null)
        {
            UpdateStoredPosition(isSmallWindowMode, correctedPosition.Left, correctedPosition.Top);
        }
    }

    private bool TryGetWindowBounds(out DrawingRectangle bounds)
    {
        bounds = DrawingRectangle.Empty;
        if (_window is null)
        {
            return false;
        }

        var handle = new WindowInteropHelper(_window).Handle;
        if (handle == 0 || !User32.GetWindowRect(handle, out var rect))
        {
            return false;
        }

        bounds = DrawingRectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    private void UpdateStoredPosition(bool isSmallWindowMode, int left, int top)
    {
        var currentPosition = isSmallWindowMode ? SmallWindowPosition : NormalWindowPosition;
        if (currentPosition?.Left == left && currentPosition.Top == top)
        {
            return;
        }

        var newPosition = new WindowPositionConfig(left, top);
        var positionProperty = isSmallWindowMode
            ? SmallWindowPositionProperty
            : NormalWindowPositionProperty;
        SetCurrentValue(positionProperty, newPosition);
        BindingOperations.GetBindingExpression(this, positionProperty)?.UpdateSource();
    }

    private static WindowPositionConfig EnsureVisiblePosition(
        int left,
        int top,
        int width,
        int height)
    {
        var screens = FormsScreen.AllScreens;
        if (screens.Length == 0)
        {
            return new WindowPositionConfig(left, top);
        }

        var visibleTitleBarWidth = Math.Min(width, MinimumVisibleWidth);
        var visibleTitleBarHeight = Math.Min(height, MinimumVisibleTitleBarHeight);
        var titleBarBounds = new DrawingRectangle(left, top, width, visibleTitleBarHeight);
        if (screens.Any(screen =>
                DrawingRectangle.Intersect(titleBarBounds, screen.WorkingArea) is var intersection &&
                intersection.Width >= visibleTitleBarWidth &&
                intersection.Height >= visibleTitleBarHeight))
        {
            return new WindowPositionConfig(left, top);
        }

        var targetWorkArea = screens
            .Select(screen => screen.WorkingArea)
            .OrderBy(workArea => DistanceSquaredToRectangle(left, top, workArea))
            .First();
        var correctedLeft = width <= targetWorkArea.Width
            ? Math.Clamp(left, targetWorkArea.Left, targetWorkArea.Right - width)
            : targetWorkArea.Left;
        var correctedTop = height <= targetWorkArea.Height
            ? Math.Clamp(top, targetWorkArea.Top, targetWorkArea.Bottom - height)
            : targetWorkArea.Top;
        return new WindowPositionConfig(correctedLeft, correctedTop);
    }

    private static double DistanceSquaredToRectangle(int x, int y, DrawingRectangle rectangle)
    {
        var deltaX = x < rectangle.Left
            ? (double)rectangle.Left - x
            : x > rectangle.Right
                ? (double)x - rectangle.Right
                : 0;
        var deltaY = y < rectangle.Top
            ? (double)rectangle.Top - y
            : y > rectangle.Bottom
                ? (double)y - rectangle.Bottom
                : 0;

        return deltaX * deltaX + deltaY * deltaY;
    }
}
