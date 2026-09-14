using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.View.Behavior;

public static class DomainCascadingComboBoxBehavior
{
    private static readonly DependencyPropertyDescriptor SelectedCascadingItemDescriptor =
        DependencyPropertyDescriptor.FromProperty(CascadingComboBox.SelectedCascadingItemProperty, typeof(CascadingComboBox));

    private static readonly DependencyProperty LastCommittedItemProperty =
        DependencyProperty.RegisterAttached(
            "LastCommittedItem",
            typeof(ICascadingItem),
            typeof(DomainCascadingComboBoxBehavior),
            new PropertyMetadata(null));

    private static readonly DependencyProperty LastCommittedTextProperty =
        DependencyProperty.RegisterAttached(
            "LastCommittedText",
            typeof(string),
            typeof(DomainCascadingComboBoxBehavior),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ClearValueProperty =
        DependencyProperty.RegisterAttached(
            "ClearValue",
            typeof(string),
            typeof(DomainCascadingComboBoxBehavior),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty UseFullLabelsWhenOpenProperty =
        DependencyProperty.RegisterAttached(
            "UseFullLabelsWhenOpen",
            typeof(bool),
            typeof(DomainCascadingComboBoxBehavior),
            new PropertyMetadata(false, OnUseFullLabelsWhenOpenChanged));

    public static bool GetUseFullLabelsWhenOpen(DependencyObject obj)
    {
        return (bool)obj.GetValue(UseFullLabelsWhenOpenProperty);
    }

    public static void SetUseFullLabelsWhenOpen(DependencyObject obj, bool value)
    {
        obj.SetValue(UseFullLabelsWhenOpenProperty, value);
    }

    public static string GetClearValue(DependencyObject obj)
    {
        return (string)obj.GetValue(ClearValueProperty);
    }

    public static void SetClearValue(DependencyObject obj, string value)
    {
        obj.SetValue(ClearValueProperty, value);
    }

    private static void OnUseFullLabelsWhenOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not CascadingComboBox comboBox)
            return;

        comboBox.DropDownClosed -= OnDropDownClosed;
        comboBox.DropDownOpened -= OnDropDownOpened;
        comboBox.SelectionChanged -= OnSelectionChanged;
        comboBox.Loaded -= OnLoaded;
        comboBox.Loaded -= OnLoadedForBindingSync;
        comboBox.Unloaded -= OnUnloadedForBindingSync;
        comboBox.RemoveHandler(Button.ClickEvent, new RoutedEventHandler(OnButtonClick));
        SyncSelectedCascadingItemListener(comboBox, attach: false);

        if (e.NewValue is true)
        {
            comboBox.DropDownClosed += OnDropDownClosed;
            comboBox.DropDownOpened += OnDropDownOpened;
            comboBox.SelectionChanged += OnSelectionChanged;
            comboBox.Loaded += OnLoaded;
            comboBox.AddHandler(Button.ClickEvent, new RoutedEventHandler(OnButtonClick), true);
            // 程序化修改 SelectedCascadingItem（例如切换一条龙预设导致绑定值变化）不会触发交互事件，
            // 这里额外监听依赖属性变化，使折叠态展示文案随绑定值同步刷新（#3235）。
            // 订阅挂在 Loaded/Unloaded 生命周期上配对移除，避免 Descriptor 的强引用把已卸载元素钉死。
            comboBox.Loaded += OnLoadedForBindingSync;
            comboBox.Unloaded += OnUnloadedForBindingSync;
            SyncSelectedCascadingItemListener(comboBox, attach: true);
            comboBox.Dispatcher.BeginInvoke(() => UpdateCommittedSelection(comboBox, restoreInvalidSelection: false));
        }
    }

    private static void OnLoadedForBindingSync(object sender, RoutedEventArgs e)
    {
        if (sender is CascadingComboBox comboBox)
        {
            SyncSelectedCascadingItemListener(comboBox, attach: true);
        }
    }

    private static void OnUnloadedForBindingSync(object sender, RoutedEventArgs e)
    {
        if (sender is CascadingComboBox comboBox)
        {
            SyncSelectedCascadingItemListener(comboBox, attach: false);
        }
    }

    /// <summary>
    /// 幂等挂接/摘除 SelectedCascadingItem 的值变化监听（先移除再挂接，避免重复订阅）。
    /// </summary>
    private static void SyncSelectedCascadingItemListener(CascadingComboBox comboBox, bool attach)
    {
        SelectedCascadingItemDescriptor.RemoveValueChanged(comboBox, OnSelectedCascadingItemChanged);
        if (attach)
        {
            SelectedCascadingItemDescriptor.AddValueChanged(comboBox, OnSelectedCascadingItemChanged);
        }
    }

    private static void OnSelectedCascadingItemChanged(object? sender, EventArgs e)
    {
        if (sender is not CascadingComboBox comboBox)
        {
            return;
        }

        comboBox.Dispatcher.BeginInvoke(() =>
        {
            // 绑定/程序化驱动（如切换一条龙预设）的值变化：直接以当前绑定值为准提交。
            // 值为空或无法解析时清空提交状态：既不残留上一预设的旧文案，
            // 也防止 DropDownClosed 的恢复逻辑把上一预设的值回写进新预设（#3235 自审）。
            var item = comboBox.SelectedCascadingItem;
            if (IsSelectableDomain(item))
            {
                CommitSelectedItem(comboBox, item);
            }
            else
            {
                comboBox.SetValue(LastCommittedItemProperty, null);
                comboBox.SetValue(LastCommittedTextProperty, comboBox.PlaceholderText);
            }

            ApplySelectedPreviewBinding(comboBox);
        });
    }

    private static void OnButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not CascadingComboBox comboBox ||
            e.OriginalSource is not DependencyObject source ||
            FindVisualAncestor<Button>(source) is not { Name: CascadingComboBox.PART_ClearButton, TemplatedParent: CascadingComboBox owner } ||
            !ReferenceEquals(owner, comboBox))
        {
            return;
        }

        comboBox.SetValue(LastCommittedItemProperty, null);
        comboBox.SetValue(LastCommittedTextProperty, comboBox.PlaceholderText);
        comboBox.SetCurrentValue(ClearValueProperty, string.Empty);
        comboBox.GetBindingExpression(ClearValueProperty)?.UpdateSource();
        ApplySelectedPreviewBinding(comboBox);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is CascadingComboBox comboBox)
            comboBox.Dispatcher.BeginInvoke(() => UpdateCommittedSelection(comboBox, restoreInvalidSelection: false));
    }

    private static void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is CascadingComboBox comboBox)
            comboBox.Dispatcher.BeginInvoke(() => UpdateCommittedSelection(comboBox, restoreInvalidSelection: false));
    }

    private static void OnDropDownOpened(object? sender, System.EventArgs e)
    {
        if (sender is CascadingComboBox comboBox)
            comboBox.Dispatcher.BeginInvoke(() => UpdateCommittedSelection(comboBox, restoreInvalidSelection: false));
    }

    private static void OnDropDownClosed(object? sender, System.EventArgs e)
    {
        if (sender is CascadingComboBox comboBox)
            comboBox.Dispatcher.BeginInvoke(() => UpdateCommittedSelection(comboBox, restoreInvalidSelection: true));
    }

    private static void UpdateCommittedSelection(CascadingComboBox comboBox, bool restoreInvalidSelection)
    {
        if (IsSelectableDomain(comboBox.SelectedCascadingItem))
            CommitSelectedItem(comboBox, comboBox.SelectedCascadingItem);
        else if (GetLastCommittedItem(comboBox) == null)
            comboBox.SetValue(LastCommittedTextProperty, comboBox.PlaceholderText);
        else if (restoreInvalidSelection && GetLastCommittedItem(comboBox) is { } lastCommittedItem)
            comboBox.SetCurrentValue(CascadingComboBox.SelectedCascadingItemProperty, lastCommittedItem);

        ApplySelectedPreviewBinding(comboBox);
    }

    private static bool IsSelectableDomain(ICascadingItem? item)
    {
        return item?.Tag is string name && !string.IsNullOrWhiteSpace(name);
    }

    private static ICascadingItem? GetLastCommittedItem(DependencyObject obj)
    {
        return (ICascadingItem?)obj.GetValue(LastCommittedItemProperty);
    }

    private static void CommitSelectedItem(CascadingComboBox comboBox, ICascadingItem? item)
    {
        if (!IsSelectableDomain(item))
            return;

        comboBox.SetValue(LastCommittedItemProperty, item);
        comboBox.SetValue(LastCommittedTextProperty, item!.Tag as string ?? string.Empty);
    }

    private static void ApplySelectedPreviewBinding(CascadingComboBox comboBox)
    {
        comboBox.ApplyTemplate();

        BindPreviewText(comboBox, "PART_SelectedText");
        BindPreviewText(comboBox, "PART_SelectedValueText");
    }

    private static void BindPreviewText(CascadingComboBox comboBox, string partName)
    {
        if (FindVisualChild<TextBlock>(comboBox, partName) is not { } selectedText)
            return;

        BindingOperations.SetBinding(selectedText, TextBlock.TextProperty, new Binding("SelectedCascadingItem.Tag")
        {
            Source = comboBox,
            Mode = BindingMode.OneWay,
            Path = new PropertyPath("(0)", LastCommittedTextProperty),
            TargetNullValue = comboBox.PlaceholderText,
            FallbackValue = comboBox.PlaceholderText
        });
    }

    private static T? FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T element && element.Name == name)
                return element;

            var result = FindVisualChild<T>(child, name);
            if (result != null)
                return result;
        }

        return null;
    }

    private static T? FindVisualAncestor<T>(DependencyObject child) where T : DependencyObject
    {
        for (var current = child; current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T ancestor)
                return ancestor;
        }

        return null;
    }
}
