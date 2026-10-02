using System.Windows;
using System.Windows.Controls;
using Microsoft.Xaml.Behaviors;

namespace BetterGenshinImpact.View.Behavior;

/// <summary>
/// 为 WPF TreeView 提供可双向绑定的选中项，避免在页面代码后置中处理交互。
/// </summary>
public sealed class TreeViewSelectedItemBehavior : Behavior<TreeView>
{
    /// <summary>
    /// 可双向绑定的树选中项依赖属性。
    /// </summary>
    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
        nameof(SelectedItem), typeof(object), typeof(TreeViewSelectedItemBehavior),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedItemChanged));

    /// <summary>
    /// 当前树选中项。
    /// </summary>
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>
    /// 关联 TreeView 后订阅选中项事件。
    /// </summary>
    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.SelectedItemChanged += OnTreeSelectedItemChanged;
    }

    /// <summary>
    /// 解除关联时移除事件订阅。
    /// </summary>
    protected override void OnDetaching()
    {
        AssociatedObject.SelectedItemChanged -= OnTreeSelectedItemChanged;
        base.OnDetaching();
    }

    /// <summary>
    /// 将用户在树中的选择同步回 ViewModel。
    /// </summary>
    private void OnTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (!ReferenceEquals(SelectedItem, e.NewValue))
            SelectedItem = e.NewValue;
    }

    /// <summary>
    /// 在 ViewModel 恢复撤销快照后，尽量把对应容器重新设为选中。
    /// </summary>
    private static void OnSelectedItemChanged(DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var behavior = (TreeViewSelectedItemBehavior)dependencyObject;
        if (behavior.AssociatedObject is null || args.NewValue is null
            || ReferenceEquals(behavior.AssociatedObject.SelectedItem, args.NewValue))
            return;
        if (FindContainer(behavior.AssociatedObject, args.NewValue) is { } container)
        {
            container.IsSelected = true;
            container.BringIntoView();
        }
    }

    /// <summary>
    /// 递归查找已生成的树项容器；未展开的分支不会被强制实例化。
    /// </summary>
    private static TreeViewItem? FindContainer(ItemsControl parent, object item)
    {
        if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem direct)
            return direct;
        foreach (var parentItem in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(parentItem) is not TreeViewItem child)
                continue;
            if (FindContainer(child, item) is { } nested)
                return nested;
        }
        return null;
    }
}
