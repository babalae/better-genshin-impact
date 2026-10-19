using Microsoft.Xaml.Behaviors;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BetterGenshinImpact.View.Behavior;

/// <summary>
/// 在自定义标题栏内独占右键事件并打开控件自己的菜单，避免同时弹出 Windows 系统菜单。
/// </summary>
public sealed class TitleBarContextMenuBehavior : Behavior<FrameworkElement>
{
    /// <summary>
    /// 订阅标题栏交互控件的右键抬起事件。
    /// </summary>
    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.PreviewMouseRightButtonUp += OnPreviewMouseRightButtonUp;
    }

    /// <summary>
    /// 解除右键事件订阅，避免控件释放后仍被行为引用。
    /// </summary>
    protected override void OnDetaching()
    {
        AssociatedObject.PreviewMouseRightButtonUp -= OnPreviewMouseRightButtonUp;
        base.OnDetaching();
    }

    /// <summary>
    /// 手动打开实例菜单并终止事件路由，使标题栏不再继续显示系统菜单。
    /// </summary>
    private void OnPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (AssociatedObject.ContextMenu is not ContextMenu contextMenu)
        {
            return;
        }

        contextMenu.PlacementTarget = AssociatedObject;
        contextMenu.IsOpen = true;
        e.Handled = true;
    }
}
