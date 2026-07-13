using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BetterGenshinImpact.View.Behavior;

public static class RightClickSelectBehavior
{
    public static bool GetEnabled(DependencyObject obj)
    {
        return (bool)obj.GetValue(EnabledProperty);
    }

    public static void SetEnabled(DependencyObject obj, bool value)
    {
        obj.SetValue(EnabledProperty, value);
    }

    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(RightClickSelectBehavior), new PropertyMetadata(false, OnEnabledChanged));

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
    }

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
    }

    private static T VisualUpwardSearch<T>(DependencyObject source) where T : DependencyObject
    {
        while (source != null && !(source is T))
        {
            source = VisualTreeHelper.GetParent(source);
        }
        return source as T;
    }
}
