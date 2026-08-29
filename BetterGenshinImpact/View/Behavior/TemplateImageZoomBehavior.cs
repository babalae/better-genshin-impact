using Microsoft.Xaml.Behaviors;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BetterGenshinImpact.View.Behavior;

/// <summary>
/// 为模板截图视口提供适应窗口、滚轮缩放和右键拖拽平移。
/// </summary>
public sealed class TemplateImageZoomBehavior : Behavior<ScrollViewer>
{
    private bool _isPanning;
    private Point _panStartPoint;
    private double _panStartHorizontalOffset;
    private double _panStartVerticalOffset;
    private Cursor? _previousCursor;
    private bool _previousForceCursor;

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
        AssociatedObject.PreviewMouseRightButtonDown += OnPreviewMouseRightButtonDown;
        AssociatedObject.PreviewMouseMove += OnPreviewMouseMove;
        AssociatedObject.PreviewMouseRightButtonUp += OnPreviewMouseRightButtonUp;
        AssociatedObject.LostMouseCapture += OnLostMouseCapture;
    }

    protected override void OnDetaching()
    {
        FinishPanning();
        AssociatedObject.Loaded -= OnLoaded;
        AssociatedObject.PreviewMouseWheel -= OnPreviewMouseWheel;
        AssociatedObject.PreviewMouseRightButtonDown -= OnPreviewMouseRightButtonDown;
        AssociatedObject.PreviewMouseMove -= OnPreviewMouseMove;
        AssociatedObject.PreviewMouseRightButtonUp -= OnPreviewMouseRightButtonUp;
        AssociatedObject.LostMouseCapture -= OnLostMouseCapture;
        base.OnDetaching();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        FitToViewport();
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta == 0)
        {
            return;
        }

        var oldScale = ZoomScale;
        var factor = e.Delta > 0 ? 1.1 : 1 / 1.1;
        var newScale = Math.Clamp(oldScale * factor, 0.1, 4);
        if (Math.Abs(newScale - oldScale) < double.Epsilon)
        {
            e.Handled = true;
            return;
        }

        var mousePosition = e.GetPosition(AssociatedObject);
        var scaleRatio = newScale / oldScale;
        var targetHorizontalOffset = (AssociatedObject.HorizontalOffset + mousePosition.X) * scaleRatio - mousePosition.X;
        var targetVerticalOffset = (AssociatedObject.VerticalOffset + mousePosition.Y) * scaleRatio - mousePosition.Y;

        SetCurrentValue(ZoomScaleProperty, newScale);
        AssociatedObject.UpdateLayout();
        AssociatedObject.ScrollToHorizontalOffset(targetHorizontalOffset);
        AssociatedObject.ScrollToVerticalOffset(targetVerticalOffset);
        e.Handled = true;
    }

    private void OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isPanning = true;
        _panStartPoint = e.GetPosition(AssociatedObject);
        _panStartHorizontalOffset = AssociatedObject.HorizontalOffset;
        _panStartVerticalOffset = AssociatedObject.VerticalOffset;
        _previousCursor = AssociatedObject.Cursor;
        _previousForceCursor = AssociatedObject.ForceCursor;
        AssociatedObject.Cursor = Cursors.SizeAll;
        AssociatedObject.ForceCursor = true;
        AssociatedObject.CaptureMouse();
        e.Handled = true;
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        if (e.RightButton != MouseButtonState.Pressed)
        {
            FinishPanning();
            return;
        }

        var currentPoint = e.GetPosition(AssociatedObject);
        AssociatedObject.ScrollToHorizontalOffset(_panStartHorizontalOffset - (currentPoint.X - _panStartPoint.X));
        AssociatedObject.ScrollToVerticalOffset(_panStartVerticalOffset - (currentPoint.Y - _panStartPoint.Y));
        e.Handled = true;
    }

    private void OnPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        FinishPanning();
        e.Handled = true;
    }

    private void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        FinishPanning();
    }

    private void FinishPanning()
    {
        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;
        AssociatedObject.Cursor = _previousCursor;
        AssociatedObject.ForceCursor = _previousForceCursor;
        if (AssociatedObject.IsMouseCaptured)
        {
            AssociatedObject.ReleaseMouseCapture();
        }
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
