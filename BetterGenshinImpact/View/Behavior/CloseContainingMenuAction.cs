using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Xaml.Behaviors;

namespace BetterGenshinImpact.View.Behavior;

/// <summary>从触发按钮查找所属菜单，避免行为对象无法解析视觉祖先绑定。</summary>
public sealed class CloseContainingMenuAction : TriggerAction<FrameworkElement>
{
    /// <summary>关闭承载按钮的菜单，不改变筛选结果。</summary>
    protected override void Invoke(object parameter)
    {
        DependencyObject? current = AssociatedObject;
        while (current != null)
        {
            if (current is ContextMenu menu)
            {
                menu.IsOpen = false;
                return;
            }
            current = current is MenuItem item
                ? ItemsControl.ItemsControlFromItemContainer(item)
                : VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
        }
    }
}