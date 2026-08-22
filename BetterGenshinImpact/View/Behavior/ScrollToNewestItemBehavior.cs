using Microsoft.Xaml.Behaviors;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace BetterGenshinImpact.View.Behavior;

/// <summary>
/// 当 ItemsControl 增加内容时滚动到最后一项。
/// </summary>
public sealed class ScrollToNewestItemBehavior : Behavior<ListBox>
{
    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.ItemContainerGenerator.ItemsChanged += OnItemsChanged;
        AssociatedObject.Loaded += OnLoaded;
    }

    protected override void OnDetaching()
    {
        AssociatedObject.ItemContainerGenerator.ItemsChanged -= OnItemsChanged;
        AssociatedObject.Loaded -= OnLoaded;
        base.OnDetaching();
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e) => ScrollToLast();

    private void OnItemsChanged(object? sender, ItemsChangedEventArgs e) => ScrollToLast();

    private void ScrollToLast()
    {
        _ = AssociatedObject.Dispatcher.BeginInvoke(() =>
        {
            if (AssociatedObject.Items.Count > 0)
                AssociatedObject.ScrollIntoView(AssociatedObject.Items[AssociatedObject.Items.Count - 1]);
        }, DispatcherPriority.Background);
    }
}