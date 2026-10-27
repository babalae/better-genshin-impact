using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Xaml.Behaviors;

namespace BetterGenshinImpact.View.Behavior;

/// <summary>右键菜单打开前先选中对应行，保留系统菜单和键盘操作。</summary>
public sealed class AccountRecordsRowSelectionBehavior : Behavior<DataGrid>
{
    /// <summary>监听预览事件，不阻止菜单继续打开。</summary>
    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.PreviewMouseRightButtonDown += SelectRow;
    }

    /// <summary>只处理本表的数据行，不改变空白区域的选择。</summary>
    private void SelectRow(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(AssociatedObject, source) is DataGridRow row)
        {
            AssociatedObject.SelectedItem = row.Item;
            row.Focus();
        }
    }

    /// <summary>解除事件引用。</summary>
    protected override void OnDetaching()
    {
        AssociatedObject.PreviewMouseRightButtonDown -= SelectRow;
        base.OnDetaching();
    }
}
