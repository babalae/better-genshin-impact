using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Xaml.Behaviors;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Linq;
using BetterGenshinImpact.ViewModel.Controls;

namespace BetterGenshinImpact.View.Behavior;

/// <summary>处理右键选行、双击详情及置顶优先的列排序，保留其他菜单和键盘操作。</summary>
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
        AssociatedObject.Sorting += SortRows;
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

    /// <summary>置顶始终优先；组内沿用列排序及 Shift 多列排序，状态排序保留有灯优先规则。</summary>
    private void SortRows(object sender, DataGridSortingEventArgs e)
    {
        // 加载时保留表头及排序箭头，避免对尚未完整建立的行集合重复排序。
        if (AssociatedObject.DataContext is AccountRecordsInventoryViewModel { IsLoading: true })
        {
            e.Handled = true;
            return;
        }
        var member = e.Column.SortMemberPath;
        if (string.IsNullOrEmpty(member)) return;
        e.Handled = true;
        var direction = e.Column.SortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending : ListSortDirection.Ascending;
        var view = CollectionViewSource.GetDefaultView(AssociatedObject.ItemsSource);
        foreach (var row in view.SourceCollection.OfType<AccountRecordsInventoryRow>()) row.CaptureSortValues();
        var path = "SortValues." + member;
        var append = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var sorts = append ? view.SortDescriptions.Where(sort => sort.PropertyName != "SortValues.Prior").ToList() : [];
        var index = sorts.FindIndex(sort => sort.PropertyName == path);
        if (index >= 0) sorts[index] = new SortDescription(path, direction);
        else
        {
            if (member == nameof(AccountRecordsInventoryRow.StateRank))
                sorts.Add(new SortDescription("SortValues.HasStatusLight", ListSortDirection.Descending));
            if (member == nameof(AccountRecordsInventoryRow.EarliestExpiration))
                sorts.Add(new SortDescription("SortValues.HasExpiration", ListSortDirection.Descending));
            sorts.Add(new SortDescription(path, direction));
        }
        if (member == nameof(AccountRecordsInventoryRow.StateRank) && !sorts.Any(sort => sort.PropertyName == "SortValues.RecordedAt"))
            sorts.Add(new SortDescription("SortValues.RecordedAt", ListSortDirection.Descending));
        using (view.DeferRefresh())
        {
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription("SortValues.Prior", ListSortDirection.Descending));
            foreach (var sort in sorts) view.SortDescriptions.Add(sort);
        }
        if (!append) foreach (var column in AssociatedObject.Columns) column.SortDirection = null;
        e.Column.SortDirection = direction;
    }

    /// <summary>解除事件引用。</summary>
    protected override void OnDetaching()
    {
        AssociatedObject.PreviewMouseRightButtonDown -= SelectRow;
        AssociatedObject.Sorting -= SortRows;
        AssociatedObject.MouseDoubleClick -= OpenDetails;
        base.OnDetaching();
    }
}
