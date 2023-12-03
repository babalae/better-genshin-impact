using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MicaSetup.Design.Controls;

public class SmoothScrollViewer : ScrollViewer
{
    private double _totalVerticalOffset;
    private double _totalHorizontalOffset;
    private bool _isRunning;

    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(SmoothScrollViewer), new PropertyMetadata(Orientation.Vertical));

    public bool CanMouseWheel
    {
        get => (bool)GetValue(CanMouseWheelProperty);
        set => SetValue(CanMouseWheelProperty, value);
    }

    public static readonly DependencyProperty CanMouseWheelProperty = DependencyProperty.Register(nameof(CanMouseWheel), typeof(bool), typeof(SmoothScrollViewer), new PropertyMetadata(true));

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (ViewportHeight + VerticalOffset >= ExtentHeight && e.Delta <= 0)
        {
            return;
        }

        if (VerticalOffset == 0 && e.Delta >= 0)
        {
            return;
        }

        if (!CanMouseWheel)
        {
            return;
        }

        if (!IsInertiaEnabled)
        {
            if (Orientation == Orientation.Vertical)
            {
                base.OnMouseWheel(e);
            }
            else
            {
                _totalHorizontalOffset = HorizontalOffset;
                CurrentHorizontalOffset = HorizontalOffset;
                _totalHorizontalOffset = Math.Min(Math.Max(0, _totalHorizontalOffset - e.Delta), ScrollableWidth);
                CurrentHorizontalOffset = _totalHorizontalOffset;
            }

            return;
        }

        e.Handled = true;

        if (Orientation == Orientation.Vertical)
        {
            if (!_isRunning)
            {
                _totalVerticalOffset = VerticalOffset;
                CurrentVerticalOffset = VerticalOffset;
            }

            _totalVerticalOffset = Math.Min(Math.Max(0, _totalVerticalOffset - e.Delta), ScrollableHeight);
            ScrollToVerticalOffsetWithAnimation(_totalVerticalOffset);
        }
        else
        {
            if (!_isRunning)
            {
                _totalHorizontalOffset = HorizontalOffset;
                CurrentHorizontalOffset = HorizontalOffset;
            }

            _totalHorizontalOffset = Math.Min(Math.Max(0, _totalHorizontalOffset - e.Delta), ScrollableWidth);
            ScrollToHorizontalOffsetWithAnimation(_totalHorizontalOffset);
        }
    }

    internal void ScrollToTopInternal(double milliseconds = 500)
    {
        if (!_isRunning)
        {
            _totalVerticalOffset = VerticalOffset;
            CurrentVerticalOffset = VerticalOffset;
        }

        ScrollToVerticalOffsetWithAnimation(0, milliseconds);
    }

    public void ScrollToVerticalOffsetWithAnimation(double offset, double milliseconds = 500)
    {
        DoubleAnimation animation = AnimationHelper.CreateAnimation(offset, milliseconds);
        animation.EasingFunction = new CubicEase
        {
            EasingMode = EasingMode.EaseOut
        };
        animation.FillBehavior = FillBehavior.Stop;
        animation.Completed += (s, e1) =>
        {
            CurrentVerticalOffset = offset;
            _isRunning = false;
        };
        _isRunning = true;

        BeginAnimation(CurrentVerticalOffsetProperty, animation, HandoffBehavior.Compose);
    }

    public void ScrollToHorizontalOffsetWithAnimation(double offset, double milliseconds = 500)
    {
        DoubleAnimation animation = AnimationHelper.CreateAnimation(offset, milliseconds);
        animation.EasingFunction = new CubicEase
        {
            EasingMode = EasingMode.EaseOut
        };
        animation.FillBehavior = FillBehavior.Stop;
        animation.Completed += (s, e1) =>
        {
            CurrentHorizontalOffset = offset;
            _isRunning = false;
        };
        _isRunning = true;

        BeginAnimation(CurrentHorizontalOffsetProperty, animation, HandoffBehavior.Compose);
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
    {
        return IsPenetrating ? null : base.HitTestCore(hitTestParameters);
    }

    public static void SetIsInertiaEnabled(DependencyObject element, bool value)
    {
        element.SetValue(IsInertiaEnabledProperty, value);
    }

    public static bool GetIsInertiaEnabled(DependencyObject element)
    {
        return (bool)element.GetValue(IsInertiaEnabledProperty);
    }

    public bool IsInertiaEnabled
    {
        get => (bool)GetValue(IsInertiaEnabledProperty);
        set => SetValue(IsInertiaEnabledProperty, value);
    }

    public static readonly DependencyProperty IsInertiaEnabledProperty = DependencyProperty.RegisterAttached(nameof(IsInertiaEnabled), typeof(bool), typeof(SmoothScrollViewer), new PropertyMetadata(true));

    public bool IsPenetrating
    {
        get => (bool)GetValue(IsPenetratingProperty);
        set => SetValue(IsPenetratingProperty, value);
    }

    public static void SetIsPenetrating(DependencyObject element, bool value)
    {
        element.SetValue(IsPenetratingProperty, value);
    }

    public static bool GetIsPenetrating(DependencyObject element)
    {
        return (bool)element.GetValue(IsPenetratingProperty);
    }

    public static readonly DependencyProperty IsPenetratingProperty = DependencyProperty.RegisterAttached(nameof(IsPenetrating), typeof(bool), typeof(SmoothScrollViewer), new PropertyMetadata(false));

    internal double CurrentVerticalOffset
    {
        get => (double)GetValue(CurrentVerticalOffsetProperty);
        set => SetValue(CurrentVerticalOffsetProperty, value);
    }

    internal static readonly DependencyProperty CurrentVerticalOffsetProperty = DependencyProperty.Register(nameof(CurrentVerticalOffset), typeof(double), typeof(SmoothScrollViewer), new PropertyMetadata(0d, OnCurrentVerticalOffsetChanged));

    private static void OnCurrentVerticalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SmoothScrollViewer ctl && e.NewValue is double v)
        {
            ctl.ScrollToVerticalOffset(v);
        }
    }

    internal double CurrentHorizontalOffset
    {
        get => (double)GetValue(CurrentHorizontalOffsetProperty);
        set => SetValue(CurrentHorizontalOffsetProperty, value);
    }

    internal static readonly DependencyProperty CurrentHorizontalOffsetProperty = DependencyProperty.Register(nameof(CurrentHorizontalOffset), typeof(double), typeof(SmoothScrollViewer), new PropertyMetadata(0d, OnCurrentHorizontalOffsetChanged));

    private static void OnCurrentHorizontalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SmoothScrollViewer ctl && e.NewValue is double v)
        {
            ctl.ScrollToHorizontalOffset(v);
        }
    }
}

file sealed class AnimationHelper
{
    public static DoubleAnimation CreateAnimation(double toValue, double milliseconds = 200)
    {
        return new(toValue, new Duration(TimeSpan.FromMilliseconds(milliseconds)))
        {
            EasingFunction = new PowerEase { EasingMode = EasingMode.EaseInOut }
        };
    }
}
