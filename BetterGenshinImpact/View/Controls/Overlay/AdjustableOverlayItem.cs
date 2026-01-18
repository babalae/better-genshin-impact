using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace BetterGenshinImpact.View.Controls.Overlay;

public class AdjustableOverlayItem : ContentControl
{
    public static readonly DependencyProperty LayoutKeyProperty =
        DependencyProperty.Register(
            nameof(LayoutKey),
            typeof(string),
            typeof(AdjustableOverlayItem),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsEditEnabledProperty =
        DependencyProperty.Register(
            nameof(IsEditEnabled),
            typeof(bool),
            typeof(AdjustableOverlayItem),
            new PropertyMetadata(false, OnIsEditEnabledChanged));

    public bool IsEditEnabled
    {
        get => (bool)GetValue(IsEditEnabledProperty);
        set => SetValue(IsEditEnabledProperty, value);
    }

    public string LayoutKey
    {
        get => (string)GetValue(LayoutKeyProperty);
        set => SetValue(LayoutKeyProperty, value);
    }

    public event EventHandler<OverlayLayoutCommittedEventArgs>? LayoutCommitted;

    private Thumb? _moveThumb;
    private readonly Dictionary<Thumb, ResizeDirection> _resizeThumbs = new();

    static AdjustableOverlayItem()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(AdjustableOverlayItem),
            new FrameworkPropertyMetadata(typeof(AdjustableOverlayItem)));
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_moveThumb != null)
        {
            _moveThumb.DragDelta -= MoveThumbOnDragDelta;
            _moveThumb.DragCompleted -= OnAnyThumbDragCompleted;
        }

        foreach (var (thumb, _) in _resizeThumbs)
        {
            thumb.DragDelta -= ResizeThumbOnDragDelta;
            thumb.DragCompleted -= OnAnyThumbDragCompleted;
        }

        _moveThumb = GetTemplateChild("PART_MoveThumb") as Thumb;
        _resizeThumbs.Clear();

        RegisterResizeThumb("PART_ResizeThumb", ResizeDirection.Right | ResizeDirection.Bottom);
        RegisterResizeThumb("PART_ResizeThumbTop", ResizeDirection.Top);
        RegisterResizeThumb("PART_ResizeThumbBottom", ResizeDirection.Bottom);
        RegisterResizeThumb("PART_ResizeThumbLeft", ResizeDirection.Left);
        RegisterResizeThumb("PART_ResizeThumbRight", ResizeDirection.Right);
        RegisterResizeThumb("PART_ResizeThumbTopLeft", ResizeDirection.Top | ResizeDirection.Left);
        RegisterResizeThumb("PART_ResizeThumbTopRight", ResizeDirection.Top | ResizeDirection.Right);
        RegisterResizeThumb("PART_ResizeThumbBottomLeft", ResizeDirection.Bottom | ResizeDirection.Left);
        RegisterResizeThumb("PART_ResizeThumbBottomRight", ResizeDirection.Bottom | ResizeDirection.Right);

        if (_moveThumb != null)
        {
            _moveThumb.DragDelta += MoveThumbOnDragDelta;
            _moveThumb.DragCompleted += OnAnyThumbDragCompleted;
        }

        foreach (var (thumb, _) in _resizeThumbs)
        {
            thumb.DragDelta += ResizeThumbOnDragDelta;
            thumb.DragCompleted += OnAnyThumbDragCompleted;
        }

        UpdateHitTestState();
    }

    private void RegisterResizeThumb(string partName, ResizeDirection direction)
    {
        if (GetTemplateChild(partName) is not Thumb thumb)
        {
            return;
        }

        _resizeThumbs[thumb] = direction;
    }

    private static void OnIsEditEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AdjustableOverlayItem item)
        {
            item.UpdateHitTestState();
        }
    }

    private void UpdateHitTestState()
    {
        IsHitTestVisible = IsEditEnabled;
    }

    private void MoveThumbOnDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!IsEditEnabled)
        {
            return;
        }

        var left = Canvas.GetLeft(this);
        var top = Canvas.GetTop(this);
        if (double.IsNaN(left)) left = 0;
        if (double.IsNaN(top)) top = 0;

        var nextLeft = left + e.HorizontalChange;
        var nextTop = top + e.VerticalChange;

        SetCurrentValue(Canvas.LeftProperty, nextLeft);
        SetCurrentValue(Canvas.TopProperty, nextTop);
    }

    private void ResizeThumbOnDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!IsEditEnabled)
        {
            return;
        }

        if (sender is not Thumb thumb || !_resizeThumbs.TryGetValue(thumb, out var direction))
        {
            direction = ResizeDirection.Right | ResizeDirection.Bottom;
        }

        var left = Canvas.GetLeft(this);
        var top = Canvas.GetTop(this);
        if (double.IsNaN(left)) left = 0;
        if (double.IsNaN(top)) top = 0;

        var width = Width;
        var height = Height;
        if (double.IsNaN(width) || width <= 0) width = ActualWidth;
        if (double.IsNaN(height) || height <= 0) height = ActualHeight;

        var right = left + width;
        var bottom = top + height;

        var nextLeft = left;
        var nextRight = right;
        var nextTop = top;
        var nextBottom = bottom;

        if (direction.HasFlag(ResizeDirection.Left))
        {
            nextLeft += e.HorizontalChange;
        }

        if (direction.HasFlag(ResizeDirection.Right))
        {
            nextRight += e.HorizontalChange;
        }

        if (direction.HasFlag(ResizeDirection.Top))
        {
            nextTop += e.VerticalChange;
        }

        if (direction.HasFlag(ResizeDirection.Bottom))
        {
            nextBottom += e.VerticalChange;
        }

        var minWidth = Math.Max(1, MinWidth);
        var minHeight = Math.Max(1, MinHeight);

        EnsureMinSize(ref nextLeft, ref nextRight, minWidth, direction, ResizeDirection.Left, ResizeDirection.Right);
        EnsureMinSize(ref nextTop, ref nextBottom, minHeight, direction, ResizeDirection.Top, ResizeDirection.Bottom);

        var nextWidth = nextRight - nextLeft;
        var nextHeight = nextBottom - nextTop;

        SetCurrentValue(Canvas.LeftProperty, nextLeft);
        SetCurrentValue(Canvas.TopProperty, nextTop);
        SetCurrentValue(WidthProperty, nextWidth);
        SetCurrentValue(HeightProperty, nextHeight);
    }

    private void OnAnyThumbDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (!IsEditEnabled)
        {
            return;
        }

        var key = LayoutKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var left = Canvas.GetLeft(this);
        var top = Canvas.GetTop(this);
        if (double.IsNaN(left)) left = 0;
        if (double.IsNaN(top)) top = 0;

        var width = Width;
        var height = Height;
        if (double.IsNaN(width) || width <= 0) width = ActualWidth;
        if (double.IsNaN(height) || height <= 0) height = ActualHeight;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        LayoutCommitted?.Invoke(this, new OverlayLayoutCommittedEventArgs(key, left, top, width, height));
    }

    private static void EnsureMinSize(
        ref double start,
        ref double end,
        double minSize,
        ResizeDirection direction,
        ResizeDirection startFlag,
        ResizeDirection endFlag)
    {
        var size = end - start;
        if (size >= minSize)
        {
            return;
        }

        var moveStart = direction.HasFlag(startFlag) && !direction.HasFlag(endFlag);
        if (moveStart)
        {
            start = end - minSize;
        }
        else
        {
            end = start + minSize;
        }
    }

    private static double Clamp(double value, double min, double max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    [Flags]
    private enum ResizeDirection
    {
        None = 0,
        Left = 1,
        Top = 2,
        Right = 4,
        Bottom = 8
    }
}
