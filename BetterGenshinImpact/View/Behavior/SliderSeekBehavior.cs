using Microsoft.Xaml.Behaviors;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace BetterGenshinImpact.View.Behavior;

public sealed class SliderSeekBehavior : Behavior<Slider>
{
    private bool _isPointerSeeking;

    public static readonly DependencyProperty BeginCommandProperty = DependencyProperty.Register(
        nameof(BeginCommand),
        typeof(ICommand),
        typeof(SliderSeekBehavior));

    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command),
        typeof(ICommand),
        typeof(SliderSeekBehavior));

    public ICommand? BeginCommand
    {
        get => (ICommand?)GetValue(BeginCommandProperty);
        set => SetValue(BeginCommandProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        AssociatedObject.PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
        AssociatedObject.LostMouseCapture += OnLostMouseCapture;
        AssociatedObject.KeyDown += OnKeyDown;
        AssociatedObject.KeyUp += OnKeyUp;
    }

    protected override void OnDetaching()
    {
        AssociatedObject.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        AssociatedObject.PreviewMouseLeftButtonUp -= OnPreviewMouseLeftButtonUp;
        AssociatedObject.LostMouseCapture -= OnLostMouseCapture;
        AssociatedObject.KeyDown -= OnKeyDown;
        AssociatedObject.KeyUp -= OnKeyUp;
        base.OnDetaching();
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isPointerSeeking = true;
        ExecuteBeginSeek();

        if (FindVisualParent<Thumb>(e.OriginalSource as DependencyObject) != null)
        {
            return;
        }

        SeekToPointerPosition(e);
        _isPointerSeeking = false;
        ExecuteSeek();
        e.Handled = true;
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPointerSeeking)
        {
            return;
        }

        _isPointerSeeking = false;
        ExecuteSeek();
    }

    private void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!_isPointerSeeking || Mouse.LeftButton == MouseButtonState.Pressed)
        {
            return;
        }

        _isPointerSeeking = false;
        ExecuteSeek();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (IsSeekKey(e.Key))
        {
            ExecuteBeginSeek();
        }
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (IsSeekKey(e.Key))
        {
            ExecuteSeek();
        }
    }

    private void ExecuteBeginSeek()
    {
        if (BeginCommand?.CanExecute(null) == true)
        {
            BeginCommand.Execute(null);
        }
    }

    private void ExecuteSeek()
    {
        var value = AssociatedObject.Value;
        if (Command?.CanExecute(value) == true)
        {
            Command.Execute(value);
        }
    }

    private void SeekToPointerPosition(MouseButtonEventArgs e)
    {
        var track = AssociatedObject.Template.FindName("PART_Track", AssociatedObject) as Track;
        if (track == null || track.ActualWidth <= 0)
        {
            return;
        }

        var thumbWidth = track.Thumb?.ActualWidth ?? 0;
        var availableWidth = Math.Max(1, track.ActualWidth - thumbWidth);
        var pointerX = e.GetPosition(track).X - thumbWidth / 2;
        var ratio = Math.Clamp(pointerX / availableWidth, 0, 1);
        if (AssociatedObject.IsDirectionReversed)
        {
            ratio = 1 - ratio;
        }

        var value = AssociatedObject.Minimum
                    + ratio * (AssociatedObject.Maximum - AssociatedObject.Minimum);
        AssociatedObject.SetCurrentValue(RangeBase.ValueProperty, value);
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T result)
            {
                return result;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }

    private static bool IsSeekKey(Key key)
    {
        return key is Key.Left or Key.Right or Key.Home or Key.End or Key.PageUp or Key.PageDown;
    }
}
