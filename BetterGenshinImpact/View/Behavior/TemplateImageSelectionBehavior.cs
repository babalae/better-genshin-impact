using Microsoft.Xaml.Behaviors;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BetterGenshinImpact.View.Behavior;

/// <summary>
/// 模板编辑画布当前由鼠标修改的矩形类型。
/// </summary>
public enum TemplateImageSelectionTarget
{
    /// <summary>编辑用于裁剪模板图片的蓝色模板框。</summary>
    Template,

    /// <summary>编辑参考画布坐标系中的橙色基础搜索框，不包含扩展量。</summary>
    SearchBox
}

/// <summary>
/// 在与原图像素尺寸一致的 Canvas 上通过鼠标框选模板区域或独立搜索区域。
/// 所有依赖属性都使用原图整数像素，缩放仅由外层画布的 LayoutTransform 负责。
/// </summary>
public sealed class TemplateImageSelectionBehavior : Behavior<Canvas>
{
    private Point _startPoint;
    private bool _isSelecting;

    public static readonly DependencyProperty SelectionXProperty = RegisterSelectionProperty(nameof(SelectionX));
    public static readonly DependencyProperty SelectionYProperty = RegisterSelectionProperty(nameof(SelectionY));
    public static readonly DependencyProperty SelectionWidthProperty = RegisterSelectionProperty(nameof(SelectionWidth));
    public static readonly DependencyProperty SelectionHeightProperty = RegisterSelectionProperty(nameof(SelectionHeight));
    public static readonly DependencyProperty SearchBoxXProperty = RegisterSelectionProperty(nameof(SearchBoxX));
    public static readonly DependencyProperty SearchBoxYProperty = RegisterSelectionProperty(nameof(SearchBoxY));
    public static readonly DependencyProperty SearchBoxWidthProperty = RegisterSelectionProperty(nameof(SearchBoxWidth));
    public static readonly DependencyProperty SearchBoxHeightProperty = RegisterSelectionProperty(nameof(SearchBoxHeight));
    public static readonly DependencyProperty SelectionTargetProperty = DependencyProperty.Register(
        nameof(SelectionTarget),
        typeof(TemplateImageSelectionTarget),
        typeof(TemplateImageSelectionBehavior),
        new PropertyMetadata(TemplateImageSelectionTarget.Template));
    public static readonly DependencyProperty IsSearchBoxEnabledProperty = DependencyProperty.Register(
        nameof(IsSearchBoxEnabled),
        typeof(bool),
        typeof(TemplateImageSelectionBehavior),
        new PropertyMetadata(false));

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

    public int SearchBoxX
    {
        get => (int)GetValue(SearchBoxXProperty);
        set => SetValue(SearchBoxXProperty, value);
    }

    public int SearchBoxY
    {
        get => (int)GetValue(SearchBoxYProperty);
        set => SetValue(SearchBoxYProperty, value);
    }

    public int SearchBoxWidth
    {
        get => (int)GetValue(SearchBoxWidthProperty);
        set => SetValue(SearchBoxWidthProperty, value);
    }

    public int SearchBoxHeight
    {
        get => (int)GetValue(SearchBoxHeightProperty);
        set => SetValue(SearchBoxHeightProperty, value);
    }

    public TemplateImageSelectionTarget SelectionTarget
    {
        get => (TemplateImageSelectionTarget)GetValue(SelectionTargetProperty);
        set => SetValue(SelectionTargetProperty, value);
    }

    public bool IsSearchBoxEnabled
    {
        get => (bool)GetValue(IsSearchBoxEnabledProperty);
        set => SetValue(IsSearchBoxEnabledProperty, value);
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
        if (SelectionTarget == TemplateImageSelectionTarget.SearchBox && !IsSearchBoxEnabled)
        {
            return;
        }

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

        if (SelectionTarget == TemplateImageSelectionTarget.SearchBox)
        {
            SetCurrentValue(SearchBoxXProperty, left);
            SetCurrentValue(SearchBoxYProperty, top);
            SetCurrentValue(SearchBoxWidthProperty, right - left);
            SetCurrentValue(SearchBoxHeightProperty, bottom - top);
        }
        else
        {
            SetCurrentValue(SelectionXProperty, left);
            SetCurrentValue(SelectionYProperty, top);
            SetCurrentValue(SelectionWidthProperty, right - left);
            SetCurrentValue(SelectionHeightProperty, bottom - top);
        }
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
