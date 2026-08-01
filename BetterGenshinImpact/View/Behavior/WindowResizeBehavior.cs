using System;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Xaml.Behaviors;

namespace BetterGenshinImpact.View.Behavior;

public sealed class WindowResizeBehavior : Behavior<FrameworkElement>
{
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

    private Window? _window;

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
        _window ??= Window.GetWindow(AssociatedObject);
    }

    private void DetachWindow()
    {
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

        _ = _window.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(ResizeWindowCore));
    }

    private void ResizeWindowCore()
    {
        if (_window is null ||
            !double.IsFinite(TargetWidth) ||
            TargetWidth <= 0 ||
            !double.IsFinite(AspectRatio) ||
            AspectRatio <= 0 ||
            AssociatedObject.ActualWidth <= 0 ||
            AssociatedObject.ActualHeight <= 0)
        {
            return;
        }

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
}
