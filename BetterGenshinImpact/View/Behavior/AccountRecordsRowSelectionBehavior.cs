using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Xaml.Behaviors;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace BetterGenshinImpact.View.Behavior;

/// <summary>右键菜单打开前先选中对应行，保留系统菜单和键盘操作。</summary>
public sealed class AccountRecordsRowSelectionBehavior : Behavior<DataGrid>
{
    /// <summary>双击物品行时打开只读详情。</summary>
    public static readonly DependencyProperty OpenDetailsCommandProperty = DependencyProperty.Register(
        nameof(OpenDetailsCommand), typeof(ICommand), typeof(AccountRecordsRowSelectionBehavior));

    /// <summary>接收被双击行对应的物品。</summary>
    public ICommand? OpenDetailsCommand
    {
        get => (ICommand?)GetValue(OpenDetailsCommandProperty);
        set => SetValue(OpenDetailsCommandProperty, value);
    }

    /// <summary>监听预览事件，不阻止菜单继续打开。</summary>
    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.PreviewMouseRightButtonDown += SelectRow;
        AssociatedObject.Sorting += SortStatus;
        AssociatedObject.MouseDoubleClick += OpenDetails;
    }

    /// <summary>只处理左键双击数据行，按钮保留自身交互。</summary>
    private void OpenDetails(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject source) return;
        if (ItemsControl.ContainerFromElement(AssociatedObject, source) is not DataGridRow row) return;
        for (var current = source; current != null && current != row;)
        {
            if (current is ButtonBase) return;
            current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }
        if (OpenDetailsCommand?.CanExecute(row.Item) != true) return;
        AssociatedObject.SelectedItem = row.Item;
        e.Handled = true;
        OpenDetailsCommand.Execute(row.Item);
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

    /// <summary>状态排序始终将有灯物品置前，点击切换灯色顺序；同级按记录时间倒序。</summary>
    private void SortStatus(object sender, DataGridSortingEventArgs e)
    {
        if (e.Column.SortMemberPath != "StateRank") return;
        e.Handled = true;
        var direction = e.Column.SortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending : ListSortDirection.Ascending;
        var view = CollectionViewSource.GetDefaultView(AssociatedObject.ItemsSource);
        using (view.DeferRefresh())
        {
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription("HasStatusLight", ListSortDirection.Descending));
            view.SortDescriptions.Add(new SortDescription("StateRank", direction));
            view.SortDescriptions.Add(new SortDescription("RecordedAt", ListSortDirection.Descending));
        }
        foreach (var column in AssociatedObject.Columns) column.SortDirection = null;
        e.Column.SortDirection = direction;
    }

    /// <summary>解除事件引用。</summary>
    protected override void OnDetaching()
    {
        AssociatedObject.PreviewMouseRightButtonDown -= SelectRow;
        AssociatedObject.Sorting -= SortStatus;
        AssociatedObject.MouseDoubleClick -= OpenDetails;
        base.OnDetaching();
    }
}
