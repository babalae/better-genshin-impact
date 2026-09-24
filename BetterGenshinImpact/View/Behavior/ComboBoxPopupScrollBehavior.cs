using Microsoft.Xaml.Behaviors;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace BetterGenshinImpact.View.Behavior;

/// <summary>
/// Handles mouse-wheel scrolling inside a ComboBox popup, which has a separate visual tree.
/// </summary>
public sealed class ComboBoxPopupScrollBehavior : Behavior<ComboBox>
{
    private const double ScrollSensitivity = 0.27;
    private ScrollViewer? _scrollViewer;

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.DropDownOpened += OnDropDownOpened;
        AssociatedObject.DropDownClosed += OnDropDownClosed;
        AssociatedObject.Unloaded += OnUnloaded;
    }

    protected override void OnDetaching()
    {
        AssociatedObject.DropDownOpened -= OnDropDownOpened;
        AssociatedObject.DropDownClosed -= OnDropDownClosed;
        AssociatedObject.Unloaded -= OnUnloaded;
        DetachScrollViewer();
        base.OnDetaching();
    }

    private void OnDropDownOpened(object? sender, EventArgs e)
    {
        AssociatedObject.Dispatcher.BeginInvoke(AttachScrollViewer, DispatcherPriority.Loaded);
    }

    private void OnDropDownClosed(object? sender, EventArgs e)
    {
        DetachScrollViewer();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachScrollViewer();
    }

    private void AttachScrollViewer()
    {
        DetachScrollViewer();
        if (!AssociatedObject.IsDropDownOpen)
        {
            return;
        }

        AssociatedObject.ApplyTemplate();
        if (AssociatedObject.Template.FindName("PART_Popup", AssociatedObject) is not Popup { Child: { } popupChild })
        {
            return;
        }

        _scrollViewer = FindVisualChild<ScrollViewer>(popupChild);
        _scrollViewer?.AddHandler(
            UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnPreviewMouseWheel),
            true);
    }

    private void DetachScrollViewer()
    {
        _scrollViewer?.RemoveHandler(
            UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnPreviewMouseWheel));
        _scrollViewer = null;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer || scrollViewer.ScrollableHeight <= 0)
        {
            return;
        }

        var targetOffset = Math.Clamp(
            scrollViewer.VerticalOffset - e.Delta * ScrollSensitivity,
            0,
            scrollViewer.ScrollableHeight);
        scrollViewer.ScrollToVerticalOffset(targetOffset);
        e.Handled = true;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result)
            {
                return result;
            }

            if (FindVisualChild<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }
}
