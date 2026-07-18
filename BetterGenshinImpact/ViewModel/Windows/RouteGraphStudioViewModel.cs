using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.AutoPathing.Telemetry;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BetterGenshinImpact.ViewModel.Windows;

public partial class RouteGraphStudioViewModel : ViewModel
{
    private const string All = "全部";
    private readonly string _graphDirectory;
    private readonly RouteNavigationGraphProvider _provider;
    private readonly RouteGraphOverrideStore _overrideStore;
    private readonly RouteGraphQualityAnalyzer _qualityAnalyzer = new();
    private readonly List<RouteGraphOverrideOperation> _pendingOperations = [];
    private RouteNavigationGraphSnapshot _snapshot = RouteNavigationGraphSnapshot.Empty;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "等待加载路网";
    [ObservableProperty] private string _filterMap = All;
    [ObservableProperty] private string _filterLayer = All;
    [ObservableProperty] private string _filterReview = All;
    [ObservableProperty] private string _filterSource = string.Empty;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private List<RouteNavigationNode> _visibleNodes = [];
    [ObservableProperty] private List<RouteNavigationEdge> _visibleEdges = [];
    [ObservableProperty] private List<RouteGraphTeleportEntry> _visibleTeleports = [];
    [ObservableProperty] private RouteNavigationNode? _selectedNode;
    [ObservableProperty] private RouteNavigationEdge? _selectedEdge;
    [ObservableProperty] private RouteGraphTeleportEntry? _selectedTeleport;
    [ObservableProperty] private RouteGraphQualityIssue? _selectedQualityIssue;
    [ObservableProperty] private RouteNavigationNode? _connectionStartNode;
    [ObservableProperty] private double _nodeX;
    [ObservableProperty] private double _nodeY;
    [ObservableProperty] private string _nodeLayer = "surface";
    [ObservableProperty] private string _nodeType = "path";
    [ObservableProperty] private string _nodeAreaTag = string.Empty;
    [ObservableProperty] private int? _nodeFloor;
    [ObservableProperty] private bool _nodeUnderground;
    [ObservableProperty] private string _edgeMoveMode = MoveModeEnum.Walk.Code;
    [ObservableProperty] private bool _addBidirectionalEdge = true;
    [ObservableProperty] private string _patchAuthor = Environment.UserName;
    [ObservableProperty] private string _patchReason = "人工审核路网";
    [ObservableProperty] private int _pendingOperationCount;
    [ObservableProperty] private string _selectionSummary = "未选择对象";
    [ObservableProperty] private RouteGraphPoint? _currentTargetPoint;
    [ObservableProperty] private string _selectedNodeAnchorsText = "无";

    public RouteGraphStudioViewModel(
        string? graphDirectory = null,
        string? initialMapName = null,
        RouteGraphPoint? currentTargetPoint = null)
    {
        _graphDirectory = string.IsNullOrWhiteSpace(graphDirectory)
            ? Global.Absolute(Path.Combine("User", "AutoPathing", "Routes"))
            : graphDirectory;
        _provider = new RouteNavigationGraphProvider(_graphDirectory);
        _overrideStore = new RouteGraphOverrideStore(_graphDirectory);
        CurrentTargetPoint = currentTargetPoint;
        FilterMap = string.IsNullOrWhiteSpace(initialMapName)
            ? All
            : RouteGraphGeometry.NormalizeMapName(initialMapName);
        MapOptions.Add(All);
        LayerOptions.Add(All);
        ReviewOptions.Add(All);
        foreach (var review in Enum.GetNames<GraphReviewStatus>())
        {
            ReviewOptions.Add(review);
        }
    }

    public ObservableCollection<string> MapOptions { get; } = [];

    public ObservableCollection<string> LayerOptions { get; } = [];

    public ObservableCollection<string> ReviewOptions { get; } = [];

    public ObservableCollection<RouteGraphQualityIssue> QualityIssues { get; } = [];

    public string GraphFilePath => _provider.GraphFilePath;

    public string OverrideDirectoryPath => _overrideStore.DirectoryPath;

    partial void OnSelectedNodeChanged(RouteNavigationNode? value)
    {
        if (value == null)
        {
            return;
        }
        NodeX = value.X;
        NodeY = value.Y;
        NodeLayer = value.LayerId;
        NodeType = value.NodeType;
        NodeAreaTag = value.AreaTag;
        NodeFloor = value.Floor;
        NodeUnderground = value.Underground;
        SelectedNodeAnchorsText = value.AnchorIds.Count == 0 ? "无" : string.Join(", ", value.AnchorIds.Order());
        SelectionSummary = $"节点 {value.NodeId}｜{value.MapName}｜{value.NodeType}｜传送关联 {value.AnchorIds.Count}";
    }

    partial void OnSelectedEdgeChanged(RouteNavigationEdge? value)
    {
        if (value == null)
        {
            return;
        }
        EdgeMoveMode = value.MoveMode;
        SelectionSummary = $"边 {value.EdgeId}｜{value.FromNodeId} → {value.ToNodeId}｜{value.ReviewStatus}｜{value.SourceKind}";
    }

    partial void OnSelectedTeleportChanged(RouteGraphTeleportEntry? value)
    {
        if (value != null)
        {
            SelectionSummary = $"传送点 {value.Name}｜{value.AnchorId}｜出生 ({value.SpawnImagePoint.X:F1}, {value.SpawnImagePoint.Y:F1})";
        }
    }

    partial void OnSelectedQualityIssueChanged(RouteGraphQualityIssue? value)
    {
        if (value == null)
        {
            return;
        }
        if (!string.IsNullOrWhiteSpace(value.EdgeId))
        {
            SelectedEdge = _snapshot.Edges.FirstOrDefault(edge => Same(edge.EdgeId, value.EdgeId));
        }
        else if (!string.IsNullOrWhiteSpace(value.NodeId))
        {
            SelectedNode = _snapshot.Nodes.FirstOrDefault(node => Same(node.NodeId, value.NodeId));
        }
    }

    public async Task InitializeAsync()
    {
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusText = "正在加载生成路网和人工补丁...";
        try
        {
            var loaded = await Task.Run(() =>
            {
                var succeeded = _provider.TryGetSnapshot(out var snapshot, out var status, true);
                return (succeeded, snapshot, status);
            });
            if (!loaded.succeeded)
            {
                _snapshot = RouteNavigationGraphSnapshot.Empty;
                VisibleNodes = [];
                VisibleEdges = [];
                VisibleTeleports = [];
                StatusText = loaded.status switch
                {
                    RouteNavigationGraphLoadStatus.FileMissing => $"路网文件不存在：{GraphFilePath}",
                    RouteNavigationGraphLoadStatus.Invalid => "路网或补丁 JSON 无效",
                    _ => "路网为空"
                };
                return;
            }

            _snapshot = loaded.snapshot;
            _pendingOperations.Clear();
            PendingOperationCount = 0;
            RefreshFilterOptions();
            ApplyFilters();
            RunQualityCheck();
            var apply = _provider.LastOverrideApplyResult;
            StatusText = $"已加载 {_snapshot.Nodes.Count:N0} 节点 / {_snapshot.Edges.Count:N0} 边 / {_snapshot.Teleports.Count:N0} 传送点；" +
                         $"应用 {apply.AppliedPatchIds.Count} 个补丁，隔离 {apply.IsolatedPatchIds.Count} 个，错误 {apply.Errors.Count} 个";
        }
        catch (Exception ex)
        {
            StatusText = $"路网加载失败：{ex.Message}";
            await ThemedMessageBox.ErrorAsync(StatusText, "路网工作室");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ApplyFilters()
    {
        var map = FilterMap;
        var layer = FilterLayer;
        var search = SearchText.Trim();
        var source = FilterSource.Trim();
        var visibleNodes = _snapshot.Nodes.Where(node =>
                (map == All || Same(node.MapName, map)) &&
                (layer == All || Same(node.LayerId, layer)) &&
                (search.Length == 0 || Contains(node.NodeId, search) || Contains(node.AreaTag, search) || Contains(node.NodeType, search)))
            .ToList();
        var nodeIds = visibleNodes.Select(node => node.NodeId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visibleEdges = _snapshot.Edges.Where(edge =>
                nodeIds.Contains(edge.FromNodeId) && nodeIds.Contains(edge.ToNodeId) &&
                (FilterReview == All || Same(edge.ReviewStatus.ToString(), FilterReview)) &&
                (source.Length == 0 || Contains(edge.SourceKind, source) || Contains(edge.SourceFileName, source) ||
                 edge.Sources.Any(item => Contains(item.Repository, source) || Contains(item.FileName, source))) &&
                (search.Length == 0 || Contains(edge.EdgeId, search) || Contains(edge.SourceFileName, search)))
            .ToList();
        VisibleNodes = visibleNodes;
        VisibleEdges = visibleEdges;
        VisibleTeleports = _snapshot.Teleports.Where(teleport =>
            (map == All || Same(teleport.MapName, map)) &&
            (search.Length == 0 || Contains(teleport.Name, search) || Contains(teleport.AnchorId, search))).ToList();
    }

    [RelayCommand]
    private void SelectNode(RouteNavigationNode? node)
    {
        SelectedNode = node;
        SelectedEdge = null;
        if (node == null)
        {
            return;
        }

        NodeX = node.X;
        NodeY = node.Y;
        NodeLayer = node.LayerId;
        NodeType = node.NodeType;
        NodeAreaTag = node.AreaTag;
        NodeFloor = node.Floor;
        NodeUnderground = node.Underground;
        SelectionSummary = $"节点 {node.NodeId}｜{node.MapName}｜{node.NodeType}｜传送关联 {node.AnchorIds.Count}";
    }

    [RelayCommand]
    private void SelectEdge(RouteNavigationEdge? edge)
    {
        SelectedEdge = edge;
        SelectedNode = null;
        if (edge == null)
        {
            return;
        }

        EdgeMoveMode = edge.MoveMode;
        SelectionSummary = $"边 {edge.EdgeId}｜{edge.FromNodeId} → {edge.ToNodeId}｜{edge.ReviewStatus}｜{edge.SourceKind}";
    }

    [RelayCommand]
    private void SelectTeleport(RouteGraphTeleportEntry? teleport)
    {
        SelectedTeleport = teleport;
        if (teleport != null)
        {
            SelectionSummary = $"传送点 {teleport.Name}｜{teleport.AnchorId}｜出生 ({teleport.SpawnImagePoint.X:F1}, {teleport.SpawnImagePoint.Y:F1})";
        }
    }

    [RelayCommand]
    private void ApplyNodeEdit()
    {
        if (SelectedNode == null)
        {
            return;
        }

        if (!NodeX.Equals(SelectedNode.X) || !NodeY.Equals(SelectedNode.Y))
        {
            Stage(new RouteGraphOverrideOperation
            {
                Type = RouteGraphOverrideOperationType.MoveNode,
                NodeId = SelectedNode.NodeId,
                X = NodeX,
                Y = NodeY
            });
            SelectedNode.X = NodeX;
            SelectedNode.Y = NodeY;
        }

        if (!Same(NodeLayer, SelectedNode.LayerId) || NodeFloor != SelectedNode.Floor ||
            NodeUnderground != SelectedNode.Underground || !Same(NodeAreaTag, SelectedNode.AreaTag))
        {
            Stage(new RouteGraphOverrideOperation
            {
                Type = RouteGraphOverrideOperationType.SetNodeLayer,
                NodeId = SelectedNode.NodeId,
                LayerId = NodeLayer,
                Floor = NodeFloor,
                Underground = NodeUnderground,
                AreaTag = NodeAreaTag
            });
            SelectedNode.LayerId = NodeLayer;
            SelectedNode.Floor = NodeFloor;
            SelectedNode.Underground = NodeUnderground;
            SelectedNode.AreaTag = NodeAreaTag;
        }

        if (!Same(NodeType, SelectedNode.NodeType))
        {
            Stage(new RouteGraphOverrideOperation
            {
                Type = RouteGraphOverrideOperationType.SetNodeType,
                NodeId = SelectedNode.NodeId,
                NodeType = string.IsNullOrWhiteSpace(NodeType) ? "path" : NodeType
            });
            SelectedNode.NodeType = string.IsNullOrWhiteSpace(NodeType) ? "path" : NodeType;
        }

        ApplyFilters();
    }

    [RelayCommand]
    private void AddNode()
    {
        var map = FilterMap == All ? "Teyvat" : FilterMap;
        var node = new RouteNavigationNode
        {
            NodeId = "manual_" + Guid.NewGuid().ToString("N")[..16],
            MapName = map,
            X = NodeX,
            Y = NodeY,
            LayerId = string.IsNullOrWhiteSpace(NodeLayer) || NodeLayer == All ? "surface" : NodeLayer,
            NodeType = string.IsNullOrWhiteSpace(NodeType) ? "path" : NodeType,
            AreaTag = NodeAreaTag,
            Floor = NodeFloor,
            Underground = NodeUnderground
        };
        Stage(new RouteGraphOverrideOperation { Type = RouteGraphOverrideOperationType.AddNode, Node = node });
        _snapshot.Graph.Nodes.Add(node);
        ApplyFilters();
        SelectNode(node);
    }

    [RelayCommand]
    private void DeleteNode()
    {
        if (SelectedNode == null)
        {
            return;
        }

        var nodeId = SelectedNode.NodeId;
        Stage(new RouteGraphOverrideOperation { Type = RouteGraphOverrideOperationType.DeleteNode, NodeId = nodeId });
        _snapshot.Graph.Edges.RemoveAll(edge => Same(edge.FromNodeId, nodeId) || Same(edge.ToNodeId, nodeId));
        _snapshot.Graph.Nodes.Remove(SelectedNode);
        SelectedNode = null;
        ApplyFilters();
    }

    [RelayCommand]
    private void SetConnectionStart()
    {
        ConnectionStartNode = SelectedNode;
        if (ConnectionStartNode != null)
        {
            StatusText = $"连接起点：{ConnectionStartNode.NodeId}；请选择终点后点击“新增连接”";
        }
    }

    [RelayCommand]
    private void AddConnection()
    {
        if (ConnectionStartNode == null || SelectedNode == null || ReferenceEquals(ConnectionStartNode, SelectedNode))
        {
            return;
        }

        AddManualEdge(ConnectionStartNode, SelectedNode);
        if (AddBidirectionalEdge)
        {
            AddManualEdge(SelectedNode, ConnectionStartNode);
        }
        ApplyFilters();
    }

    [RelayCommand]
    private void DeleteEdge()
    {
        if (SelectedEdge == null)
        {
            return;
        }

        Stage(new RouteGraphOverrideOperation { Type = RouteGraphOverrideOperationType.DeleteEdge, EdgeId = SelectedEdge.EdgeId });
        _snapshot.Graph.Edges.Remove(SelectedEdge);
        SelectedEdge = null;
        ApplyFilters();
    }

    [RelayCommand]
    private void MarkEdgeVerified() => SetEdgeReview(GraphReviewStatus.Verified);

    [RelayCommand]
    private void MarkEdgeRisky() => SetEdgeReview(GraphReviewStatus.Risky);

    [RelayCommand]
    private void DisableEdge() => SetEdgeReview(GraphReviewStatus.Disabled);

    [RelayCommand]
    private void RestoreEdge()
    {
        if (SelectedEdge == null)
        {
            return;
        }
        Stage(new RouteGraphOverrideOperation { Type = RouteGraphOverrideOperationType.RestoreEdge, EdgeId = SelectedEdge.EdgeId });
        SelectedEdge.ReviewStatus = GraphReviewStatus.Unreviewed;
        SelectedEdge.HealthStatus = RouteHealthStatus.Unknown;
        ApplyFilters();
    }

    [RelayCommand]
    private void AssociateTeleport()
    {
        if (SelectedNode == null || SelectedTeleport == null || SelectedNode.AnchorIds.Contains(SelectedTeleport.AnchorId))
        {
            return;
        }
        Stage(new RouteGraphOverrideOperation
        {
            Type = RouteGraphOverrideOperationType.AssociateTeleport,
            NodeId = SelectedNode.NodeId,
            TeleportAnchorId = SelectedTeleport.AnchorId
        });
        SelectedNode.AnchorIds.Add(SelectedTeleport.AnchorId);
        SelectedNodeAnchorsText = string.Join(", ", SelectedNode.AnchorIds.Order());
        SelectionSummary = $"已暂存关联：{SelectedTeleport.Name} → {SelectedNode.NodeId}";
    }

    [RelayCommand]
    private void RemoveTeleportAssociation()
    {
        if (SelectedNode == null || SelectedTeleport == null || !SelectedNode.AnchorIds.Contains(SelectedTeleport.AnchorId))
        {
            return;
        }
        Stage(new RouteGraphOverrideOperation
        {
            Type = RouteGraphOverrideOperationType.RemoveTeleportAssociation,
            NodeId = SelectedNode.NodeId,
            TeleportAnchorId = SelectedTeleport.AnchorId
        });
        SelectedNode.AnchorIds.Remove(SelectedTeleport.AnchorId);
        SelectedNodeAnchorsText = SelectedNode.AnchorIds.Count == 0 ? "无" : string.Join(", ", SelectedNode.AnchorIds.Order());
        SelectionSummary = $"已暂存取消关联：{SelectedTeleport.Name} / {SelectedNode.NodeId}";
    }

    [RelayCommand]
    private async Task SavePatchAsync()
    {
        if (_pendingOperations.Count == 0)
        {
            StatusText = "没有待保存的人工修正";
            return;
        }

        try
        {
            var patch = new RouteGraphOverridePatch
            {
                Id = $"graph-fix-{DateTime.UtcNow:yyyyMMdd-HHmmss}",
                BaseGraphId = _snapshot.Graph.GraphId,
                Author = PatchAuthor.Trim(),
                Reason = PatchReason.Trim(),
                CreatedAtUtc = DateTime.UtcNow,
                Operations = _pendingOperations.ToList()
            };
            var path = _overrideStore.Save(patch);
            StatusText = $"人工修正已保存：{path}";
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"补丁保存失败：{ex.Message}";
            await ThemedMessageBox.ErrorAsync(StatusText, "路网工作室");
        }
    }

    [RelayCommand]
    private void RunQualityCheck()
    {
        var issues = _qualityAnalyzer.Analyze(_snapshot.Graph, _snapshot.Teleports);
        QualityIssues.Clear();
        foreach (var issue in issues)
        {
            QualityIssues.Add(issue);
        }
    }

    [RelayCommand]
    private void SelectQualityIssue(RouteGraphQualityIssue? issue)
    {
        SelectedQualityIssue = issue;
        if (issue == null)
        {
            return;
        }
        if (!string.IsNullOrWhiteSpace(issue.EdgeId))
        {
            SelectEdge(_snapshot.Edges.FirstOrDefault(edge => Same(edge.EdgeId, issue.EdgeId)));
        }
        else if (!string.IsNullOrWhiteSpace(issue.NodeId))
        {
            SelectNode(_snapshot.Nodes.FirstOrDefault(node => Same(node.NodeId, issue.NodeId)));
        }
    }

    [RelayCommand]
    private void ValidatePlan()
    {
        if (ConnectionStartNode == null || SelectedNode == null)
        {
            StatusText = "请先设置连接起点，再选择一个目标节点";
            return;
        }
        var planner = new RouteNavigationPlanner(_provider);
        var succeeded = planner.TryPlan(
            new RouteNavigationPlanRequest
            {
                MapName = ConnectionStartNode.MapName,
                CurrentImagePoint = new RouteGraphPoint(ConnectionStartNode.X, ConnectionStartNode.Y),
                TargetImagePoint = new RouteGraphPoint(SelectedNode.X, SelectedNode.Y),
                TaskName = "路网工作室规划验证"
            },
            out var plan,
            new RouteNavigationPlanOptions { AllowTeleport = false });
        StatusText = succeeded
            ? $"规划验证：{plan.CompletionMode}，{plan.Edges.Count} 条边，预计 {plan.Cost:F1} 秒"
            : $"规划验证失败：{plan.FailureCode} / {plan.FailureReason}";
    }

    [RelayCommand]
    private void OpenGraphDirectory()
    {
        Directory.CreateDirectory(_graphDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", _graphDirectory) { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenOverrideDirectory()
    {
        Directory.CreateDirectory(OverrideDirectoryPath);
        Process.Start(new ProcessStartInfo("explorer.exe", OverrideDirectoryPath) { UseShellExecute = true });
    }

    private void AddManualEdge(RouteNavigationNode from, RouteNavigationNode to)
    {
        var edge = new RouteNavigationEdge
        {
            EdgeId = "manual_edge_" + Guid.NewGuid().ToString("N")[..16],
            SegmentId = "manual",
            FromNodeId = from.NodeId,
            ToNodeId = to.NodeId,
            MapName = from.MapName,
            MoveMode = string.IsNullOrWhiteSpace(EdgeMoveMode) ? MoveModeEnum.Walk.Code : EdgeMoveMode,
            ReviewStatus = GraphReviewStatus.Unreviewed,
            SourceKind = "manual-override",
            SourceAuthor = PatchAuthor,
            Sources =
            [
                new RouteNavigationEdgeSource
                {
                    Author = PatchAuthor,
                    Kind = "manual-override"
                }
            ],
            Points =
            [
                new TelemetryPoint2D { X = (float)from.X, Y = (float)from.Y },
                new TelemetryPoint2D { X = (float)to.X, Y = (float)to.Y }
            ]
        };
        Stage(new RouteGraphOverrideOperation { Type = RouteGraphOverrideOperationType.AddEdge, Edge = edge });
        _snapshot.Graph.Edges.Add(edge);
    }

    private void SetEdgeReview(GraphReviewStatus status)
    {
        if (SelectedEdge == null)
        {
            return;
        }
        Stage(new RouteGraphOverrideOperation
        {
            Type = RouteGraphOverrideOperationType.SetEdgeReview,
            EdgeId = SelectedEdge.EdgeId,
            ReviewStatus = status
        });
        SelectedEdge.ReviewStatus = status;
        if (status is GraphReviewStatus.Disabled or GraphReviewStatus.Rejected)
        {
            SelectedEdge.HealthStatus = RouteHealthStatus.Disabled;
        }
        else if (status == GraphReviewStatus.Verified)
        {
            SelectedEdge.HealthStatus = RouteHealthStatus.Verified;
            SelectedEdge.LastVerifiedAtUtc = DateTime.UtcNow;
        }
        else if (status == GraphReviewStatus.Risky)
        {
            SelectedEdge.HealthStatus = RouteHealthStatus.Risky;
        }
        ApplyFilters();
    }

    private void Stage(RouteGraphOverrideOperation operation)
    {
        _pendingOperations.Add(operation);
        PendingOperationCount = _pendingOperations.Count;
        StatusText = $"已暂存 {PendingOperationCount} 项人工修正，尚未写入补丁";
    }

    private void RefreshFilterOptions()
    {
        ReplaceOptions(MapOptions, _snapshot.Nodes.Select(node => RouteGraphGeometry.NormalizeMapName(node.MapName)));
        ReplaceOptions(LayerOptions, _snapshot.Nodes.Select(node => string.IsNullOrWhiteSpace(node.LayerId) ? "surface" : node.LayerId));
        if (!MapOptions.Contains(FilterMap)) FilterMap = All;
        if (!LayerOptions.Contains(FilterLayer)) FilterLayer = All;
    }

    private static void ReplaceOptions(ObservableCollection<string> target, IEnumerable<string> values)
    {
        target.Clear();
        target.Add(All);
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Order())
        {
            target.Add(value);
        }
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string? value, string search) =>
        !string.IsNullOrWhiteSpace(value) && value.Contains(search, StringComparison.OrdinalIgnoreCase);
}
