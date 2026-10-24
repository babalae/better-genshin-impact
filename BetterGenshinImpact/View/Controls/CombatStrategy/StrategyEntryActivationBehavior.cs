using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BetterGenshinImpact.ViewModel.Controls.CombatStrategy;
using Microsoft.Xaml.Behaviors;

namespace BetterGenshinImpact.View.Controls.CombatStrategy;

/// <summary>单击列表行时选择或进入，键盘移动不直接改写策略；选中行自动滚动到可见区域。</summary>
public sealed class StrategyEntryActivationBehavior : Behavior<ListBox>
{
    /// <summary>挂接点击和选中变化。</summary>
    protected override void OnAttached()
    {
        AssociatedObject.PreviewMouseLeftButtonUp += OnMouseUp;
        AssociatedObject.SelectionChanged += OnSelectionChanged;
        AssociatedObject.Loaded += OnLoaded;
        AssociatedObject.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged));
    }
    /// <summary>成对清理列表事件。</summary>
    protected override void OnDetaching()
    {
        AssociatedObject.PreviewMouseLeftButtonUp -= OnMouseUp;
        AssociatedObject.SelectionChanged -= OnSelectionChanged;
        AssociatedObject.Loaded -= OnLoaded;
        AssociatedObject.RemoveHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged));
    }
    /// <summary>列表首次生成时滚动到当前策略。</summary>
    private void OnLoaded(object sender, RoutedEventArgs e) => ScrollCurrent();
    /// <summary>条目重建或键盘移动时滚动到当前行。</summary>
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => ScrollCurrent();
    /// <summary>列表按行滚动时，为可见条目补充元数据；不在 UI 层读取文件。</summary>
    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (AssociatedObject.DataContext is StrategyBrowserViewModel browser
            && (e.VerticalChange != 0 || e.ViewportHeightChange != 0) && e.ViewportHeight > 0)
            browser.RequestMetadata((int)Math.Floor(e.VerticalOffset), (int)Math.Ceiling(e.ViewportHeight) + 1);
    }
    /// <summary>让当前行保持可见。</summary>
    private void ScrollCurrent()
    {
        if (AssociatedObject.SelectedItem is { } item) AssociatedObject.ScrollIntoView(item);
    }
    /// <summary>只响应行内容，滚动条和空白区域的点击不会选择。</summary>
    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        while (source != null && source is not ListBoxItem && source != AssociatedObject)
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        if (source is not ListBoxItem item) return;
        if (AssociatedObject.DataContext is StrategyBrowserViewModel browser) browser.EnterCommand.Execute(item.DataContext as StrategyEntryViewModel);
        else if (AssociatedObject.DataContext is AutoMatchCategoryViewModel auto) auto.SelectCommand.Execute(item.DataContext as AutoMatchScopeViewModel);
        e.Handled = true;
    }
}
