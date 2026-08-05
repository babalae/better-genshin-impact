using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Xaml.Behaviors;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Behavior;

public sealed class ResponsiveTitleBarBehavior : Behavior<Panel>
{
    public static readonly DependencyProperty CollapseOrderProperty =
        DependencyProperty.RegisterAttached(
            "CollapseOrder",
            typeof(int),
            typeof(ResponsiveTitleBarBehavior),
            new PropertyMetadata(0));

    public static readonly DependencyProperty MinimumGapProperty =
        DependencyProperty.Register(
            nameof(MinimumGap),
            typeof(double),
            typeof(ResponsiveTitleBarBehavior),
            new PropertyMetadata(8d));

    private readonly Dictionary<FrameworkElement, double> _elementWidths = new();
    private Window? _window;
    private TitleBar? _titleBar;
    private double? _rightReservedWidth;
    private bool _updatePending;

    public double MinimumGap
    {
        get => (double)GetValue(MinimumGapProperty);
        set => SetValue(MinimumGapProperty, value);
    }

    public static int GetCollapseOrder(DependencyObject obj)
    {
        return (int)obj.GetValue(CollapseOrderProperty);
    }

    public static void SetCollapseOrder(DependencyObject obj, int value)
    {
        obj.SetValue(CollapseOrderProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.Loaded += OnAssociatedObjectLoaded;
        AssociatedObject.Unloaded += OnAssociatedObjectUnloaded;

        if (AssociatedObject.IsLoaded)
        {
            Attach();
        }
    }

    protected override void OnDetaching()
    {
        AssociatedObject.Loaded -= OnAssociatedObjectLoaded;
        AssociatedObject.Unloaded -= OnAssociatedObjectUnloaded;
        Detach();
        base.OnDetaching();
    }

    private void OnAssociatedObjectLoaded(object sender, RoutedEventArgs e)
    {
        Attach();
    }

    private void OnAssociatedObjectUnloaded(object sender, RoutedEventArgs e)
    {
        Detach();
    }

    private void Attach()
    {
        if (_window is not null)
        {
            return;
        }

        _window = Window.GetWindow(AssociatedObject);
        _titleBar = FindVisualAncestor<TitleBar>(AssociatedObject);
        if (_window is null || _titleBar is null)
        {
            _window = null;
            _titleBar = null;
            return;
        }

        CacheElementWidths();
        _window.SizeChanged += OnWindowSizeChanged;
        _window.Closed += OnWindowClosed;
        ScheduleUpdate();
    }

    private void Detach()
    {
        if (_window is not null)
        {
            _window.SizeChanged -= OnWindowSizeChanged;
            _window.Closed -= OnWindowClosed;
        }

        _window = null;
        _titleBar = null;
        _rightReservedWidth = null;
        _elementWidths.Clear();
        _updatePending = false;
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ScheduleUpdate();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        Detach();
    }

    private void ScheduleUpdate()
    {
        if (_updatePending)
        {
            return;
        }

        _updatePending = true;
        _ = AssociatedObject.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() =>
            {
                _updatePending = false;
                UpdateChildVisibility();
            }));
    }

    private void CacheElementWidths()
    {
        foreach (var element in AssociatedObject.Children.OfType<FrameworkElement>())
        {
            var width = double.IsNaN(element.Width)
                ? element.DesiredSize.Width
                : element.Width;
            _elementWidths[element] = Math.Max(0, width + element.Margin.Left + element.Margin.Right);
        }
    }

    private void UpdateChildVisibility()
    {
        if (_titleBar is null || !_titleBar.IsVisible)
        {
            return;
        }

        var collapsibleElements = AssociatedObject.Children
            .OfType<FrameworkElement>()
            .Where(element => GetCollapseOrder(element) > 0)
            .OrderBy(GetCollapseOrder)
            .ToArray();
        if (collapsibleElements.Length == 0)
        {
            return;
        }

        CacheMissingElementWidths();
        var availableWidth = CalculateAvailableWidth();
        var requiredWidth = AssociatedObject.Children
            .OfType<FrameworkElement>()
            .Where(element =>
                GetCollapseOrder(element) > 0 || element.Visibility != Visibility.Collapsed)
            .Sum(GetCachedWidth);
        var collapsedElements = new HashSet<FrameworkElement>();

        foreach (var element in collapsibleElements)
        {
            if (requiredWidth <= availableWidth)
            {
                break;
            }

            collapsedElements.Add(element);
            requiredWidth -= GetCachedWidth(element);
        }

        foreach (var element in collapsibleElements)
        {
            element.SetCurrentValue(
                UIElement.VisibilityProperty,
                collapsedElements.Contains(element) ? Visibility.Collapsed : Visibility.Visible);
        }
    }

    private void CacheMissingElementWidths()
    {
        foreach (var element in AssociatedObject.Children.OfType<FrameworkElement>())
        {
            if (_elementWidths.ContainsKey(element))
            {
                continue;
            }

            var width = double.IsNaN(element.Width)
                ? element.DesiredSize.Width
                : element.Width;
            _elementWidths[element] = Math.Max(0, width + element.Margin.Left + element.Margin.Right);
        }
    }

    private double CalculateAvailableWidth()
    {
        if (_titleBar is null)
        {
            return 0;
        }

        var panelRight = AssociatedObject
            .TranslatePoint(new Point(AssociatedObject.ActualWidth, 0), _titleBar)
            .X;
        var rightReservedWidth = _titleBar.ActualWidth - panelRight;
        if (!_rightReservedWidth.HasValue && rightReservedWidth >= 0)
        {
            _rightReservedWidth = rightReservedWidth;
        }

        var headerRight = _titleBar.Header is FrameworkElement header
            ? header.TranslatePoint(new Point(header.ActualWidth, 0), _titleBar).X
            : 0;
        return Math.Max(
            0,
            _titleBar.ActualWidth -
            Math.Max(0, _rightReservedWidth ?? rightReservedWidth) -
            headerRight -
            Math.Max(0, MinimumGap));
    }

    private double GetCachedWidth(FrameworkElement element)
    {
        return _elementWidths.GetValueOrDefault(element);
    }

    private static T? FindVisualAncestor<T>(DependencyObject child) where T : DependencyObject
    {
        for (var current = VisualTreeHelper.GetParent(child);
             current is not null;
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is T ancestor)
            {
                return ancestor;
            }
        }

        return null;
    }
}
