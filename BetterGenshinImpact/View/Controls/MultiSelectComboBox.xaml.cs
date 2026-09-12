using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace BetterGenshinImpact.View.Controls;

/// <summary>
/// 支持搜索、批量操作及双向集合绑定的下拉多选控件。
/// </summary>
public partial class MultiSelectComboBox : UserControl
{
    private readonly ObservableCollection<MultiSelectItem> _items = [];
    private readonly List<ScrollViewer> _trackedScrollViewers = [];
    private bool _isSynchronizingSelection;
    private bool _isPopupRepositionPending;
    private Window? _trackedWindow;

    public MultiSelectComboBox()
    {
        FilteredItems = CollectionViewSource.GetDefaultView(_items);
        FilteredItems.Filter = FilterItem;
        if (FilteredItems is ListCollectionView listCollectionView)
        {
            listCollectionView.CustomSort = MultiSelectItemComparer.Instance;
        }

        InitializeComponent();
        IsEnabledChanged += OnIsEnabledChanged;
        Unloaded += OnUnloaded;
    }

    public ICollectionView FilteredItems { get; }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(MultiSelectComboBox),
        new PropertyMetadata(null, OnItemsSourceChanged));

    public IList? SelectedItems
    {
        get => (IList?)GetValue(SelectedItemsProperty);
        set => SetValue(SelectedItemsProperty, value);
    }

    public static readonly DependencyProperty SelectedItemsProperty = DependencyProperty.Register(
        nameof(SelectedItems),
        typeof(IList),
        typeof(MultiSelectComboBox),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnSelectedItemsChanged));

    public bool IsDropDownOpen
    {
        get => (bool)GetValue(IsDropDownOpenProperty);
        set => SetValue(IsDropDownOpenProperty, value);
    }

    public static readonly DependencyProperty IsDropDownOpenProperty = DependencyProperty.Register(
        nameof(IsDropDownOpen),
        typeof(bool),
        typeof(MultiSelectComboBox),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public double MaxDropDownHeight
    {
        get => (double)GetValue(MaxDropDownHeightProperty);
        set => SetValue(MaxDropDownHeightProperty, value);
    }

    public static readonly DependencyProperty MaxDropDownHeightProperty = DependencyProperty.Register(
        nameof(MaxDropDownHeight),
        typeof(double),
        typeof(MultiSelectComboBox),
        new PropertyMetadata(300d));

    public string PlaceholderText
    {
        get => (string)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
        nameof(PlaceholderText),
        typeof(string),
        typeof(MultiSelectComboBox),
        new PropertyMetadata("请选择"));

    public string SearchPlaceholderText
    {
        get => (string)GetValue(SearchPlaceholderTextProperty);
        set => SetValue(SearchPlaceholderTextProperty, value);
    }

    public static readonly DependencyProperty SearchPlaceholderTextProperty = DependencyProperty.Register(
        nameof(SearchPlaceholderText),
        typeof(string),
        typeof(MultiSelectComboBox),
        new PropertyMetadata("搜索选项"));

    public string SelectionText
    {
        get => (string)GetValue(SelectionTextProperty);
        private set => SetValue(SelectionTextPropertyKey, value);
    }

    private static readonly DependencyPropertyKey SelectionTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(SelectionText),
        typeof(string),
        typeof(MultiSelectComboBox),
        new PropertyMetadata("请选择"));

    public static readonly DependencyProperty SelectionTextProperty = SelectionTextPropertyKey.DependencyProperty;

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (MultiSelectComboBox)d;

        if (e.OldValue is INotifyCollectionChanged oldCollection)
        {
            CollectionChangedEventManager.RemoveHandler(oldCollection, control.OnItemsSourceCollectionChanged);
        }

        if (e.NewValue is INotifyCollectionChanged newCollection)
        {
            CollectionChangedEventManager.AddHandler(newCollection, control.OnItemsSourceCollectionChanged);
        }

        control.RebuildItems();
    }

    private static void OnSelectedItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (MultiSelectComboBox)d;

        if (e.OldValue is INotifyCollectionChanged oldCollection)
        {
            CollectionChangedEventManager.RemoveHandler(oldCollection, control.OnSelectedItemsCollectionChanged);
        }

        if (e.NewValue is INotifyCollectionChanged newCollection)
        {
            CollectionChangedEventManager.AddHandler(newCollection, control.OnSelectedItemsCollectionChanged);
        }

        control.SynchronizeSelectionFromSource();
    }

    private void OnItemsSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildItems();
    }

    private void OnSelectedItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_isSynchronizingSelection)
        {
            SynchronizeSelectionFromSource();
        }
    }

    private void RebuildItems()
    {
        _items.Clear();

        if (ItemsSource is not null)
        {
            int sourceIndex = 0;
            foreach (var item in ItemsSource)
            {
                if (item is not null)
                {
                    _items.Add(new MultiSelectItem(item, sourceIndex));
                }

                sourceIndex++;
            }
        }

        SynchronizeSelectionFromSource();
        RefreshFilter();
    }

    private void SynchronizeSelectionFromSource()
    {
        _isSynchronizingSelection = true;
        try
        {
            foreach (var item in _items)
            {
                item.IsSelected = ContainsSelectedItem(item.Value);
            }
        }
        finally
        {
            _isSynchronizingSelection = false;
        }

        UpdateSelectionSummary();
    }

    private bool ContainsSelectedItem(object value)
    {
        return SelectedItems?.Contains(value) == true;
    }

    private void SetItemSelection(MultiSelectItem item, bool isSelected)
    {
        if (SelectedItems is null || SelectedItems.IsReadOnly || SelectedItems.IsFixedSize)
        {
            SynchronizeSelectionFromSource();
            return;
        }

        _isSynchronizingSelection = true;
        try
        {
            if (isSelected && !ContainsSelectedItem(item.Value))
            {
                SelectedItems.Add(item.Value);
            }
            else if (!isSelected && ContainsSelectedItem(item.Value))
            {
                SelectedItems.Remove(item.Value);
            }
        }
        finally
        {
            _isSynchronizingSelection = false;
        }

        SynchronizeSelectionFromSource();
    }

    private void SelectAllFilteredItems()
    {
        if (SelectedItems is null || SelectedItems.IsReadOnly || SelectedItems.IsFixedSize)
        {
            return;
        }

        _isSynchronizingSelection = true;
        try
        {
            foreach (var item in FilteredItems.Cast<MultiSelectItem>())
            {
                if (!ContainsSelectedItem(item.Value))
                {
                    SelectedItems.Add(item.Value);
                }
            }
        }
        finally
        {
            _isSynchronizingSelection = false;
        }

        SynchronizeSelectionFromSource();
    }

    private void ClearSelection()
    {
        if (SelectedItems is null || SelectedItems.IsReadOnly || SelectedItems.IsFixedSize)
        {
            return;
        }

        _isSynchronizingSelection = true;
        try
        {
            SelectedItems.Clear();
        }
        finally
        {
            _isSynchronizingSelection = false;
        }

        SynchronizeSelectionFromSource();
    }

    private void UpdateSelectionSummary()
    {
        var selectedTexts = SelectedItems?
            .Cast<object>()
            .Select(GetDisplayText)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList() ?? [];

        SelectionText = selectedTexts.Count == 0
            ? PlaceholderText
            : string.Join("，", selectedTexts);

        if (SelectionCountTextBlock is not null)
        {
            SelectionCountTextBlock.Text = $"已选 {selectedTexts.Count} 项";
        }
    }

    private bool FilterItem(object item)
    {
        if (item is not MultiSelectItem option || string.IsNullOrWhiteSpace(SearchTextBox?.Text))
        {
            return true;
        }

        return option.DisplayText.Contains(SearchTextBox.Text.Trim(), StringComparison.CurrentCultureIgnoreCase);
    }

    private void RefreshFilter()
    {
        FilteredItems.Refresh();

        if (NoResultsTextBlock is not null)
        {
            NoResultsTextBlock.Visibility = FilteredItems.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void ItemCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: MultiSelectItem item } checkBox)
        {
            SetItemSelection(item, checkBox.IsChecked == true);
        }
    }

    private void SelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        SelectAllFilteredItems();
    }

    private void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        ClearSelection();
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshFilter();
    }

    private void SearchTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            FocusFirstFilteredItem();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && FilteredItems.Cast<MultiSelectItem>().FirstOrDefault() is { } item)
        {
            SetItemSelection(item, !ContainsSelectedItem(item.Value));
            e.Handled = true;
        }
    }

    private void DropDownToggle_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            SetCurrentValue(IsDropDownOpenProperty, true);
            e.Handled = true;
        }
    }

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && IsDropDownOpen)
        {
            SetCurrentValue(IsDropDownOpenProperty, false);
            DropDownToggle.Focus();
            e.Handled = true;
        }
    }

    private void DropDownPopup_Opened(object? sender, EventArgs e)
    {
        CaptureSelectionOrder();
        AttachPopupPositionTracking();
        QueuePopupReposition();
        SearchTextBox.Focus();
        SearchTextBox.SelectAll();
    }

    private void CaptureSelectionOrder()
    {
        foreach (var item in _items)
        {
            item.IsSelectedForSort = item.IsSelected;
        }

        RefreshFilter();
    }

    private void DropDownPopup_Closed(object? sender, EventArgs e)
    {
        DetachPopupPositionTracking();
        SetCurrentValue(IsDropDownOpenProperty, false);
        SearchTextBox.Clear();
    }

    /// <summary>
    /// Popup 使用独立窗口承载，祖先 ScrollViewer 滚动时需要主动刷新其位置。
    /// </summary>
    private void AttachPopupPositionTracking()
    {
        DetachPopupPositionTracking();

        DependencyObject? ancestor = VisualTreeHelper.GetParent(this);
        while (ancestor is not null)
        {
            if (ancestor is ScrollViewer scrollViewer)
            {
                scrollViewer.ScrollChanged += AncestorScrollViewer_ScrollChanged;
                _trackedScrollViewers.Add(scrollViewer);
            }

            ancestor = VisualTreeHelper.GetParent(ancestor);
        }

        _trackedWindow = Window.GetWindow(this);
        if (_trackedWindow is not null)
        {
            _trackedWindow.LocationChanged += TrackedWindow_LocationChanged;
            _trackedWindow.SizeChanged += TrackedWindow_SizeChanged;
        }
    }

    private void DetachPopupPositionTracking()
    {
        foreach (var scrollViewer in _trackedScrollViewers)
        {
            scrollViewer.ScrollChanged -= AncestorScrollViewer_ScrollChanged;
        }

        _trackedScrollViewers.Clear();

        if (_trackedWindow is not null)
        {
            _trackedWindow.LocationChanged -= TrackedWindow_LocationChanged;
            _trackedWindow.SizeChanged -= TrackedWindow_SizeChanged;
            _trackedWindow = null;
        }
    }

    private void AncestorScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        QueuePopupReposition();
    }

    private void TrackedWindow_LocationChanged(object? sender, EventArgs e)
    {
        QueuePopupReposition();
    }

    private void TrackedWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        QueuePopupReposition();
    }

    private void QueuePopupReposition()
    {
        if (!IsDropDownOpen || _isPopupRepositionPending)
        {
            return;
        }

        _isPopupRepositionPending = true;
        Dispatcher.BeginInvoke(() =>
        {
            _isPopupRepositionPending = false;

            if (!IsDropDownOpen)
            {
                return;
            }

            if (!IsPlacementTargetVisible())
            {
                SetCurrentValue(IsDropDownOpenProperty, false);
                return;
            }

            // 轻微改变偏移量会让 WPF 重新计算 Popup 相对于 PlacementTarget 的屏幕坐标。
            double horizontalOffset = DropDownPopup.HorizontalOffset;
            DropDownPopup.SetCurrentValue(Popup.HorizontalOffsetProperty, horizontalOffset + 0.1d);
            DropDownPopup.SetCurrentValue(Popup.HorizontalOffsetProperty, horizontalOffset);
        }, DispatcherPriority.Render);
    }

    private bool IsPlacementTargetVisible()
    {
        if (!IsVisible || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return false;
        }

        foreach (var scrollViewer in _trackedScrollViewers)
        {
            try
            {
                Rect targetBounds = TransformToAncestor(scrollViewer)
                    .TransformBounds(new Rect(new Point(), RenderSize));
                Rect viewportBounds = new(new Point(), scrollViewer.RenderSize);

                if (!targetBounds.IntersectsWith(viewportBounds))
                {
                    return false;
                }
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        return true;
    }

    private void FocusFirstFilteredItem()
    {
        if (FilteredItems.Cast<MultiSelectItem>().FirstOrDefault() is not { } item)
        {
            return;
        }

        OptionsListBox.ScrollIntoView(item);
        OptionsListBox.UpdateLayout();

        if (OptionsListBox.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem container)
        {
            container.Focus();
        }
    }

    private void OnIsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is false)
        {
            SetCurrentValue(IsDropDownOpenProperty, false);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        SetCurrentValue(IsDropDownOpenProperty, false);
        DetachPopupPositionTracking();
    }

    private static string GetDisplayText(object value)
    {
        return value.ToString() ?? string.Empty;
    }

    private sealed class MultiSelectItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public MultiSelectItem(object value, int sourceIndex)
        {
            Value = value;
            SourceIndex = sourceIndex;
            DisplayText = GetDisplayText(value);
        }

        public object Value { get; }

        public int SourceIndex { get; }

        public string DisplayText { get; }

        public bool IsSelectedForSort { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    private sealed class MultiSelectItemComparer : IComparer
    {
        public static MultiSelectItemComparer Instance { get; } = new();

        public int Compare(object? x, object? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is not MultiSelectItem left)
            {
                return -1;
            }

            if (y is not MultiSelectItem right)
            {
                return 1;
            }

            int selectionComparison = right.IsSelectedForSort.CompareTo(left.IsSelectedForSort);
            return selectionComparison != 0
                ? selectionComparison
                : left.SourceIndex.CompareTo(right.SourceIndex);
        }
    }
}
