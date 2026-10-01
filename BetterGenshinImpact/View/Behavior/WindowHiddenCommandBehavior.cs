using System;
using System.Windows;
using System.Windows.Input;
using Microsoft.Xaml.Behaviors;

namespace BetterGenshinImpact.View.Behavior;

/// <summary>
/// 窗口隐藏或最小化时执行命令。
/// IsVisibleChanged 的事件参数不是 EventArgs，无法直接用 EventTrigger 绑定，所以单独做成 Behavior。
/// </summary>
public sealed class WindowHiddenCommandBehavior : Behavior<Window>
{
    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(
            nameof(Command),
            typeof(ICommand),
            typeof(WindowHiddenCommandBehavior),
            new PropertyMetadata(null));

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.IsVisibleChanged += OnIsVisibleChanged;
        AssociatedObject.StateChanged += OnStateChanged;
    }

    protected override void OnDetaching()
    {
        AssociatedObject.IsVisibleChanged -= OnIsVisibleChanged;
        AssociatedObject.StateChanged -= OnStateChanged;
        base.OnDetaching();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!AssociatedObject.IsVisible)
        {
            Execute();
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (AssociatedObject.WindowState == WindowState.Minimized)
        {
            Execute();
        }
    }

    private void Execute()
    {
        var command = Command;
        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
        }
    }
}
