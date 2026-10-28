using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
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
    /// 点击树空白区域时是否取消选中的依赖属性；默认关闭，保持原有选择行为。
    /// </summary>
    public static readonly DependencyProperty ClearSelectionOnEmptyClickProperty = DependencyProperty.Register(
        nameof(ClearSelectionOnEmptyClick), typeof(bool), typeof(TreeViewSelectedItemBehavior), new PropertyMetadata(false));

    /// <summary>
    /// 当前树选中项。
    /// </summary>
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>
    /// 点击树空白区域时是否取消当前选中项，让“按选中位置添加”的工具栏可以回到“添加到末尾”。
    /// </summary>
    public bool ClearSelectionOnEmptyClick
    {
        get => (bool)GetValue(ClearSelectionOnEmptyClickProperty);
        set => SetValue(ClearSelectionOnEmptyClickProperty, value);
    }

    /// <summary>
    /// 关联 TreeView 后订阅选中项与空白点击事件。
    /// </summary>
    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.SelectedItemChanged += OnTreeSelectedItemChanged;
        AssociatedObject.PreviewMouseLeftButtonDown += OnTreePreviewMouseLeftButtonDown;
    }

    /// <summary>
    /// 解除关联时移除事件订阅。
    /// </summary>
    protected override void OnDetaching()
    {
        AssociatedObject.SelectedItemChanged -= OnTreeSelectedItemChanged;
        AssociatedObject.PreviewMouseLeftButtonDown -= OnTreePreviewMouseLeftButtonDown;
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
    /// 启用空白点击取消选中时，点在树项与滚动条之外即取消当前选中；选中项随 SelectedItemChanged 同步为 null。
    /// </summary>
    private void OnTreePreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!ClearSelectionOnEmptyClick || AssociatedObject.SelectedItem is not { } selected
            || IsInsideItemOrScrollBar(e.OriginalSource as DependencyObject))
            return;
        if (FindContainer(AssociatedObject, selected) is { } container)
            container.IsSelected = false;
    }

    /// <summary>
    /// 判断点击位置是否位于树项或滚动条内；文本等内容元素沿逻辑树向上查找。
    /// </summary>
    private static bool IsInsideItemOrScrollBar(DependencyObject? source)
    {
        for (var current = source; current is not null;
             current = current is Visual or Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
        {
            if (current is TreeViewItem or ScrollBar)
                return true;
        }
        return false;
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
