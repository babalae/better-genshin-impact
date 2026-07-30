using Microsoft.Xaml.Behaviors;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BetterGenshinImpact.View.Behavior;

public sealed class SliderSeekBehavior : Behavior<Slider>
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command),
        typeof(ICommand),
        typeof(SliderSeekBehavior));

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
        AssociatedObject.KeyUp += OnKeyUp;
    }

    protected override void OnDetaching()
    {
        AssociatedObject.PreviewMouseLeftButtonUp -= OnPreviewMouseLeftButtonUp;
        AssociatedObject.KeyUp -= OnKeyUp;
        base.OnDetaching();
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ExecuteSeek();
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right or Key.Home or Key.End or Key.PageUp or Key.PageDown)
        {
            ExecuteSeek();
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
}
