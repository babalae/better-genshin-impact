using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Telemetry;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.Helpers.Extensions;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using OpenCvSharp;

namespace BetterGenshinImpact.ViewModel.Windows;

public partial class MapPathingDevViewModel : ObservableObject
{
    private readonly string _routeSaveDir = Global.Absolute(Path.Combine("User", "AutoPathing", "Routes"));
    private readonly RouteNavigationGraphProvider _graphProvider;
    private readonly RouteNavigationPlanner _routeNavigationPlanner;
    private MapViewer? _mapViewer;
    private bool _isRefreshingLite;

    public IEnumerable<EnumItem<DisplayMapTypes>> MapTypeItems { get; } = EnumExtensions.ToEnumItems<DisplayMapTypes>();

    public DevConfig DevConfig { get; set; } = TaskContext.Instance().Config.DevConfig;

    public ObservableCollection<RoutePlanEdgeRow> PlannedEdges { get; } = [];

    public ObservableCollection<RouteNearbyNodeRow> NearbyNodes { get; } = [];

    public ObservableCollection<RouteNearbyEdgeRow> NearbyEdges { get; } = [];

    public ObservableCollection<RouteHealthRow> HealthRows { get; } = [];

    public string GraphFilePath => Path.Combine(_routeSaveDir, RouteNavigationGraphBuilder.GraphFileName);

    public string HealthFilePath => Path.Combine(_routeSaveDir, "route_health.json");

    [ObservableProperty]
    private double _currentImageX = 1024;

    [ObservableProperty]
    private double _currentImageY = 1024;

    [ObservableProperty]
    private bool _followCurrentPosition = true;

    [ObservableProperty]
    private double _targetImageX = 1200;

    [ObservableProperty]
    private double _targetImageY = 1200;

    [ObservableProperty]
    private bool _allowTeleport = true;

    [ObservableProperty]
    private bool _allowUnknownStartConnector = true;

    [ObservableProperty]
    private bool _allowUnknownTargetConnector = true;

    [ObservableProperty]
    private bool _allowDisabledEdges;

    [ObservableProperty]
    private string _targetMoveMode = string.Empty;

    [ObservableProperty]
    private string _targetAction = string.Empty;

    [ObservableProperty]
    private string _planSummary = "等待规划";

    [ObservableProperty]
    private string _graphStatus = string.Empty;

    [ObservableProperty]
    private string _graphSummary = "等待刷新";

    [ObservableProperty]
    private string _healthSummary = "等待刷新";

    [ObservableProperty]
    private string _targetPickSummary = "点击实时追踪地图可选择目标点";

    [ObservableProperty]
    private bool _hasPlan;

    [ObservableProperty]
    private bool _isPlanning;

    [ObservableProperty]
    private bool _isRefreshing;

    public MapPathingDevViewModel()
    {
        _graphProvider = new RouteNavigationGraphProvider(_routeSaveDir);
        _routeNavigationPlanner = new RouteNavigationPlanner(_graphProvider);

        WeakReferenceMessenger.Default.Register<PropertyChangedMessage<object>>(this, (_, msg) =>
        {
            if (msg.PropertyName == "SelectPathingTargetPosition" && msg.NewValue is Point2f targetPoint)
            {
                UIDispatcherHelper.Invoke(() =>
                {
                    TargetImageX = Math.Round(targetPoint.X, 1);
                    TargetImageY = Math.Round(targetPoint.Y, 1);
                    TargetPickSummary = $"目标点：{TargetImageX:F1}, {TargetImageY:F1}";
                    _ = RefreshDiagnosticsLiteAsync();
                });
                return;
            }

            if (msg.PropertyName != "SendCurrentPosition" || msg.NewValue is not Point2f point)
            {
                return;
            }

            if (!FollowCurrentPosition)
            {
                return;
            }

            UIDispatcherHelper.Invoke(() =>
            {
                CurrentImageX = Math.Round(point.X, 1);
                CurrentImageY = Math.Round(point.Y, 1);
            });
        });

        _ = RefreshDiagnosticsLiteAsync();
    }

    [RelayCommand]
    private void DropDownChanged()
    {
        // The ComboBox writes directly to DevConfig.RecordMapName.
    }

    [RelayCommand]
    private void OpenMapViewer()
    {
        if (_mapViewer == null || !_mapViewer.IsVisible)
        {
            _mapViewer = new MapViewer(DevConfig.RecordMapName);
            _mapViewer.Closed += (_, _) => _mapViewer = null;
            _mapViewer.Show();
        }
        else
        {
            _mapViewer.Activate();
        }
    }

    [RelayCommand]
    private void OpenMapEditor()
    {
        PathRecorder.Instance.OpenEditorInWebView(DevConfig.RecordMapName);
    }

    [RelayCommand]
    private async Task RebuildGraphAsync()
    {
        if (IsRefreshing)
        {
            return;
        }

        IsRefreshing = true;
        GraphSummary = "正在重建路网...";
        try
        {
            await Task.Run(() =>
            {
                var healthEntries = new RouteHealthStore(_routeSaveDir).GetSnapshot();
                new RouteNavigationGraphBuilder(_routeSaveDir).BuildNow(healthEntries);
            });

            IsRefreshing = false;
            await RefreshGraphDiagnosticsAsync();
        }
        catch (Exception ex)
        {
            GraphSummary = $"重建失败：{ex.Message}";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private async Task RefreshDiagnosticsLiteAsync()
    {
        if (_isRefreshingLite)
        {
            return;
        }

        _isRefreshingLite = true;
        HealthSummary = "正在后台刷新健康数据...";
        var mapName = DevConfig.RecordMapName;

        try
        {
            var result = await Task.Run(() => BuildLiteDiagnostics(mapName));

            HealthSummary = result.HealthSummary;
            GraphSummary = result.GraphSummary;

            NearbyNodes.Clear();
            NearbyEdges.Clear();
            HealthRows.Clear();

            foreach (var row in result.HealthRows)
            {
                HealthRows.Add(row);
            }
        }
        catch (Exception ex)
        {
            HealthSummary = $"健康数据刷新失败：{ex.Message}";
        }
        finally
        {
            _isRefreshingLite = false;
        }
    }

    private RouteLiteDiagnosticsResult BuildLiteDiagnostics(string mapName)
    {
        var graphExists = File.Exists(GraphFilePath);
        var graphSizeMb = graphExists ? new FileInfo(GraphFilePath).Length / 1024.0 / 1024.0 : 0;
        var healthExists = File.Exists(HealthFilePath);
        var telemetryCount = Directory.Exists(_routeSaveDir)
            ? Directory.EnumerateFiles(_routeSaveDir, "*_Telemetry.json", SearchOption.TopDirectoryOnly).Count()
            : 0;

        var healthEntries = new RouteHealthStore(_routeSaveDir).GetSnapshot();
        var healthSummary = healthExists
            ? $"Health {healthEntries.Count} 条，Telemetry {telemetryCount} 个文件"
            : $"Health 文件不存在，Telemetry {telemetryCount} 个文件";
        var graphSummary = graphExists
            ? $"Graph 文件 {graphSizeMb:F1} MB，点击“查询附近”加载路网"
            : "路网文件不存在";

        var normalizedMapName = RouteGraphGeometry.NormalizeMapName(mapName);
        var healthRows = healthEntries
                     .Where(e => string.Equals(RouteGraphGeometry.NormalizeMapName(e.MapName), normalizedMapName, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(e => e.Status == RouteHealthStatus.Disabled ? 0 : e.Status == RouteHealthStatus.Risky ? 1 : 2)
                     .ThenByDescending(e => e.FailureCount)
                     .ThenByDescending(e => e.LastSeenUtc)
                     .Take(80)
                     .Select(entry => new RouteHealthRow
            {
                SegmentId = ShortId(entry.SegmentId),
                Status = entry.Status,
                Success = entry.SuccessCount,
                Failure = entry.FailureCount,
                Rate = entry.SuccessRate,
                MoveMode = entry.MoveMode,
                Action = entry.Action,
                LastFailure = entry.LastFailureReason
            })
                     .ToList();

        return new RouteLiteDiagnosticsResult(graphSummary, healthSummary, healthRows);
    }

    [RelayCommand]
    private async Task RefreshGraphDiagnosticsAsync()
    {
        if (IsRefreshing)
        {
            return;
        }

        IsRefreshing = true;
        NearbyNodes.Clear();
        NearbyEdges.Clear();
        GraphSummary = "正在后台加载路网...";

        try
        {
            var targetPoint = new RouteGraphPoint(TargetImageX, TargetImageY);
            var result = await Task.Run(() =>
            {
                if (!_graphProvider.TryGetSnapshot(out var graph, forceReload: true) || graph.IsEmpty)
                {
                    return RouteGraphDiagnosticsResult.Empty(File.Exists(GraphFilePath) ? "路网为空或读取失败" : "路网文件不存在");
                }

                var normalizedMapName = RouteGraphGeometry.NormalizeMapName(DevConfig.RecordMapName);
                var mapNodeCount = graph.Nodes.Count(n => string.Equals(RouteGraphGeometry.NormalizeMapName(n.MapName), normalizedMapName, StringComparison.OrdinalIgnoreCase));
                var mapEdgeCount = graph.Edges.Count(e => string.Equals(RouteGraphGeometry.NormalizeMapName(e.MapName), normalizedMapName, StringComparison.OrdinalIgnoreCase));
                var nearbyNodes = graph.FindNearestNodes(DevConfig.RecordMapName, targetPoint, 8, 220)
                    .Select(candidate => new RouteNearbyNodeRow
                    {
                        NodeId = ShortId(candidate.Node.NodeId),
                        X = candidate.Node.X,
                        Y = candidate.Node.Y,
                        Distance = candidate.Distance,
                        Anchors = candidate.Node.AnchorIds.Count,
                        Resources = candidate.Node.ResourceIds.Count + candidate.Node.ResourceLabelIds.Count
                    })
                    .ToList();
                var nearbyEdges = graph.FindNearestEdges(DevConfig.RecordMapName, targetPoint, 8, 120)
                    .Select(projection => new RouteNearbyEdgeRow
                    {
                        EdgeId = ShortId(projection.Edge.EdgeId),
                        Distance = projection.Distance,
                        Cost = projection.Edge.Cost,
                        MoveMode = projection.Edge.MoveMode,
                        Action = projection.Edge.Action,
                        HealthStatus = projection.Edge.HealthStatus,
                        Reverse = projection.Edge.IsSyntheticReverse
                    })
                    .ToList();

                return new RouteGraphDiagnosticsResult(
                    $"当前地图 Nodes {mapNodeCount} / Edges {mapEdgeCount}；全局 Nodes {graph.Nodes.Count} / Edges {graph.Edges.Count} / Teleports {graph.Teleports.Count}",
                    nearbyNodes,
                    nearbyEdges);
            });

            GraphSummary = result.Summary;
            foreach (var node in result.NearbyNodes)
            {
                NearbyNodes.Add(node);
            }
            foreach (var edge in result.NearbyEdges)
            {
                NearbyEdges.Add(edge);
            }
        }
        catch (Exception ex)
        {
            GraphSummary = $"路网查询失败：{ex.Message}";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private void OpenRoutesFolder()
    {
        Directory.CreateDirectory(_routeSaveDir);
        Process.Start(new ProcessStartInfo(_routeSaveDir) { UseShellExecute = true });
    }

    [RelayCommand]
    private async Task PlanRouteAsync()
    {
        if (IsPlanning)
        {
            return;
        }

        PlannedEdges.Clear();
        HasPlan = false;
        IsPlanning = true;
        PlanSummary = "正在规划...";

        try
        {
            var request = new RouteNavigationPlanRequest
            {
                MapName = DevConfig.RecordMapName,
                CurrentImagePoint = new RouteGraphPoint(CurrentImageX, CurrentImageY),
                TargetImagePoint = new RouteGraphPoint(TargetImageX, TargetImageY),
                TaskName = "路网规划测试",
                TargetMoveMode = string.IsNullOrWhiteSpace(TargetMoveMode) ? null : TargetMoveMode.Trim(),
                TargetAction = string.IsNullOrWhiteSpace(TargetAction) ? null : TargetAction.Trim()
            };

            var options = new RouteNavigationPlanOptions
            {
                AllowTeleport = AllowTeleport,
                AllowUnknownStartConnector = AllowUnknownStartConnector,
                AllowUnknownTargetConnector = AllowUnknownTargetConnector,
                AllowDisabledEdges = AllowDisabledEdges
            };

            var result = await Task.Run(() =>
            {
                var succeeded = _routeNavigationPlanner.TryPlan(request, out var plannedRoute, options);
                return new RoutePlanRunResult(succeeded, plannedRoute);
            });

            var plan = result.Plan;

            if (!result.Succeeded)
            {
                PlanSummary = $"规划失败：{plan.FailureReason}";
                GraphStatus = File.Exists(GraphFilePath) ? GraphFilePath : "路网文件不存在";
                return;
            }

            HasPlan = true;
            var generatedTargetMoveMode = plan.Task?.Positions.LastOrDefault()?.MoveMode ?? "-";
            PlanSummary =
                $"成功：Cost {plan.Cost:F2}，Edges {plan.Edges.Count}，" +
                $"传送 {(plan.UsesTeleport ? "是" : "否")}，" +
                $"起点吸附 {plan.StartAttachDistance:F1}，终点吸附 {plan.TargetAttachDistance:F1}，" +
                $"终点模式 {generatedTargetMoveMode}";
            GraphStatus =
                $"StartUnknown {FormatBool(plan.RequiresUnknownStartConnector)} / " +
                $"TargetUnknown {FormatBool(plan.RequiresUnknownTargetConnector)} / " +
                $"Frontier {plan.FrontierNode?.NodeId ?? "-"}";

            for (var i = 0; i < plan.Edges.Count; i++)
            {
                var edge = plan.Edges[i];
                PlannedEdges.Add(new RoutePlanEdgeRow
                {
                    Index = i + 1,
                    FromNodeId = ShortId(edge.FromNodeId),
                    ToNodeId = ShortId(edge.ToNodeId),
                    Cost = edge.Cost,
                    MoveMode = edge.MoveMode,
                    Action = edge.Action,
                    HealthStatus = edge.HealthStatus,
                    IsSyntheticReverse = edge.IsSyntheticReverse,
                    IsBidirectionalCandidate = edge.IsBidirectionalCandidate
                });
            }

            if (_mapViewer == null || !_mapViewer.IsVisible)
            {
                OpenMapViewer();
            }

            if (plan.Task != null)
            {
                WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "UpdateCurrentPathing", new object(), plan.Task));
            }
        }
        catch (Exception ex)
        {
            PlanSummary = $"规划异常：{ex.Message}";
        }
        finally
        {
            IsPlanning = false;
        }
    }

    private static string FormatBool(bool value)
    {
        return value ? "是" : "否";
    }

    private static string ShortId(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return "-";
        }

        return nodeId.Length <= 14 ? nodeId : nodeId[..14];
    }
}

internal readonly record struct RoutePlanRunResult(bool Succeeded, RouteNavigationPlan Plan);

internal sealed record RouteLiteDiagnosticsResult(
    string GraphSummary,
    string HealthSummary,
    IReadOnlyList<RouteHealthRow> HealthRows);

internal sealed record RouteGraphDiagnosticsResult(
    string Summary,
    IReadOnlyList<RouteNearbyNodeRow> NearbyNodes,
    IReadOnlyList<RouteNearbyEdgeRow> NearbyEdges)
{
    public static RouteGraphDiagnosticsResult Empty(string summary)
    {
        return new RouteGraphDiagnosticsResult(summary, [], []);
    }
}

public sealed class RoutePlanEdgeRow
{
    public int Index { get; init; }

    public string FromNodeId { get; init; } = string.Empty;

    public string ToNodeId { get; init; } = string.Empty;

    public double Cost { get; init; }

    public string MoveMode { get; init; } = string.Empty;

    public string Action { get; init; } = string.Empty;

    public string HealthStatus { get; init; } = string.Empty;

    public bool IsSyntheticReverse { get; init; }

    public bool IsBidirectionalCandidate { get; init; }
}

public sealed class RouteNearbyNodeRow
{
    public string NodeId { get; init; } = string.Empty;

    public double X { get; init; }

    public double Y { get; init; }

    public double Distance { get; init; }

    public int Anchors { get; init; }

    public int Resources { get; init; }
}

public sealed class RouteNearbyEdgeRow
{
    public string EdgeId { get; init; } = string.Empty;

    public double Distance { get; init; }

    public double Cost { get; init; }

    public string MoveMode { get; init; } = string.Empty;

    public string Action { get; init; } = string.Empty;

    public string HealthStatus { get; init; } = string.Empty;

    public bool Reverse { get; init; }
}

public sealed class RouteHealthRow
{
    public string SegmentId { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public int Success { get; init; }

    public int Failure { get; init; }

    public double Rate { get; init; }

    public string MoveMode { get; init; } = string.Empty;

    public string Action { get; init; } = string.Empty;

    public string LastFailure { get; init; } = string.Empty;
}
