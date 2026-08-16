using Microsoft.Xaml.Behaviors;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BetterGenshinImpact.View.Behavior;

/// <summary>
/// 为模板截图视口提供适应窗口和 Ctrl+滚轮缩放。
/// </summary>
public sealed class TemplateImageZoomBehavior : Behavior<ScrollViewer>
{
    public static readonly DependencyProperty ImageWidthProperty = DependencyProperty.Register(
        nameof(ImageWidth),
        typeof(double),
        typeof(TemplateImageZoomBehavior),
        new PropertyMetadata(0d, OnImageSizeChanged));

    public static readonly DependencyProperty ImageHeightProperty = DependencyProperty.Register(
        nameof(ImageHeight),
        typeof(double),
        typeof(TemplateImageZoomBehavior),
        new PropertyMetadata(0d, OnImageSizeChanged));

    public static readonly DependencyProperty ZoomScaleProperty = DependencyProperty.Register(
        nameof(ZoomScale),
        typeof(double),
        typeof(TemplateImageZoomBehavior),
        new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty FitRequestTokenProperty = DependencyProperty.Register(
        nameof(FitRequestToken),
        typeof(int),
        typeof(TemplateImageZoomBehavior),
        new PropertyMetadata(0, OnFitRequestChanged));

    public double ImageWidth
    {
        get => (double)GetValue(ImageWidthProperty);
        set => SetValue(ImageWidthProperty, value);
    }

    public double ImageHeight
    {
        get => (double)GetValue(ImageHeightProperty);
        set => SetValue(ImageHeightProperty, value);
    }

    public double ZoomScale
    {
        get => (double)GetValue(ZoomScaleProperty);
        set => SetValue(ZoomScaleProperty, value);
    }

    public int FitRequestToken
    {
        get => (int)GetValue(FitRequestTokenProperty);
        set => SetValue(FitRequestTokenProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.Loaded += OnLoaded;
        AssociatedObject.PreviewMouseWheel += OnPreviewMouseWheel;
    }

    protected override void OnDetaching()
    {
        AssociatedObject.Loaded -= OnLoaded;
        AssociatedObject.PreviewMouseWheel -= OnPreviewMouseWheel;
        base.OnDetaching();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        FitToViewport();
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        var factor = e.Delta > 0 ? 1.1 : 1 / 1.1;
        SetCurrentValue(ZoomScaleProperty, Math.Clamp(ZoomScale * factor, 0.1, 4));
        e.Handled = true;
    }

    private void FitToViewport()
    {
        if (AssociatedObject == null || ImageWidth <= 0 || ImageHeight <= 0)
        {
            return;
        }

        var viewportWidth = AssociatedObject.ViewportWidth > 0
            ? AssociatedObject.ViewportWidth
            : AssociatedObject.ActualWidth;
        var viewportHeight = AssociatedObject.ViewportHeight > 0
            ? AssociatedObject.ViewportHeight
            : AssociatedObject.ActualHeight;
        if (viewportWidth <= 0 || viewportHeight <= 0)
        {
            return;
        }

        var scale = Math.Min(
            Math.Max(1, viewportWidth - 24) / ImageWidth,
            Math.Max(1, viewportHeight - 24) / ImageHeight);
        SetCurrentValue(ZoomScaleProperty, Math.Clamp(scale, 0.1, 4));
        AssociatedObject.ScrollToHorizontalOffset(0);
        AssociatedObject.ScrollToVerticalOffset(0);
    }

    private static void OnImageSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TemplateImageZoomBehavior behavior && behavior.AssociatedObject?.IsLoaded == true)
        {
            behavior.FitToViewport();
        }
    }

    private static void OnFitRequestChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TemplateImageZoomBehavior behavior && behavior.AssociatedObject?.IsLoaded == true)
        {
            behavior.FitToViewport();
        }
    }
}
