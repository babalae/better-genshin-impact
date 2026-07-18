using BetterGenshinImpact.GameTask.AutoPathing.Telemetry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace BetterGenshinImpact.View.Controls;

/// <summary>A lightweight renderer for large route graphs. It avoids creating one WPF element per node or edge.</summary>
public sealed class RouteGraphCanvas : FrameworkElement
{
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

    public static readonly DependencyProperty SelectNodeCommandProperty = DependencyProperty.Register(
        nameof(SelectNodeCommand), typeof(ICommand), typeof(RouteGraphCanvas));

    public static readonly DependencyProperty SelectEdgeCommandProperty = DependencyProperty.Register(
        nameof(SelectEdgeCommand), typeof(ICommand), typeof(RouteGraphCanvas));

    public static readonly DependencyProperty SelectTeleportCommandProperty = DependencyProperty.Register(
        nameof(SelectTeleportCommand), typeof(ICommand), typeof(RouteGraphCanvas));

    private static readonly Pen NormalEdgePen = FrozenPen(Color.FromArgb(90, 80, 150, 230), 1);
    private static readonly Pen VerifiedEdgePen = FrozenPen(Color.FromArgb(180, 60, 190, 110), 1.4);
    private static readonly Pen RiskyEdgePen = FrozenPen(Color.FromArgb(190, 245, 160, 45), 1.5);
    private static readonly Pen DisabledEdgePen = FrozenPen(Color.FromArgb(100, 220, 70, 70), 1);
    private static readonly Pen SelectedEdgePen = FrozenPen(Colors.Cyan, 3);
    private static readonly Brush NodeBrush = FrozenBrush(Color.FromArgb(220, 190, 210, 235));
    private static readonly Brush TeleportEntryBrush = FrozenBrush(Color.FromArgb(240, 255, 170, 35));
    private static readonly Brush TeleportBrush = FrozenBrush(Color.FromArgb(240, 255, 220, 50));
    private static readonly Brush SelectedBrush = FrozenBrush(Colors.Cyan);

    private double _scale = 1;
    private Vector _offset;
    private bool _viewInitialized;
    private bool _isPanning;
    private Point _lastMousePoint;

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

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(new SolidColorBrush(Color.FromRgb(20, 25, 32)), null, new Rect(RenderSize));
        EnsureView();
        var nodes = Nodes ?? [];
        var nodeById = nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.NodeId))
            .GroupBy(node => node.NodeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var edge in Edges ?? [])
        {
            var points = _scale < 0.08 &&
                         nodeById.TryGetValue(edge.FromNodeId, out var lodFrom) &&
                         nodeById.TryGetValue(edge.ToNodeId, out var lodTo)
                ? (IReadOnlyList<RouteGraphPoint>)
                [
                    new RouteGraphPoint(lodFrom.X, lodFrom.Y),
                    new RouteGraphPoint(lodTo.X, lodTo.Y)
                ]
                : ResolvePoints(edge, nodeById);
            if (points.Count < 2)
            {
                continue;
            }

            var pen = ReferenceEquals(edge, SelectedEdge) ? SelectedEdgePen : ResolveEdgePen(edge);
            for (var index = 1; index < points.Count; index++)
            {
                drawingContext.DrawLine(pen, ToScreen(points[index - 1]), ToScreen(points[index]));
            }
        }

        foreach (var node in nodes)
        {
            var selected = ReferenceEquals(node, SelectedNode);
            var radius = selected ? 5 : node.AnchorIds.Count > 0 ? 3.5 : 2;
            drawingContext.DrawEllipse(
                selected ? SelectedBrush : node.AnchorIds.Count > 0 ? TeleportEntryBrush : NodeBrush,
                null,
                ToScreen(new RouteGraphPoint(node.X, node.Y)),
                radius,
                radius);
        }

        foreach (var teleport in Teleports ?? [])
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

    private void EnsureView()
    {
        if (_viewInitialized || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var points = (Nodes ?? []).Select(node => new RouteGraphPoint(node.X, node.Y))
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
        var tolerance = 9 / Math.Max(0.001, _scale);
        var node = (Nodes ?? []).Select(item => new
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

        var nodesById = (Nodes ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.NodeId))
            .GroupBy(item => item.NodeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var edge = (Edges ?? []).Select(item => new
            {
                Item = item,
                Distance = DistanceToPolyline(world, ResolvePoints(item, nodesById))
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
}
