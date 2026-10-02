using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BetterGenshinImpact.View.Behavior;

/// <summary>
/// 让树或列表在打开右键菜单前先选中鼠标所在项，保证上下文命令作用于用户点击的对象。
/// </summary>
public static class RightClickSelectBehavior
{
    /// <summary>
    /// 读取是否启用右键选中行为。
    /// </summary>
    public static bool GetEnabled(DependencyObject obj)
    {
        return (bool)obj.GetValue(EnabledProperty);
    }

    /// <summary>
    /// 设置是否启用右键选中行为。
    /// </summary>
    public static void SetEnabled(DependencyObject obj, bool value)
    {
        obj.SetValue(EnabledProperty, value);
    }

    /// <summary>
    /// 控制右键选中行为的附加属性。
    /// </summary>
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(RightClickSelectBehavior), new PropertyMetadata(false, OnEnabledChanged));

    /// <summary>
    /// 根据附加属性变化订阅或移除相应控件的鼠标事件。
    /// </summary>
    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreeView treeView)
        {
            if ((bool)e.NewValue)
            {
                treeView.PreviewMouseRightButtonDown += OnPreviewMouseRightButtonDown;
            }
            else
            {
                treeView.PreviewMouseRightButtonDown -= OnPreviewMouseRightButtonDown;
            }
        }
        else if (d is ListBox listBox)
        {
            if ((bool)e.NewValue)
            {
                listBox.PreviewMouseRightButtonDown += OnPreviewMouseRightButtonDown;
            }
            else
            {
                listBox.PreviewMouseRightButtonDown -= OnPreviewMouseRightButtonDown;
            }
        }
    }

    /// <summary>
    /// 将右键所在的树节点或列表项设为当前选择。
    /// </summary>
    private static void OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TreeView treeView)
        {
            var item = VisualUpwardSearch<TreeViewItem>(e.OriginalSource as DependencyObject);
            if (item != null)
            {
                item.Focus();
                item.IsSelected = true;
            }
        }
        else if (sender is ListBox)
        {
            var item = VisualUpwardSearch<ListBoxItem>(e.OriginalSource as DependencyObject);
            if (item != null)
            {
                item.Focus();
                item.IsSelected = true;
            }
        }
    }

    /// <summary>
    /// 沿视觉树向上查找指定类型的容器。
    /// </summary>
    private static T? VisualUpwardSearch<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source != null && !(source is T))
        {
            source = VisualTreeHelper.GetParent(source);
        }
        return source as T;
    }
}
