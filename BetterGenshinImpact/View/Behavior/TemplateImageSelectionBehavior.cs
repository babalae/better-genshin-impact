using Microsoft.Xaml.Behaviors;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BetterGenshinImpact.View.Behavior;

/// <summary>
/// 在与原图像素尺寸一致的 Canvas 上通过鼠标框选整数像素区域。
/// </summary>
public sealed class TemplateImageSelectionBehavior : Behavior<Canvas>
{
    private Point _startPoint;
    private bool _isSelecting;

    public static readonly DependencyProperty SelectionXProperty = RegisterSelectionProperty(nameof(SelectionX));
    public static readonly DependencyProperty SelectionYProperty = RegisterSelectionProperty(nameof(SelectionY));
    public static readonly DependencyProperty SelectionWidthProperty = RegisterSelectionProperty(nameof(SelectionWidth));
    public static readonly DependencyProperty SelectionHeightProperty = RegisterSelectionProperty(nameof(SelectionHeight));

    public int SelectionX
    {
        get => (int)GetValue(SelectionXProperty);
        set => SetValue(SelectionXProperty, value);
    }

    public int SelectionY
    {
        get => (int)GetValue(SelectionYProperty);
        set => SetValue(SelectionYProperty, value);
    }

    public int SelectionWidth
    {
        get => (int)GetValue(SelectionWidthProperty);
        set => SetValue(SelectionWidthProperty, value);
    }

    public int SelectionHeight
    {
        get => (int)GetValue(SelectionHeightProperty);
        set => SetValue(SelectionHeightProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.MouseLeftButtonDown += OnMouseLeftButtonDown;
        AssociatedObject.MouseMove += OnMouseMove;
        AssociatedObject.MouseLeftButtonUp += OnMouseLeftButtonUp;
        AssociatedObject.LostMouseCapture += OnLostMouseCapture;
    }

    protected override void OnDetaching()
    {
        AssociatedObject.MouseLeftButtonDown -= OnMouseLeftButtonDown;
        AssociatedObject.MouseMove -= OnMouseMove;
        AssociatedObject.MouseLeftButtonUp -= OnMouseLeftButtonUp;
        AssociatedObject.LostMouseCapture -= OnLostMouseCapture;
        base.OnDetaching();
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _startPoint = ClampPoint(e.GetPosition(AssociatedObject));
        _isSelecting = true;
        AssociatedObject.CaptureMouse();
        UpdateSelection(_startPoint);
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isSelecting || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        UpdateSelection(ClampPoint(e.GetPosition(AssociatedObject)));
        e.Handled = true;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isSelecting)
        {
            return;
        }

        UpdateSelection(ClampPoint(e.GetPosition(AssociatedObject)));
        FinishSelection();
        e.Handled = true;
    }

    private void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        _isSelecting = false;
    }

    private void UpdateSelection(Point currentPoint)
    {
        var left = (int)Math.Floor(Math.Min(_startPoint.X, currentPoint.X));
        var top = (int)Math.Floor(Math.Min(_startPoint.Y, currentPoint.Y));
        var right = (int)Math.Ceiling(Math.Max(_startPoint.X, currentPoint.X));
        var bottom = (int)Math.Ceiling(Math.Max(_startPoint.Y, currentPoint.Y));

        left = Math.Clamp(left, 0, (int)AssociatedObject.Width);
        top = Math.Clamp(top, 0, (int)AssociatedObject.Height);
        right = Math.Clamp(right, left, (int)AssociatedObject.Width);
        bottom = Math.Clamp(bottom, top, (int)AssociatedObject.Height);

        SetCurrentValue(SelectionXProperty, left);
        SetCurrentValue(SelectionYProperty, top);
        SetCurrentValue(SelectionWidthProperty, right - left);
        SetCurrentValue(SelectionHeightProperty, bottom - top);
    }

    private Point ClampPoint(Point point)
    {
        return new Point(
            Math.Clamp(point.X, 0, AssociatedObject.Width),
            Math.Clamp(point.Y, 0, AssociatedObject.Height));
    }

    private void FinishSelection()
    {
        _isSelecting = false;
        if (AssociatedObject.IsMouseCaptured)
        {
            AssociatedObject.ReleaseMouseCapture();
        }
    }

    private static DependencyProperty RegisterSelectionProperty(string propertyName)
    {
        return DependencyProperty.Register(
            propertyName,
            typeof(int),
            typeof(TemplateImageSelectionBehavior),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    }
}
