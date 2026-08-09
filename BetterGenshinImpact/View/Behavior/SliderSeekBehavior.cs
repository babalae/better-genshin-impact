using Microsoft.Xaml.Behaviors;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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

    private static bool IsSeekKey(Key key)
    {
        return key is Key.Left or Key.Right or Key.Home or Key.End or Key.PageUp or Key.PageDown;
    }
}
