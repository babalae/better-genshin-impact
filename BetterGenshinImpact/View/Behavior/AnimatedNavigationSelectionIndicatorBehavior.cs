using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Xaml.Behaviors;
using Wpf.Ui.Controls;
using Border = System.Windows.Controls.Border;

namespace BetterGenshinImpact.View.Behavior;

/// <summary>
/// 为 NavigationView 的选中指示条添加类似 WinUI 的跨菜单项滑动动画。
/// </summary>
public sealed class AnimatedNavigationSelectionIndicatorBehavior : Behavior<NavigationView>
{
    public static readonly DependencyProperty DurationProperty = DependencyProperty.Register(
        nameof(Duration),
        typeof(TimeSpan),
        typeof(AnimatedNavigationSelectionIndicatorBehavior),
        new PropertyMetadata(TimeSpan.FromMilliseconds(280)));

    private readonly HashSet<Rectangle> _hiddenNativeIndicators = [];
    private SelectionIndicatorAdorner? _adorner;
    private ScrollViewer? _menuScrollViewer;
    private bool _updatePending;
    private bool _animatePending;
    private NavigationViewItem? _displayedItem;
    private double _targetX = double.NaN;
    private double _targetY = double.NaN;
    private double _targetHeight = double.NaN;

    public TimeSpan Duration
    {
        get => (TimeSpan)GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.Loaded += OnLoaded;
        AssociatedObject.Unloaded += OnUnloaded;

        if (AssociatedObject.IsLoaded)
        {
            AttachToNavigationView();
        }
    }

    protected override void OnDetaching()
    {
        AssociatedObject.Loaded -= OnLoaded;
        AssociatedObject.Unloaded -= OnUnloaded;
        DetachFromNavigationView();
        base.OnDetaching();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachToNavigationView();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachFromNavigationView();
    }

    private void AttachToNavigationView()
    {
        if (_adorner is not null)
        {
            return;
        }

        var adornerLayer = AdornerLayer.GetAdornerLayer(AssociatedObject);
        if (adornerLayer is null)
        {
            return;
        }

        _adorner = new SelectionIndicatorAdorner(AssociatedObject);
        adornerLayer.Add(_adorner);

        AssociatedObject.SelectionChanged += OnSelectionChanged;
        AssociatedObject.PaneOpened += OnPaneStateChanged;
        AssociatedObject.PaneClosed += OnPaneStateChanged;
        AssociatedObject.SizeChanged += OnNavigationViewSizeChanged;
        AssociatedObject.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        AssociatedObject.AddHandler(
            ButtonBase.ClickEvent,
            new RoutedEventHandler(OnNavigationItemClick),
            true);
        AssociatedObject.AddHandler(
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnDescendantLoaded),
            true);

        _menuScrollViewer = AssociatedObject.Template.FindName(
            "PART_ScrollViewer",
            AssociatedObject) as ScrollViewer;
        if (_menuScrollViewer is not null)
        {
            _menuScrollViewer.ScrollChanged += OnMenuScrollChanged;
        }

        HideNativeIndicators(AssociatedObject);
        ScheduleUpdate(false);
    }

    private void DetachFromNavigationView()
    {
        AssociatedObject.SelectionChanged -= OnSelectionChanged;
        AssociatedObject.PaneOpened -= OnPaneStateChanged;
        AssociatedObject.PaneClosed -= OnPaneStateChanged;
        AssociatedObject.SizeChanged -= OnNavigationViewSizeChanged;
        AssociatedObject.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        AssociatedObject.RemoveHandler(
            ButtonBase.ClickEvent,
            new RoutedEventHandler(OnNavigationItemClick));
        AssociatedObject.RemoveHandler(
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnDescendantLoaded));

        if (_menuScrollViewer is not null)
        {
            _menuScrollViewer.ScrollChanged -= OnMenuScrollChanged;
            _menuScrollViewer = null;
        }

        if (_adorner is not null)
        {
            AdornerLayer.GetAdornerLayer(AssociatedObject)?.Remove(_adorner);
            _adorner = null;
        }

        foreach (var indicator in _hiddenNativeIndicators)
        {
            indicator.ClearValue(UIElement.VisibilityProperty);
        }

        _hiddenNativeIndicators.Clear();
        _displayedItem = null;
        _targetX = double.NaN;
        _targetY = double.NaN;
        _targetHeight = double.NaN;
        _updatePending = false;
        _animatePending = false;
    }

    private void OnSelectionChanged(NavigationView sender, RoutedEventArgs args)
    {
        HideNativeIndicators(AssociatedObject);
        ScheduleUpdate(true);
    }

    private void OnPaneStateChanged(NavigationView sender, RoutedEventArgs args)
    {
        HideNativeIndicators(AssociatedObject);
        ScheduleUpdate(true);
    }

    private void OnNavigationViewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ScheduleUpdate(false);
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source
            && FindVisualAncestor<NavigationViewItem>(source) is not null)
        {
            // 在 WPF-UI 更新 IsActive 前隐藏原生指示条，避免它与动画指示条重叠一帧。
            HideNativeIndicators(AssociatedObject);
        }
    }

    private void OnNavigationItemClick(object sender, RoutedEventArgs e)
    {
        if (e.Source is NavigationViewItem)
        {
            ScheduleUpdate(true);
        }
    }

    private void OnDescendantLoaded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is NavigationViewItem item)
        {
            HideNativeIndicator(item);
        }
    }

    private void OnMenuScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        ScheduleUpdate(false);
    }

    private void ScheduleUpdate(bool animate)
    {
        _animatePending |= animate;
        if (_updatePending)
        {
            return;
        }

        _updatePending = true;
        _ = AssociatedObject.Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(() =>
            {
                _updatePending = false;
                var shouldAnimate = _animatePending;
                _animatePending = false;
                UpdateIndicator(shouldAnimate);
            }));
    }

    private void UpdateIndicator(bool animate)
    {
        if (_adorner is null || !AssociatedObject.IsVisible)
        {
            return;
        }

        HideNativeIndicators(AssociatedObject);

        var selectedItem = FindDisplayedSelectedItem();
        var nativeIndicator = selectedItem is null
            ? null
            : selectedItem.Template.FindName("ActiveRectangle", selectedItem) as FrameworkElement;
        if (selectedItem is null
            || nativeIndicator is null
            || nativeIndicator.ActualHeight <= 0)
        {
            _adorner.Hide();
            _displayedItem = null;
            return;
        }

        Point position;
        try
        {
            position = nativeIndicator.TranslatePoint(new Point(), AssociatedObject);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        var targetHeight = nativeIndicator.ActualHeight;
        var targetWidth = nativeIndicator.ActualWidth;
        var targetChanged = _displayedItem != selectedItem
                            || !AreClose(_targetX, position.X)
                            || !AreClose(_targetY, position.Y)
                            || !AreClose(_targetHeight, targetHeight);
        if (!targetChanged)
        {
            return;
        }

        var shouldAnimate = animate && _displayedItem is not null;
        var useDetachedTransition = _displayedItem is not null
                                    && (IsFooterMenuItem(_displayedItem)
                                        || IsFooterMenuItem(selectedItem));
        _adorner.MoveTo(
            position.X,
            position.Y,
            targetWidth,
            targetHeight,
            shouldAnimate,
            useDetachedTransition,
            Duration);

        _displayedItem = selectedItem;
        _targetX = position.X;
        _targetY = position.Y;
        _targetHeight = targetHeight;
    }

    private NavigationViewItem? FindDisplayedSelectedItem()
    {
        if (AssociatedObject.SelectedItem is NavigationViewItem selectedItem
            && selectedItem.IsVisible)
        {
            return selectedItem;
        }

        return FindVisualDescendant<NavigationViewItem>(
            AssociatedObject,
            item => item.IsActive && item.IsVisible);
    }

    private bool IsFooterMenuItem(NavigationViewItem item)
    {
        INavigationViewItem? current = item;
        while (current is not null)
        {
            if (AssociatedObject.FooterMenuItems.Contains(current))
            {
                return true;
            }

            current = current.NavigationViewItemParent;
        }

        return false;
    }

    private void HideNativeIndicators(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is NavigationViewItem item)
            {
                HideNativeIndicator(item);
            }

            HideNativeIndicators(child);
        }
    }

    private void HideNativeIndicator(NavigationViewItem item)
    {
        if (item.Template.FindName("ActiveRectangle", item) is Rectangle indicator
            && _hiddenNativeIndicators.Add(indicator))
        {
            indicator.SetCurrentValue(UIElement.VisibilityProperty, Visibility.Hidden);
        }
    }

    private static T? FindVisualAncestor<T>(DependencyObject child) where T : DependencyObject
    {
        for (DependencyObject? current = child;
             current is not null;
             current = GetParent(current))
        {
            if (current is T ancestor)
            {
                return ancestor;
            }
        }

        return null;
    }

    private static DependencyObject? GetParent(DependencyObject element)
    {
        if (element is ContentElement contentElement)
        {
            return ContentOperations.GetParent(contentElement)
                   ?? (contentElement as FrameworkContentElement)?.Parent;
        }

        return element is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(element)
            : LogicalTreeHelper.GetParent(element);
    }

    private static T? FindVisualDescendant<T>(
        DependencyObject parent,
        Func<T, bool> predicate) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match && predicate(match))
            {
                return match;
            }

            var descendant = FindVisualDescendant(child, predicate);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static bool AreClose(double left, double right)
    {
        return !double.IsNaN(left) && Math.Abs(left - right) < 0.5;
    }

    private sealed class SelectionIndicatorAdorner : Adorner
    {
        private const double DefaultIndicatorWidth = 3;
        private const double DefaultIndicatorHeight = 16;
        private readonly Border _indicator;
        private readonly ScaleTransform _scale = new(1, 1);
        private readonly TranslateTransform _translation = new();
        private readonly VisualCollection _visuals;
        private bool _hasPosition;

        public SelectionIndicatorAdorner(UIElement adornedElement)
            : base(adornedElement)
        {
            _indicator = new Border
            {
                Width = DefaultIndicatorWidth,
                Height = DefaultIndicatorHeight,
                CornerRadius = new CornerRadius(2),
                Opacity = 0,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new TransformGroup
                {
                    Children =
                    {
                        _scale,
                        _translation
                    }
                }
            };
            _indicator.SetResourceReference(
                Border.BackgroundProperty,
                "NavigationViewSelectionIndicatorForeground");
            _visuals = new VisualCollection(this) { _indicator };

            // WPF 在这些属性变更时可能立即查询 VisualChildrenCount，
            // 因此必须等可视子项集合初始化完成后再设置。
            IsHitTestVisible = false;
            ClipToBounds = true;
        }

        protected override int VisualChildrenCount => _visuals?.Count ?? 0;

        protected override Visual GetVisualChild(int index)
        {
            return _visuals is null
                ? throw new ArgumentOutOfRangeException(nameof(index))
                : _visuals[index];
        }

        protected override Size MeasureOverride(Size constraint)
        {
            _indicator.Measure(constraint);
            return _indicator.DesiredSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _indicator.Arrange(new Rect(
                new Point(),
                new Size(_indicator.Width, _indicator.Height)));
            return finalSize;
        }

        public void Hide()
        {
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _scale.SetCurrentValue(ScaleTransform.ScaleYProperty, 1d);
            _indicator.BeginAnimation(UIElement.OpacityProperty, null);
            _indicator.SetCurrentValue(UIElement.OpacityProperty, 0d);
            _hasPosition = false;
        }

        public void MoveTo(
            double targetX,
            double targetY,
            double targetWidth,
            double targetHeight,
            bool animate,
            bool useDetachedTransition,
            TimeSpan duration)
        {
            targetWidth = targetWidth > 0 ? targetWidth : DefaultIndicatorWidth;
            targetHeight = targetHeight > 0 ? targetHeight : DefaultIndicatorHeight;

            var currentX = _translation.X;
            var currentY = _translation.Y;
            var currentHeight = _indicator.Height;
            var currentScaleY = _scale.ScaleY;
            StopAnimations(currentX, currentY, currentHeight, currentScaleY);

            _indicator.SetCurrentValue(FrameworkElement.WidthProperty, targetWidth);

            if (!_hasPosition || !animate || duration <= TimeSpan.Zero)
            {
                _translation.SetCurrentValue(TranslateTransform.XProperty, targetX);
                _translation.SetCurrentValue(TranslateTransform.YProperty, targetY);
                _scale.SetCurrentValue(ScaleTransform.ScaleYProperty, 1d);
                _indicator.SetCurrentValue(FrameworkElement.HeightProperty, targetHeight);
                _indicator.SetCurrentValue(UIElement.OpacityProperty, 1d);
                _hasPosition = true;
                InvalidateArrange();
                return;
            }

            var distance = Math.Abs(targetY - currentY);
            var actualDuration = TimeSpan.FromMilliseconds(Math.Clamp(
                duration.TotalMilliseconds + distance * 0.16,
                duration.TotalMilliseconds,
                duration.TotalMilliseconds + 100));

            if (useDetachedTransition || !AreClose(currentX, targetX))
            {
                AnimateDetachedLevelTransition(
                    currentX,
                    currentY,
                    currentHeight,
                    currentScaleY,
                    targetX,
                    targetY,
                    targetHeight,
                    actualDuration);
                _hasPosition = true;
                return;
            }

            var middleTime = TimeSpan.FromTicks((long)(actualDuration.Ticks * 0.52));
            var top = Math.Min(currentY, targetY);
            var bottom = Math.Max(currentY + currentHeight, targetY + targetHeight);
            var stretchedHeight = bottom - top;
            // 首段从零速度平滑启动，避免按下后的第一帧出现高度突变。
            var easingSpline = new KeySpline(0.25, 0.0, 0.35, 1.0);

            _scale.SetCurrentValue(ScaleTransform.ScaleYProperty, 1d);
            _translation.BeginAnimation(
                TranslateTransform.XProperty,
                CreateAnimation(currentX, targetX, targetX, middleTime, actualDuration, easingSpline));
            _translation.BeginAnimation(
                TranslateTransform.YProperty,
                CreateAnimation(currentY, top, targetY, middleTime, actualDuration, easingSpline));
            _indicator.BeginAnimation(
                FrameworkElement.HeightProperty,
                CreateAnimation(
                    currentHeight,
                    stretchedHeight,
                    targetHeight,
                    middleTime,
                    actualDuration,
                    easingSpline));
            _hasPosition = true;
        }

        private void AnimateDetachedLevelTransition(
            double currentX,
            double currentY,
            double currentHeight,
            double currentScaleY,
            double targetX,
            double targetY,
            double targetHeight,
            TimeSpan duration)
        {
            // 跨层级时横坐标不同，不再把首尾两项连接成一根长线。
            // 先保持旧指示条中心不变地恢复为短条，再以轻微过冲落到新层级。
            var normalizedStartY = currentY + (currentHeight - targetHeight) / 2;
            _translation.SetCurrentValue(TranslateTransform.YProperty, normalizedStartY);
            _indicator.SetCurrentValue(FrameworkElement.HeightProperty, targetHeight);

            _translation.BeginAnimation(
                TranslateTransform.XProperty,
                CreateBackAnimation(currentX, targetX, duration));
            _translation.BeginAnimation(
                TranslateTransform.YProperty,
                CreateBackAnimation(normalizedStartY, targetY, duration));
            _scale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                CreateElasticScaleAnimation(currentScaleY, duration));
        }

        private void StopAnimations(
            double currentX,
            double currentY,
            double currentHeight,
            double currentScaleY)
        {
            var currentOpacity = _indicator.Opacity;

            _translation.BeginAnimation(TranslateTransform.XProperty, null);
            _translation.BeginAnimation(TranslateTransform.YProperty, null);
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _indicator.BeginAnimation(FrameworkElement.HeightProperty, null);
            _indicator.BeginAnimation(UIElement.OpacityProperty, null);

            _translation.SetCurrentValue(TranslateTransform.XProperty, currentX);
            _translation.SetCurrentValue(TranslateTransform.YProperty, currentY);
            _scale.SetCurrentValue(ScaleTransform.ScaleYProperty, currentScaleY);
            _indicator.SetCurrentValue(FrameworkElement.HeightProperty, currentHeight);
            _indicator.SetCurrentValue(UIElement.OpacityProperty, currentOpacity);
        }

        private static DoubleAnimation CreateBackAnimation(
            double from,
            double to,
            TimeSpan duration)
        {
            return new DoubleAnimation(from, to, duration)
            {
                EasingFunction = new BackEase
                {
                    Amplitude = 0.12,
                    EasingMode = EasingMode.EaseOut
                },
                FillBehavior = FillBehavior.HoldEnd
            };
        }

        private static DoubleAnimationUsingKeyFrames CreateElasticScaleAnimation(
            double from,
            TimeSpan duration)
        {
            return new DoubleAnimationUsingKeyFrames
            {
                FillBehavior = FillBehavior.HoldEnd,
                KeyFrames =
                {
                    new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)),
                    new SplineDoubleKeyFrame(
                        0.78,
                        KeyTime.FromTimeSpan(TimeSpan.FromTicks((long)(duration.Ticks * 0.32))),
                        new KeySpline(0.3, 0.0, 0.4, 1.0)),
                    new SplineDoubleKeyFrame(
                        1.08,
                        KeyTime.FromTimeSpan(TimeSpan.FromTicks((long)(duration.Ticks * 0.72))),
                        new KeySpline(0.2, 0.0, 0.3, 1.0)),
                    new SplineDoubleKeyFrame(
                        1.0,
                        KeyTime.FromTimeSpan(duration),
                        new KeySpline(0.2, 0.0, 0.2, 1.0))
                }
            };
        }

        private static DoubleAnimationUsingKeyFrames CreateAnimation(
            double from,
            double middle,
            double to,
            TimeSpan middleTime,
            TimeSpan duration,
            KeySpline easingSpline)
        {
            return new DoubleAnimationUsingKeyFrames
            {
                FillBehavior = FillBehavior.HoldEnd,
                KeyFrames =
                {
                    new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)),
                    new SplineDoubleKeyFrame(
                        middle,
                        KeyTime.FromTimeSpan(middleTime),
                        easingSpline),
                    new SplineDoubleKeyFrame(
                        to,
                        KeyTime.FromTimeSpan(duration),
                        easingSpline)
                }
            };
        }
    }
}
