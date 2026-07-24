using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Xaml.Behaviors;
using Vanara.PInvoke;

namespace BetterGenshinImpact.View.Behavior;

public sealed class WindowAspectRatioBehavior : Behavior<FrameworkElement>
{
    private const int WmSizing = 0x0214;
    private const int WmszLeft = 1;
    private const int WmszRight = 2;
    private const int WmszTop = 3;
    private const int WmszTopLeft = 4;
    private const int WmszTopRight = 5;
    private const int WmszBottom = 6;
    private const int WmszBottomLeft = 7;
    private const int WmszBottomRight = 8;

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.Register(
            nameof(IsEnabled),
            typeof(bool),
            typeof(WindowAspectRatioBehavior),
            new PropertyMetadata(false));

    public static readonly DependencyProperty AspectRatioProperty =
        DependencyProperty.Register(
            nameof(AspectRatio),
            typeof(double),
            typeof(WindowAspectRatioBehavior),
            new PropertyMetadata(16d / 9d));

    private Window? _window;
    private HwndSource? _source;

    public bool IsEnabled
    {
        get => (bool)GetValue(IsEnabledProperty);
        set => SetValue(IsEnabledProperty, value);
    }

    public double AspectRatio
    {
        get => (double)GetValue(AspectRatioProperty);
        set => SetValue(AspectRatioProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.Loaded += OnAssociatedObjectLoaded;
        AssociatedObject.Unloaded += OnAssociatedObjectUnloaded;

        if (AssociatedObject.IsLoaded)
        {
            AttachWindowHook();
        }
    }

    protected override void OnDetaching()
    {
        AssociatedObject.Loaded -= OnAssociatedObjectLoaded;
        AssociatedObject.Unloaded -= OnAssociatedObjectUnloaded;
        DetachWindowHook();
        base.OnDetaching();
    }

    private void OnAssociatedObjectLoaded(object sender, RoutedEventArgs e)
    {
        AttachWindowHook();
    }

    private void OnAssociatedObjectUnloaded(object sender, RoutedEventArgs e)
    {
        DetachWindowHook();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        DetachWindowHook();
    }

    private void AttachWindowHook()
    {
        if (_source is not null)
        {
            return;
        }

        _window = Window.GetWindow(AssociatedObject);
        _source = _window is null
            ? null
            : PresentationSource.FromVisual(_window) as HwndSource;
        if (_window is null || _source is null)
        {
            return;
        }

        _window.Closed += OnWindowClosed;
        _source.AddHook(WindowProc);
    }

    private void DetachWindowHook()
    {
        _source?.RemoveHook(WindowProc);
        _source = null;

        if (_window is not null)
        {
            _window.Closed -= OnWindowClosed;
            _window = null;
        }
    }

    private nint WindowProc(
        nint hWnd,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (message != WmSizing ||
            !IsEnabled ||
            _window?.WindowState != WindowState.Normal ||
            lParam == 0 ||
            !double.IsFinite(AspectRatio) ||
            AspectRatio <= 0 ||
            !TryGetSizingMetrics(hWnd, out var metrics))
        {
            return 0;
        }

        var sizingEdge = unchecked((int)wParam.ToInt64());
        var rect = Marshal.PtrToStructure<NativeRect>(lParam);
        if (!ConstrainRect(ref rect, sizingEdge, metrics))
        {
            return 0;
        }

        Marshal.StructureToPtr(rect, lParam, false);
        handled = true;
        return 1;
    }

    private bool TryGetSizingMetrics(nint hWnd, out SizingMetrics metrics)
    {
        metrics = default;
        if (_window is null ||
            _source?.CompositionTarget is null ||
            AssociatedObject.ActualWidth <= 0 ||
            AssociatedObject.ActualHeight <= 0 ||
            !User32.GetWindowRect(hWnd, out var windowRect))
        {
            return false;
        }

        var contentTopLeft = AssociatedObject.PointToScreen(new Point(0, 0));
        var contentBottomRight = AssociatedObject.PointToScreen(
            new Point(AssociatedObject.ActualWidth, AssociatedObject.ActualHeight));
        var contentWidth = Math.Abs(contentBottomRight.X - contentTopLeft.X);
        var contentHeight = Math.Abs(contentBottomRight.Y - contentTopLeft.Y);
        if (contentWidth <= 0 || contentHeight <= 0)
        {
            return false;
        }

        var frameWidth = Math.Max(0, windowRect.Right - windowRect.Left - contentWidth);
        var frameHeight = Math.Max(0, windowRect.Bottom - windowRect.Top - contentHeight);
        var transformToDevice = _source.CompositionTarget.TransformToDevice;
        var minimumOuterWidth = _window.MinWidth * transformToDevice.M11;
        var minimumOuterHeight = _window.MinHeight * transformToDevice.M22;
        var minimumContentWidth = Math.Max(
            1,
            Math.Max(
                minimumOuterWidth - frameWidth,
                (minimumOuterHeight - frameHeight) * AspectRatio));

        metrics = new SizingMetrics(
            frameWidth,
            frameHeight,
            minimumContentWidth,
            minimumContentWidth / AspectRatio);
        return true;
    }

    private bool ConstrainRect(
        ref NativeRect rect,
        int sizingEdge,
        SizingMetrics metrics)
    {
        if (sizingEdge is < WmszLeft or > WmszBottomRight)
        {
            return false;
        }

        var requestedWidth = Math.Max(1, rect.Right - rect.Left);
        var requestedHeight = Math.Max(1, rect.Bottom - rect.Top);
        var widthDrivenSize = CalculateWidthDrivenSize(requestedWidth, metrics);
        var heightDrivenSize = CalculateHeightDrivenSize(requestedHeight, metrics);
        var useWidthAsDriver = sizingEdge is WmszLeft or WmszRight ||
                               sizingEdge is WmszTopLeft or WmszTopRight or WmszBottomLeft or WmszBottomRight &&
                               Math.Abs(widthDrivenSize.Height - requestedHeight) <=
                               Math.Abs(heightDrivenSize.Width - requestedWidth) / AspectRatio;
        var targetSize = useWidthAsDriver ? widthDrivenSize : heightDrivenSize;
        var targetWidth = Math.Max(1, (int)Math.Round(targetSize.Width));
        var targetHeight = Math.Max(1, (int)Math.Round(targetSize.Height));

        if (sizingEdge is WmszLeft or WmszTopLeft or WmszBottomLeft)
        {
            rect.Left = rect.Right - targetWidth;
        }
        else
        {
            rect.Right = rect.Left + targetWidth;
        }

        if (sizingEdge is WmszTop or WmszTopLeft or WmszTopRight)
        {
            rect.Top = rect.Bottom - targetHeight;
        }
        else
        {
            rect.Bottom = rect.Top + targetHeight;
        }

        return true;
    }

    private Size CalculateWidthDrivenSize(double requestedWidth, SizingMetrics metrics)
    {
        var contentWidth = Math.Max(
            metrics.MinimumContentWidth,
            requestedWidth - metrics.FrameWidth);
        return new Size(
            contentWidth + metrics.FrameWidth,
            contentWidth / AspectRatio + metrics.FrameHeight);
    }

    private Size CalculateHeightDrivenSize(double requestedHeight, SizingMetrics metrics)
    {
        var contentHeight = Math.Max(
            metrics.MinimumContentHeight,
            requestedHeight - metrics.FrameHeight);
        return new Size(
            contentHeight * AspectRatio + metrics.FrameWidth,
            contentHeight + metrics.FrameHeight);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private readonly record struct SizingMetrics(
        double FrameWidth,
        double FrameHeight,
        double MinimumContentWidth,
        double MinimumContentHeight);
}
