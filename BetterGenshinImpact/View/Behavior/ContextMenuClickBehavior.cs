using Microsoft.Xaml.Behaviors;
using System.Windows;
using System.Windows.Input;

namespace BetterGenshinImpact.View.Behavior;

/// <summary>
/// 只允许完整的右键点击触发菜单，避免清理模拟输入时单独发送的右键抬起误弹菜单。
/// 不拦截键盘触发的上下文菜单。
/// </summary>
public sealed class ContextMenuClickBehavior : Behavior<FrameworkElement>
{
    private bool _isRightButtonPressed;

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.AddHandler(UIElement.PreviewMouseRightButtonDownEvent,
            new MouseButtonEventHandler(OnRightButtonDown), true);
        AssociatedObject.AddHandler(UIElement.PreviewMouseRightButtonUpEvent,
            new MouseButtonEventHandler(OnRightButtonUp), true);
        AssociatedObject.MouseLeave += OnReset;
        AssociatedObject.LostMouseCapture += OnReset;
        AssociatedObject.Unloaded += OnReset;
    }

    protected override void OnDetaching()
    {
        AssociatedObject.RemoveHandler(UIElement.PreviewMouseRightButtonDownEvent,
            new MouseButtonEventHandler(OnRightButtonDown));
        AssociatedObject.RemoveHandler(UIElement.PreviewMouseRightButtonUpEvent,
            new MouseButtonEventHandler(OnRightButtonUp));
        AssociatedObject.MouseLeave -= OnReset;
        AssociatedObject.LostMouseCapture -= OnReset;
        AssociatedObject.Unloaded -= OnReset;
        _isRightButtonPressed = false;
        base.OnDetaching();
    }

    private void OnRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isRightButtonPressed = true;
    }

    private void OnRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isRightButtonPressed)
        {
            e.Handled = true;
        }

        _isRightButtonPressed = false;
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        _isRightButtonPressed = false;
    }
}
