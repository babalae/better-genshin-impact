using BetterGenshinImpact.GameTask.AutoPathing.Telemetry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace BetterGenshinImpact.View.Controls;

/// <summary>A lightweight renderer for large route graphs. It avoids creating one WPF element per node or edge.</summary>
public sealed class RouteGraphCanvas : FrameworkElement
{
    private const double RenderBucketSize = 512;

    public static readonly DependencyProperty MapNameProperty = DependencyProperty.Register(
        nameof(MapName), typeof(string), typeof(RouteGraphCanvas),
        new FrameworkPropertyMetadata("Teyvat", FrameworkPropertyMetadataOptions.AffectsRender, OnMapNameChanged));

    public static readonly DependencyProperty NodesProperty = DependencyProperty.Register(
        nameof(Nodes), typeof(IReadOnlyList<RouteNavigationNode>), typeof(RouteGraphCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnGraphChanged));

    public static readonly DependencyProperty EdgesProperty = DependencyProperty.Register(
        nameof(Edges), typeof(IReadOnlyList<RouteNavigationEdge>), typeof(RouteGraphCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnGraphChanged));

    public static readonly DependencyProperty TeleportsProperty = DependencyProperty.Register(
        nameof(Teleports), typeof(IReadOnlyList<RouteGraphTeleportEntry>), typeof(RouteGraphCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnGraphChanged));

    public static readonly DependencyProperty SelectedNodeProperty = DependencyProperty.Register(
        nameof(SelectedNode), typeof(RouteNavigationNode), typeof(RouteGraphCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedEdgeProperty = DependencyProperty.Register(
        nameof(SelectedEdge), typeof(RouteNavigationEdge), typeof(RouteGraphCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedTeleportProperty = DependencyProperty.Register(
        nameof(SelectedTeleport), typeof(RouteGraphTeleportEntry), typeof(RouteGraphCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TargetPointProperty = DependencyProperty.Register(
        nameof(TargetPoint), typeof(RouteGraphPoint?), typeof(RouteGraphCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DraftPathPointsProperty = DependencyProperty.Register(
        nameof(DraftPathPoints), typeof(IReadOnlyList<RouteGraphPoint>), typeof(RouteGraphCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsPathDrawingProperty = DependencyProperty.Register(
        nameof(IsPathDrawing), typeof(bool), typeof(RouteGraphCanvas),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnPathDrawingChanged));

    public static readonly DependencyProperty SelectNodeCommandProperty = DependencyProperty.Register(
        nameof(SelectNodeCommand), typeof(ICommand), typeof(RouteGraphCanvas));

    public static readonly DependencyProperty SelectEdgeCommandProperty = DependencyProperty.Register(
        nameof(SelectEdgeCommand), typeof(ICommand), typeof(RouteGraphCanvas));

    public static readonly DependencyProperty SelectTeleportCommandProperty = DependencyProperty.Register(
        nameof(SelectTeleportCommand), typeof(ICommand), typeof(RouteGraphCanvas));

    public static readonly DependencyProperty AddDrawPathPointCommandProperty = DependencyProperty.Register(
        nameof(AddDrawPathPointCommand), typeof(ICommand), typeof(RouteGraphCanvas));

    private static readonly Pen NormalEdgePen = FrozenPen(Color.FromArgb(90, 80, 150, 230), 1);
    private static readonly Pen VerifiedEdgePen = FrozenPen(Color.FromArgb(180, 60, 190, 110), 1.4);
    private static readonly Pen RiskyEdgePen = FrozenPen(Color.FromArgb(190, 245, 160, 45), 1.5);
    private static readonly Pen DisabledEdgePen = FrozenPen(Color.FromArgb(100, 220, 70, 70), 1);
    private static readonly Pen SelectedEdgePen = FrozenPen(Colors.Cyan, 3);
    private static readonly Brush NodeBrush = FrozenBrush(Color.FromArgb(220, 190, 210, 235));
    private static readonly Brush TeleportEntryBrush = FrozenBrush(Color.FromArgb(240, 255, 170, 35));
    private static readonly Brush TeleportBrush = FrozenBrush(Color.FromArgb(240, 255, 220, 50));
    private static readonly Brush SelectedBrush = FrozenBrush(Colors.Cyan);
    private static readonly Pen DraftPathPen = FrozenPen(Color.FromArgb(245, 80, 225, 255), 2.5);

    private double _scale = 1;
    private Vector _offset;
    private bool _viewInitialized;
    private bool _isPanning;
    private bool _isDrawingStroke;
    private Point _lastMousePoint;
    private Point _lastDrawScreenPoint;
    private RouteMapBackground? _mapBackground;
    private string _loadedBackgroundMapName = string.Empty;
    private string _loadingBackgroundMapName = string.Empty;
    private int _backgroundLoadVersion;
    private bool _renderCacheDirty = true;
    private IReadOnlyDictionary<string, RouteNavigationNode> _cachedNodesById =
        new Dictionary<string, RouteNavigationNode>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<EdgeRenderItem> _cachedEdges = [];
    private IReadOnlyDictionary<(int X, int Y), List<RouteNavigationNode>> _cachedNodeBuckets =
        new Dictionary<(int X, int Y), List<RouteNavigationNode>>();
    private IReadOnlyDictionary<(int X, int Y), List<EdgeRenderItem>> _cachedEdgeBuckets =
        new Dictionary<(int X, int Y), List<EdgeRenderItem>>();

    public RouteGraphCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
        MouseWheel += OnMouseWheel;
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
    }

    public IReadOnlyList<RouteNavigationNode>? Nodes
    {
        get => (IReadOnlyList<RouteNavigationNode>?)GetValue(NodesProperty);
        set => SetValue(NodesProperty, value);
    }

    public string MapName
    {
        get => (string)GetValue(MapNameProperty);
        set => SetValue(MapNameProperty, value);
    }

    public IReadOnlyList<RouteNavigationEdge>? Edges
    {
        get => (IReadOnlyList<RouteNavigationEdge>?)GetValue(EdgesProperty);
        set => SetValue(EdgesProperty, value);
    }

    public IReadOnlyList<RouteGraphTeleportEntry>? Teleports
    {
        get => (IReadOnlyList<RouteGraphTeleportEntry>?)GetValue(TeleportsProperty);
        set => SetValue(TeleportsProperty, value);
    }

    public RouteNavigationNode? SelectedNode
    {
        get => (RouteNavigationNode?)GetValue(SelectedNodeProperty);
        set => SetValue(SelectedNodeProperty, value);
    }

    public RouteNavigationEdge? SelectedEdge
    {
        get => (RouteNavigationEdge?)GetValue(SelectedEdgeProperty);
        set => SetValue(SelectedEdgeProperty, value);
    }

    public RouteGraphTeleportEntry? SelectedTeleport
    {
        get => (RouteGraphTeleportEntry?)GetValue(SelectedTeleportProperty);
        set => SetValue(SelectedTeleportProperty, value);
    }

    public RouteGraphPoint? TargetPoint
    {
        get => (RouteGraphPoint?)GetValue(TargetPointProperty);
        set => SetValue(TargetPointProperty, value);
    }

    public IReadOnlyList<RouteGraphPoint>? DraftPathPoints
    {
        get => (IReadOnlyList<RouteGraphPoint>?)GetValue(DraftPathPointsProperty);
        set => SetValue(DraftPathPointsProperty, value);
    }

    public bool IsPathDrawing
    {
        get => (bool)GetValue(IsPathDrawingProperty);
        set => SetValue(IsPathDrawingProperty, value);
    }

    public ICommand? SelectNodeCommand
    {
        get => (ICommand?)GetValue(SelectNodeCommandProperty);
        set => SetValue(SelectNodeCommandProperty, value);
    }

    public ICommand? SelectEdgeCommand
    {
        get => (ICommand?)GetValue(SelectEdgeCommandProperty);
        set => SetValue(SelectEdgeCommandProperty, value);
    }

    public ICommand? SelectTeleportCommand
    {
        get => (ICommand?)GetValue(SelectTeleportCommandProperty);
        set => SetValue(SelectTeleportCommandProperty, value);
    }

    public ICommand? AddDrawPathPointCommand
    {
        get => (ICommand?)GetValue(AddDrawPathPointCommandProperty);
        set => SetValue(AddDrawPathPointCommandProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        EnsureBackgroundLoading();
        drawingContext.DrawRectangle(FrozenBrush(Color.FromRgb(16, 24, 32)), null, new Rect(RenderSize));
        EnsureView();
        DrawMapBackground(drawingContext);

        EnsureRenderCache();
        var nodeById = _cachedNodesById;
        var viewport = GetWorldViewport(48);
        var edgeItems = QueryBuckets(_cachedEdgeBuckets, viewport, item => item.Bounds);
        var edgeLimit = _scale < 0.025 ? 1800 : _scale < 0.08 ? 6000 : 20000;
        foreach (var item in TakeEvenly(edgeItems, edgeLimit, item => ReferenceEquals(item.Edge, SelectedEdge)))
        {
            var edge = item.Edge;
            var points = _scale < 0.08 &&
                         nodeById.TryGetValue(edge.FromNodeId, out var lodFrom) &&
                         nodeById.TryGetValue(edge.ToNodeId, out var lodTo)
                ? (IReadOnlyList<RouteGraphPoint>)
                [
                    new RouteGraphPoint(lodFrom.X, lodFrom.Y),
                    new RouteGraphPoint(lodTo.X, lodTo.Y)
                ]
                : item.Points;

            var pen = ReferenceEquals(edge, SelectedEdge) ? SelectedEdgePen : ResolveEdgePen(edge);
            for (var index = 1; index < points.Count; index++)
            {
                drawingContext.DrawLine(pen, ToScreen(points[index - 1]), ToScreen(points[index]));
            }
        }

        DrawDraftPath(drawingContext);

        var visibleNodes = QueryBuckets(
            _cachedNodeBuckets,
            viewport,
            node => new Rect(node.X, node.Y, 0.01, 0.01));
        var nodeLimit = _scale < 0.025 ? 1200 : _scale < 0.08 ? 5000 : 24000;
        foreach (var node in TakeEvenly(visibleNodes, nodeLimit, node => ReferenceEquals(node, SelectedNode)))
        {
            var selected = ReferenceEquals(node, SelectedNode);
            var radius = selected ? 5 : node.AnchorIds.Count > 0 ? 3.2 : _scale < 0.025 ? 1.2 : 2;
            drawingContext.DrawEllipse(
                selected ? SelectedBrush : node.AnchorIds.Count > 0 ? TeleportEntryBrush : NodeBrush,
                null,
                ToScreen(new RouteGraphPoint(node.X, node.Y)),
                radius,
                radius);
        }

        foreach (var teleport in (Teleports ?? []).Where(item =>
                     viewport.Contains(item.SpawnImagePoint.X, item.SpawnImagePoint.Y)))
        {
            var center = ToScreen(teleport.SpawnImagePoint);
            var radius = ReferenceEquals(teleport, SelectedTeleport) ? 8 : 5;
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(new Point(center.X, center.Y - radius), true, true);
                context.LineTo(new Point(center.X + radius, center.Y + radius), true, false);
                context.LineTo(new Point(center.X - radius, center.Y + radius), true, false);
            }
            geometry.Freeze();
            drawingContext.DrawGeometry(
                ReferenceEquals(teleport, SelectedTeleport) ? SelectedBrush : TeleportBrush,
                null,
                geometry);
        }

        if (TargetPoint is { } target)
        {
            var center = ToScreen(target);
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(new Point(center.X, center.Y - 8), true, true);
                context.LineTo(new Point(center.X + 8, center.Y), true, false);
                context.LineTo(new Point(center.X, center.Y + 8), true, false);
                context.LineTo(new Point(center.X - 8, center.Y), true, false);
            }
            geometry.Freeze();
            drawingContext.DrawGeometry(FrozenBrush(Color.FromRgb(235, 70, 210)), null, geometry);
        }
    }

    private void DrawDraftPath(DrawingContext drawingContext)
    {
        var points = DraftPathPoints ?? [];
        for (var index = 1; index < points.Count; index++)
        {
            drawingContext.DrawLine(DraftPathPen, ToScreen(points[index - 1]), ToScreen(points[index]));
        }

        foreach (var point in points)
        {
            drawingContext.DrawEllipse(SelectedBrush, null, ToScreen(point), 3.5, 3.5);
        }
    }

    private void EnsureBackgroundLoading()
    {
        var mapName = RouteGraphGeometry.NormalizeMapName(MapName);
        if (string.Equals(_loadedBackgroundMapName, mapName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_loadingBackgroundMapName, mapName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var version = ++_backgroundLoadVersion;
        _loadingBackgroundMapName = mapName;
        _ = Task.Run(() => RouteMapBackgroundLoader.Load(mapName)).ContinueWith(task =>
            Dispatcher.BeginInvoke(() =>
            {
                if (version != _backgroundLoadVersion)
                {
                    return;
                }

                _mapBackground = task.Status == TaskStatus.RanToCompletion ? task.Result : null;
                _loadedBackgroundMapName = mapName;
                _loadingBackgroundMapName = string.Empty;
                _viewInitialized = false;
                InvalidateVisual();
            }));
    }

    private void DrawMapBackground(DrawingContext drawingContext)
    {
        if (_mapBackground == null)
        {
            return;
        }

        var topLeft = ToScreen(new RouteGraphPoint(0, 0));
        var bottomRight = ToScreen(new RouteGraphPoint(
            _mapBackground.LogicalWidth,
            _mapBackground.LogicalHeight));
        drawingContext.PushOpacity(0.78);
        drawingContext.DrawImage(
            _mapBackground.Bitmap,
            new Rect(topLeft, bottomRight));
        drawingContext.Pop();
    }

    private Rect GetWorldViewport(double screenMargin)
    {
        var topLeft = ToWorld(new Point(-screenMargin, -screenMargin));
        var bottomRight = ToWorld(new Point(ActualWidth + screenMargin, ActualHeight + screenMargin));
        return new Rect(
            new Point(topLeft.X, topLeft.Y),
            new Point(bottomRight.X, bottomRight.Y));
    }

    private void EnsureRenderCache()
    {
        if (!_renderCacheDirty)
        {
            return;
        }

        _cachedNodesById = (Nodes ?? [])
            .Where(node => !string.IsNullOrWhiteSpace(node.NodeId))
            .GroupBy(node => node.NodeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        _cachedEdges = (Edges ?? [])
            .Select(edge =>
            {
                var points = ResolvePoints(edge, _cachedNodesById);
                if (points.Count < 2)
                {
                    return null;
                }

                var minX = points.Min(point => point.X);
                var minY = points.Min(point => point.Y);
                var maxX = points.Max(point => point.X);
                var maxY = points.Max(point => point.Y);
                var bounds = new Rect(
                    new Point(minX, minY),
                    new Point(maxX, maxY));
                if (bounds.Width < 0.01 || bounds.Height < 0.01)
                {
                    bounds.Inflate(0.01, 0.01);
                }
                return new EdgeRenderItem(edge, points, bounds);
            })
            .Where(item => item != null)
            .Cast<EdgeRenderItem>()
            .ToList();
        _cachedNodeBuckets = BuildNodeBuckets(Nodes ?? []);
        _cachedEdgeBuckets = BuildEdgeBuckets(_cachedEdges);
        _renderCacheDirty = false;
    }

    private static IReadOnlyDictionary<(int X, int Y), List<RouteNavigationNode>> BuildNodeBuckets(
        IReadOnlyList<RouteNavigationNode> nodes)
    {
        var buckets = new Dictionary<(int X, int Y), List<RouteNavigationNode>>();
        foreach (var node in nodes)
        {
            AddToBucket(buckets, GetBucket(node.X, node.Y), node);
        }

        return buckets;
    }

    private static IReadOnlyDictionary<(int X, int Y), List<EdgeRenderItem>> BuildEdgeBuckets(
        IReadOnlyList<EdgeRenderItem> edges)
    {
        var buckets = new Dictionary<(int X, int Y), List<EdgeRenderItem>>();
        foreach (var edge in edges)
        {
            var min = GetBucket(edge.Bounds.Left, edge.Bounds.Top);
            var max = GetBucket(edge.Bounds.Right, edge.Bounds.Bottom);
            for (var x = min.X; x <= max.X; x++)
            {
                for (var y = min.Y; y <= max.Y; y++)
                {
                    AddToBucket(buckets, (x, y), edge);
                }
            }
        }

        return buckets;
    }

    private static List<T> QueryBuckets<T>(
        IReadOnlyDictionary<(int X, int Y), List<T>> buckets,
        Rect viewport,
        Func<T, Rect> boundsSelector)
        where T : class
    {
        var result = new List<T>();
        var seen = new HashSet<T>(ReferenceEqualityComparer.Instance);
        var min = GetBucket(viewport.Left, viewport.Top);
        var max = GetBucket(viewport.Right, viewport.Bottom);
        for (var x = min.X; x <= max.X; x++)
        {
            for (var y = min.Y; y <= max.Y; y++)
            {
                if (!buckets.TryGetValue((x, y), out var items))
                {
                    continue;
                }

                foreach (var item in items)
                {
                    if (seen.Add(item) && boundsSelector(item).IntersectsWith(viewport))
                    {
                        result.Add(item);
                    }
                }
            }
        }

        return result;
    }

    private static void AddToBucket<T>(
        IDictionary<(int X, int Y), List<T>> buckets,
        (int X, int Y) key,
        T value)
    {
        if (!buckets.TryGetValue(key, out var items))
        {
            items = [];
            buckets[key] = items;
        }

        items.Add(value);
    }

    private static (int X, int Y) GetBucket(double x, double y) =>
        ((int)Math.Floor(x / RenderBucketSize), (int)Math.Floor(y / RenderBucketSize));

    private static IEnumerable<T> TakeEvenly<T>(
        IReadOnlyList<T> items,
        int limit,
        Func<T, bool> mustInclude)
    {
        if (items.Count <= limit)
        {
            return items;
        }

        var result = new List<T>(limit + 1);
        var step = items.Count / (double)limit;
        for (var index = 0d; index < items.Count && result.Count < limit; index += step)
        {
            result.Add(items[Math.Min(items.Count - 1, (int)index)]);
        }

        var selected = items.FirstOrDefault(mustInclude);
        if (selected is not null && !result.Contains(selected))
        {
            result.Add(selected);
        }

        return result;
    }

    private void EnsureView()
    {
        if (_viewInitialized || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var points = RouteMapGeometryCatalog.TryGet(MapName, out var geometry)
            ? new List<RouteGraphPoint>
            {
                new(0, 0),
                new(geometry.ImageWidth, geometry.ImageHeight)
            }
            : (Nodes ?? []).Select(node => new RouteGraphPoint(node.X, node.Y))
                .Concat((Teleports ?? []).Select(teleport => teleport.SpawnImagePoint)).ToList();
        if (TargetPoint is { } targetPoint)
        {
            points.Add(targetPoint);
        }
        if (points.Count == 0)
        {
            return;
        }

        var minX = points.Min(point => point.X);
        var maxX = points.Max(point => point.X);
        var minY = points.Min(point => point.Y);
        var maxY = points.Max(point => point.Y);
        var width = Math.Max(1, maxX - minX);
        var height = Math.Max(1, maxY - minY);
        _scale = Math.Max(0.01, Math.Min((ActualWidth - 40) / width, (ActualHeight - 40) / height));
        _offset = new Vector(
            ((ActualWidth - (width * _scale)) / 2) - (minX * _scale),
            ((ActualHeight - (height * _scale)) / 2) - (minY * _scale));
        _viewInitialized = true;
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var position = e.GetPosition(this);
        var factor = e.Delta > 0 ? 1.2 : 1 / 1.2;
        var newScale = Math.Clamp(_scale * factor, 0.005, 50);
        var worldX = (position.X - _offset.X) / _scale;
        var worldY = (position.Y - _offset.Y) / _scale;
        _scale = newScale;
        _offset = new Vector(position.X - (worldX * _scale), position.Y - (worldY * _scale));
        InvalidateVisual();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        _lastMousePoint = e.GetPosition(this);
        if (e.ChangedButton is MouseButton.Middle or MouseButton.Right)
        {
            _isPanning = true;
            CaptureMouse();
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        var world = ToWorld(_lastMousePoint);
        if (IsPathDrawing)
        {
            Execute(AddDrawPathPointCommand, world);
            _isDrawingStroke = true;
            _lastDrawScreenPoint = _lastMousePoint;
            CaptureMouse();
            return;
        }

        var tolerance = 9 / Math.Max(0.001, _scale);
        EnsureRenderCache();
        var hitBounds = new Rect(
            world.X - tolerance,
            world.Y - tolerance,
            tolerance * 2,
            tolerance * 2);
        var node = QueryBuckets(
                _cachedNodeBuckets,
                hitBounds,
                item => new Rect(item.X, item.Y, 0.01, 0.01))
            .Select(item => new
            {
                Item = item,
                Distance = RouteGraphGeometry.Distance(world, new RouteGraphPoint(item.X, item.Y))
            })
            .Where(item => item.Distance <= tolerance)
            .OrderBy(item => item.Distance)
            .FirstOrDefault()?.Item;
        if (node != null)
        {
            Execute(SelectNodeCommand, node);
            return;
        }

        var teleport = (Teleports ?? []).Select(item => new
            {
                Item = item,
                Distance = RouteGraphGeometry.Distance(world, item.SpawnImagePoint)
            })
            .Where(item => item.Distance <= tolerance)
            .OrderBy(item => item.Distance)
            .FirstOrDefault()?.Item;
        if (teleport != null)
        {
            Execute(SelectTeleportCommand, teleport);
            return;
        }

        var edge = QueryBuckets(_cachedEdgeBuckets, hitBounds, item => item.Bounds)
            .Select(item => new
            {
                Item = item.Edge,
                Distance = DistanceToPolyline(world, item.Points)
            })
            .Where(item => item.Distance <= tolerance)
            .OrderBy(item => item.Distance)
            .FirstOrDefault()?.Item;
        if (edge != null)
        {
            Execute(SelectEdgeCommand, edge);
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDrawingStroke && e.LeftButton == MouseButtonState.Pressed)
        {
            var drawPosition = e.GetPosition(this);
            if ((drawPosition - _lastDrawScreenPoint).Length >= 6)
            {
                Execute(AddDrawPathPointCommand, ToWorld(drawPosition));
                _lastDrawScreenPoint = drawPosition;
            }
            return;
        }

        if (!_isPanning)
        {
            return;
        }

        var position = e.GetPosition(this);
        _offset += position - _lastMousePoint;
        _lastMousePoint = position;
        InvalidateVisual();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDrawingStroke && e.ChangedButton == MouseButton.Left)
        {
            _isDrawingStroke = false;
            ReleaseMouseCapture();
            return;
        }

        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;
        ReleaseMouseCapture();
    }

    private Point ToScreen(RouteGraphPoint point) => new((point.X * _scale) + _offset.X, (point.Y * _scale) + _offset.Y);

    private RouteGraphPoint ToWorld(Point point) => new((point.X - _offset.X) / _scale, (point.Y - _offset.Y) / _scale);

    private static IReadOnlyList<RouteGraphPoint> ResolvePoints(
        RouteNavigationEdge edge,
        IReadOnlyDictionary<string, RouteNavigationNode> nodeById)
    {
        if (edge.Points.Count >= 2)
        {
            return edge.Points.Select(point => new RouteGraphPoint(point.X, point.Y)).ToList();
        }

        return nodeById.TryGetValue(edge.FromNodeId, out var from) && nodeById.TryGetValue(edge.ToNodeId, out var to)
            ? [new RouteGraphPoint(from.X, from.Y), new RouteGraphPoint(to.X, to.Y)]
            : [];
    }

    private static Pen ResolveEdgePen(RouteNavigationEdge edge)
    {
        return edge.ReviewStatus switch
        {
            GraphReviewStatus.Verified => VerifiedEdgePen,
            GraphReviewStatus.Risky => RiskyEdgePen,
            GraphReviewStatus.Disabled or GraphReviewStatus.Rejected => DisabledEdgePen,
            _ => NormalEdgePen
        };
    }

    private static double DistanceToPolyline(RouteGraphPoint point, IReadOnlyList<RouteGraphPoint> points)
    {
        var minimum = double.PositiveInfinity;
        for (var index = 1; index < points.Count; index++)
        {
            var from = points[index - 1];
            var to = points[index];
            var ratio = RouteGraphGeometry.ProjectRatio(point, from, to);
            var projection = new RouteGraphPoint(from.X + ((to.X - from.X) * ratio), from.Y + ((to.Y - from.Y) * ratio));
            minimum = Math.Min(minimum, RouteGraphGeometry.Distance(point, projection));
        }
        return minimum;
    }

    private static void Execute(ICommand? command, object parameter)
    {
        if (command?.CanExecute(parameter) == true)
        {
            command.Execute(parameter);
        }
    }

    private static void OnGraphChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var canvas = (RouteGraphCanvas)dependencyObject;
        canvas._viewInitialized = false;
        canvas._renderCacheDirty = true;
        canvas.InvalidateVisual();
    }

    private static void OnMapNameChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var canvas = (RouteGraphCanvas)dependencyObject;
        canvas._backgroundLoadVersion++;
        canvas._loadedBackgroundMapName = string.Empty;
        canvas._loadingBackgroundMapName = string.Empty;
        canvas._mapBackground = null;
        canvas._viewInitialized = false;
        canvas.InvalidateVisual();
    }

    private static void OnPathDrawingChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var canvas = (RouteGraphCanvas)dependencyObject;
        canvas._isDrawingStroke = false;
        canvas.Cursor = (bool)args.NewValue ? Cursors.Cross : Cursors.Arrow;
        if (canvas.IsMouseCaptured)
        {
            canvas.ReleaseMouseCapture();
        }
        canvas.InvalidateVisual();
    }

    private static Pen FrozenPen(Color color, double thickness)
    {
        var pen = new Pen(FrozenBrush(color), thickness);
        pen.Freeze();
        return pen;
    }

    private static Brush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private sealed record EdgeRenderItem(
        RouteNavigationEdge Edge,
        IReadOnlyList<RouteGraphPoint> Points,
        Rect Bounds);
}
