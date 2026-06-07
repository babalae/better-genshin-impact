using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using BetterGenshinImpact.Model.MaskMap;
using BetterGenshinImpact.ViewModel;

namespace BetterGenshinImpact.View.Controls;

public sealed class MiniMapPointsCanvas : FrameworkElement
{
    public static readonly DependencyProperty PointsSourceProperty =
        DependencyProperty.Register(
            nameof(PointsSource),
            typeof(ObservableCollection<MaskMapPoint>),
            typeof(MiniMapPointsCanvas),
            new PropertyMetadata(null, OnPointsSourceChanged));

    public static readonly DependencyProperty LabelsSourceProperty =
        DependencyProperty.Register(
            nameof(LabelsSource),
            typeof(IEnumerable<MaskMapPointLabel>),
            typeof(MiniMapPointsCanvas),
            new PropertyMetadata(null, OnLabelsSourceChanged));

    public static readonly DependencyProperty RoutePointsSourceProperty =
        DependencyProperty.Register(
            nameof(RoutePointsSource),
            typeof(ObservableCollection<MaskMapRoutePoint>),
            typeof(MiniMapPointsCanvas),
            new PropertyMetadata(null, OnRoutePointsSourceChanged));

    public static readonly DependencyProperty IsRouteOverlayEnabledProperty =
        DependencyProperty.Register(
            nameof(IsRouteOverlayEnabled),
            typeof(bool),
            typeof(MiniMapPointsCanvas),
            new PropertyMetadata(true, OnRouteOverlayEnabledChanged));

    public static readonly DependencyProperty ViewportProperty =
        DependencyProperty.Register(
            nameof(Viewport),
            typeof(Rect),
            typeof(MiniMapPointsCanvas),
            new PropertyMetadata(Rect.Empty, OnViewportChanged));

    private readonly VisualCollection _children;
    private readonly DrawingVisual _drawingVisual;
    private readonly Dictionary<string, Brush> _colorBrushCache;
    private int _refreshQueued;

    private ObservableCollection<MaskMapPoint>? _points;
    private ObservableCollection<MaskMapRoutePoint>? _routePointsSource;
    private List<MaskMapPoint> _allPoints = new();
    private List<MaskMapRoutePoint> _routePoints = new();
    private Dictionary<string, MaskMapPointLabel> _labelMap = new();
    private Rect _viewportRect = Rect.Empty;

    public ObservableCollection<MaskMapPoint>? PointsSource
    {
        get => (ObservableCollection<MaskMapPoint>?)GetValue(PointsSourceProperty);
        set => SetValue(PointsSourceProperty, value);
    }

    public IEnumerable<MaskMapPointLabel>? LabelsSource
    {
        get => (IEnumerable<MaskMapPointLabel>?)GetValue(LabelsSourceProperty);
        set => SetValue(LabelsSourceProperty, value);
    }

    public ObservableCollection<MaskMapRoutePoint>? RoutePointsSource
    {
        get => (ObservableCollection<MaskMapRoutePoint>?)GetValue(RoutePointsSourceProperty);
        set => SetValue(RoutePointsSourceProperty, value);
    }

    public bool IsRouteOverlayEnabled
    {
        get => (bool)GetValue(IsRouteOverlayEnabledProperty);
        set => SetValue(IsRouteOverlayEnabledProperty, value);
    }

    public Rect Viewport
    {
        get => (Rect)GetValue(ViewportProperty);
        set => SetValue(ViewportProperty, value);
    }

    public MiniMapPointsCanvas()
    {
        _children = new VisualCollection(this);
        _drawingVisual = new DrawingVisual();
        _children.Add(_drawingVisual);
        _colorBrushCache = new Dictionary<string, Brush>();

        IsHitTestVisible = false;

        MapIconImageCache.ImageUpdated += PointImageCacheManagerOnImageUpdated;
    }

    private static void OnPointsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var canvas = (MiniMapPointsCanvas)d;
        canvas.UpdatePoints(e.NewValue as ObservableCollection<MaskMapPoint>);
    }

    private static void OnLabelsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var canvas = (MiniMapPointsCanvas)d;
        canvas.UpdateLabels(e.NewValue as IEnumerable<MaskMapPointLabel>);
    }

    private static void OnRoutePointsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var canvas = (MiniMapPointsCanvas)d;
        canvas.UpdateRoutePoints(e.NewValue as ObservableCollection<MaskMapRoutePoint>);
    }

    private static void OnRouteOverlayEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((MiniMapPointsCanvas)d).Refresh();
    }

    private static void OnViewportChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not Rect viewport)
        {
            return;
        }

        ((MiniMapPointsCanvas)d).UpdateViewport(viewport.X, viewport.Y, viewport.Width, viewport.Height);
    }

    protected override void OnVisualParentChanged(DependencyObject oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        if (VisualParent == null)
        {
            MapIconImageCache.ImageUpdated -= PointImageCacheManagerOnImageUpdated;
        }
    }

    private void PointImageCacheManagerOnImageUpdated(object? sender, string e)
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) != 0)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            Interlocked.Exchange(ref _refreshQueued, 0);
            Refresh();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    protected override int VisualChildrenCount => _children.Count;

    protected override Visual GetVisualChild(int index)
    {
        if (index < 0 || index >= _children.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return _children[index];
    }

    private void OnPointsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (MaskMapPoint point in e.OldItems)
            {
                UnsubscribePoint(point);
            }
        }

        if (e.NewItems != null)
        {
            foreach (MaskMapPoint point in e.NewItems)
            {
                SubscribePoint(point);
            }
        }

        _allPoints = _points?.ToList() ?? new List<MaskMapPoint>();
        Refresh();
    }

    private void OnRoutePointsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _routePoints = _routePointsSource?.ToList() ?? [];
        Refresh();
    }

    private void SubscribePoint(MaskMapPoint point)
    {
        if (point is INotifyPropertyChanged notifyPoint)
        {
            notifyPoint.PropertyChanged += OnPointPropertyChanged;
        }
    }

    private void UnsubscribePoint(MaskMapPoint point)
    {
        if (point is INotifyPropertyChanged notifyPoint)
        {
            notifyPoint.PropertyChanged -= OnPointPropertyChanged;
        }
    }

    private void OnPointPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Refresh();
    }

    private void RenderPoints()
    {
        using var dc = _drawingVisual.RenderOpen();
        var hasRoute = IsRouteOverlayEnabled && _routePoints.Count > 0;
        if ((_allPoints.Count == 0 && !hasRoute) || _viewportRect.IsEmpty || _viewportRect.Width == 0)
        {
            return;
        }

        var aw = ActualWidth;
        var ah = ActualHeight;
        if (aw <= 0 || ah <= 0)
        {
            return;
        }

        var side = Math.Min(aw, ah);
        if (side <= 0)
        {
            return;
        }

        var clipRect = new Rect((aw - side) / 2.0, (ah - side) / 2.0, side, side);
        var clip = new EllipseGeometry(clipRect);
        dc.PushClip(clip);

        var expandedViewport = _viewportRect;
        expandedViewport.Inflate(MaskMapPointStatic.Width, MaskMapPointStatic.Height);

        var scaleX = side / _viewportRect.Width;
        var scaleY = side / _viewportRect.Height;

        var pointSide = Math.Max(8, Math.Min(16, side / 12.0));

        if (hasRoute)
        {
            DrawRoute(dc, clipRect, scaleX, scaleY, side);
        }

        foreach (var point in _allPoints)
        {
            if (!expandedViewport.Contains(point.ImageX, point.ImageY))
            {
                continue;
            }

            var localX = clipRect.X + (point.ImageX - _viewportRect.X) * scaleX;
            var localY = clipRect.Y + (point.ImageY - _viewportRect.Y) * scaleY;
            DrawPoint(dc, point, localX, localY, pointSide, pointSide);
        }

        dc.Pop();
    }

    private void DrawRoute(DrawingContext dc, Rect clipRect, double scaleX, double scaleY, double side)
    {
        if (_routePoints.Count == 0)
        {
            return;
        }

        var outerBrush = new SolidColorBrush(Color.FromArgb(210, 6, 12, 18));
        outerBrush.Freeze();
        var routeBrush = new SolidColorBrush(Color.FromArgb(238, 54, 220, 230));
        routeBrush.Freeze();
        var markerFillBrush = new SolidColorBrush(Color.FromArgb(245, 255, 178, 72));
        markerFillBrush.Freeze();
        var markerBorderBrush = new SolidColorBrush(Color.FromArgb(240, 255, 255, 255));
        markerBorderBrush.Freeze();

        var outerPen = new Pen(outerBrush, Math.Max(3.4, side / 70.0));
        outerPen.Freeze();
        var routePen = new Pen(routeBrush, Math.Max(1.8, side / 120.0));
        routePen.Freeze();
        var markerPen = new Pen(markerBorderBrush, Math.Max(1.0, side / 180.0));
        markerPen.Freeze();

        for (var i = 0; i < _routePoints.Count - 1; i++)
        {
            var next = _routePoints[i + 1];
            if (IsTeleportRoutePoint(next))
            {
                continue;
            }

            var start = ToLocalPoint(_routePoints[i], clipRect, scaleX, scaleY);
            var end = ToLocalPoint(next, clipRect, scaleX, scaleY);
            dc.DrawLine(outerPen, start, end);
            dc.DrawLine(routePen, start, end);
        }

        var markerRadius = Math.Max(2.0, Math.Min(4.0, side / 85.0));
        foreach (var point in _routePoints)
        {
            if (!IsInsideExpandedViewport(point, 24))
            {
                continue;
            }

            var center = ToLocalPoint(point, clipRect, scaleX, scaleY);
            dc.DrawEllipse(markerFillBrush, markerPen, center, markerRadius, markerRadius);
        }
    }

    private static bool IsTeleportRoutePoint(MaskMapRoutePoint point)
    {
        return string.Equals(point.Type, "teleport", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsInsideExpandedViewport(MaskMapRoutePoint point, double padding)
    {
        var viewport = _viewportRect;
        viewport.Inflate(padding, padding);
        return viewport.Contains(point.ImageX, point.ImageY);
    }

    private Point ToLocalPoint(MaskMapRoutePoint point, Rect clipRect, double scaleX, double scaleY)
    {
        return new Point(
            clipRect.X + (point.ImageX - _viewportRect.X) * scaleX,
            clipRect.Y + (point.ImageY - _viewportRect.Y) * scaleY);
    }

    private void DrawPoint(DrawingContext dc, MaskMapPoint point, double centerX, double centerY, double width, double height)
    {
        var radius = width / 2.0;
        const double strokeThickness = 2.0;

        var circleCenter = new Point(centerX, centerY);

        var fillBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#323947"));
        fillBrush.Freeze();

        var borderBrush = new SolidColorBrush(Color.FromRgb(0xD3, 0xBC, 0x8E));
        borderBrush.Freeze();

        var borderPen = new Pen(borderBrush, strokeThickness);
        borderPen.Freeze();

        var shadowBrush = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0));
        shadowBrush.Freeze();

        var shadowOffset = new Point(2, 2);

        var shadowCircleGeometry = new EllipseGeometry(
            new Point(circleCenter.X + shadowOffset.X, circleCenter.Y + shadowOffset.Y),
            radius, radius);
        dc.DrawGeometry(shadowBrush, null, shadowCircleGeometry);

        var circleGeometry = new EllipseGeometry(circleCenter, radius, radius);
        dc.DrawGeometry(fillBrush, borderPen, circleGeometry);

        if (_labelMap.TryGetValue(point.LabelId, out var label))
        {
            var image = MapIconImageCache.TryGet(label.IconUrl);
            if (image != null)
            {
                var imageRect = new Rect(circleCenter.X - radius, circleCenter.Y - radius, width, height);
                dc.PushClip(circleGeometry);
                dc.DrawImage(image, imageRect);
                dc.Pop();
            }
            else
            {
                _ = MapIconImageCache.GetAsync(label.IconUrl, CancellationToken.None);

                var brush = GetColorBrush(label);
                dc.DrawEllipse(brush, null, new Point(centerX, centerY), width / 2.0, height / 2.0);
            }
        }
        else
        {
            var brush = new SolidColorBrush(GenerateRandomColor(point.Id));
            brush.Freeze();
            dc.DrawEllipse(brush, null, new Point(centerX, centerY), width / 2.0, height / 2.0);
        }
    }

    private Brush GetColorBrush(MaskMapPointLabel label)
    {
        if (_colorBrushCache.TryGetValue(label.LabelId, out var cachedBrush))
        {
            return cachedBrush;
        }

        Color color;
        if (label.Color.HasValue)
        {
            var c = label.Color.Value;
            color = Color.FromArgb(c.A, c.R, c.G, c.B);
        }
        else
        {
            color = GenerateRandomColor(label.LabelId);
        }

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        _colorBrushCache[label.LabelId] = brush;
        return brush;
    }

    private static Color GenerateRandomColor(string seed)
    {
        var hash = seed?.GetHashCode() ?? 0;
        var random = new Random(hash);
        return Color.FromRgb(
            (byte)random.Next(80, 256),
            (byte)random.Next(80, 256),
            (byte)random.Next(80, 256));
    }

    public void UpdatePoints(ObservableCollection<MaskMapPoint>? points)
    {
        if (_points != null)
        {
            _points.CollectionChanged -= OnPointsCollectionChanged;
            foreach (var point in _points)
            {
                UnsubscribePoint(point);
            }
        }

        _points = points;

        if (_points != null)
        {
            _points.CollectionChanged += OnPointsCollectionChanged;
            foreach (var point in _points)
            {
                SubscribePoint(point);
            }

            _allPoints = _points.ToList();
        }
        else
        {
            _allPoints.Clear();
        }

        Refresh();
    }

    public void UpdateLabels(IEnumerable<MaskMapPointLabel>? labels)
    {
        if (labels != null)
        {
            _labelMap = labels.ToDictionary(l => l.LabelId, l => l);
            _colorBrushCache.Clear();
        }
        else
        {
            _labelMap.Clear();
            _colorBrushCache.Clear();
        }

        Refresh();
    }

    public void UpdateRoutePoints(ObservableCollection<MaskMapRoutePoint>? points)
    {
        if (_routePointsSource != null)
        {
            _routePointsSource.CollectionChanged -= OnRoutePointsCollectionChanged;
        }

        _routePointsSource = points;
        if (_routePointsSource != null)
        {
            _routePointsSource.CollectionChanged += OnRoutePointsCollectionChanged;
            _routePoints = _routePointsSource.ToList();
        }
        else
        {
            _routePoints.Clear();
        }

        Refresh();
    }

    public void UpdateViewport(double x, double y, double width, double height)
    {
        var newRect = new Rect(x, y, width, height);
        if (newRect.Equals(_viewportRect))
        {
            return;
        }

        _viewportRect = newRect;
        Refresh();
    }

    public void Refresh()
    {
        RenderPoints();
    }
}
