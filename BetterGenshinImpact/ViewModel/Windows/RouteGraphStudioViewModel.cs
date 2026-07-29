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
    private readonly HashSet<string> _draftCreatedNodeIds = new(StringComparer.OrdinalIgnoreCase);
    private RouteNavigationGraphSnapshot _snapshot = new(new RouteNavigationGraph(), 64, []);

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "等待加载路网";
    [ObservableProperty] private string _filterMap = "Teyvat";
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
    [ObservableProperty] private bool _isPathDrawing;
    [ObservableProperty] private List<RouteGraphPoint> _draftPathPoints = [];
    [ObservableProperty] private double _drawSnapDistance = 6;
    [ObservableProperty] private double _drawSimplificationTolerance = 1;

    public int DraftPathPointCount => DraftPathPoints.Count;

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
            ? "Teyvat"
            : RouteGraphGeometry.NormalizeMapName(initialMapName);
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

    public string CanvasMapName => FilterMap;

    partial void OnFilterMapChanged(string value)
    {
        ConnectionStartNode = null;
        if (IsPathDrawing || DraftPathPoints.Count > 0)
        {
            IsPathDrawing = false;
            DraftPathPoints = [];
            _draftCreatedNodeIds.Clear();
            OnPropertyChanged(nameof(DraftPathPointCount));
        }
        OnPropertyChanged(nameof(CanvasMapName));
        if (!_snapshot.IsEmpty)
        {
            ApplyFilters();
        }
    }

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
                _snapshot = new RouteNavigationGraphSnapshot(new RouteNavigationGraph(), 64, []);
                VisibleNodes = [];
                VisibleEdges = [];
                VisibleTeleports = [];
                StatusText = loaded.status switch
                {
                    RouteNavigationGraphLoadStatus.FileMissing => $"路网文件不存在：{GraphFilePath}",
                    RouteNavigationGraphLoadStatus.Invalid => string.IsNullOrWhiteSpace(_provider.LastLoadError)
                        ? "路网或补丁 JSON 无效"
                        : _provider.LastLoadError,
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
                         $"剔除 {_provider.LastSanitizedEdgeCount:N0} 条异常超长边；" +
                         $"应用 {apply.AppliedPatchIds.Count} 个补丁，隔离 {apply.IsolatedPatchIds.Count} 个，错误 {apply.Errors.Count} 个";
            if (apply.Errors.Count > 0)
            {
                StatusText += $"；{string.Join("；", apply.Errors.Take(3))}";
            }
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
                Same(node.MapName, map) &&
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
            Same(teleport.MapName, map) &&
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
        var map = FilterMap;
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
    private void StartPathDrawing()
    {
        DraftPathPoints = [];
        _draftCreatedNodeIds.Clear();
        OnPropertyChanged(nameof(DraftPathPointCount));
        IsPathDrawing = true;
        StatusText = "手绘模式：按住左键沿道路描线，或逐点单击；右键/中键拖动画布，滚轮缩放";
    }

    [RelayCommand]
    private void AddDrawPathPoint(RouteGraphPoint point)
    {
        if (!IsPathDrawing ||
            !double.IsFinite(point.X) ||
            !double.IsFinite(point.Y))
        {
            return;
        }

        if (RouteMapGeometryCatalog.TryGet(FilterMap, out var geometry) &&
            (point.X < 0 || point.Y < 0 || point.X > geometry.ImageWidth || point.Y > geometry.ImageHeight))
        {
            return;
        }

        if (DraftPathPoints.Count > 0 &&
            RouteGraphGeometry.Distance(DraftPathPoints[^1], point) < 0.5)
        {
            return;
        }

        DraftPathPoints = [.. DraftPathPoints, point];
        OnPropertyChanged(nameof(DraftPathPointCount));
        StatusText = $"手绘中：{DraftPathPointCount} 个采样点；完成后会吸附 {Math.Max(0, DrawSnapDistance):F1} 像素内的已有节点";
    }

    [RelayCommand]
    private void UndoDrawPathPoint()
    {
        if (DraftPathPoints.Count == 0)
        {
            return;
        }

        DraftPathPoints = DraftPathPoints.Take(DraftPathPoints.Count - 1).ToList();
        OnPropertyChanged(nameof(DraftPathPointCount));
        StatusText = $"已撤销最后一个手绘点，剩余 {DraftPathPointCount} 个";
    }

    [RelayCommand]
    private void CancelPathDrawing()
    {
        IsPathDrawing = false;
        DraftPathPoints = [];
        _draftCreatedNodeIds.Clear();
        OnPropertyChanged(nameof(DraftPathPointCount));
        StatusText = "已取消手绘，未产生路网补丁";
    }

    [RelayCommand]
    private void FinishPathDrawing()
    {
        if (DraftPathPoints.Count < 2)
        {
            StatusText = "手绘路径至少需要两个点";
            return;
        }

        var sampledPointCount = DraftPathPoints.Count;
        var optimizedPoints = RoutePolylineSimplifier.Simplify(
            DraftPathPoints,
            Math.Max(0, DrawSimplificationTolerance));
        var nodes = new List<RouteNavigationNode>();
        foreach (var point in optimizedPoints)
        {
            var node = ResolveOrCreateDrawNode(point);
            if (nodes.Count == 0 || !Same(nodes[^1].NodeId, node.NodeId))
            {
                nodes.Add(node);
            }
        }

        var edgeCount = 0;
        for (var index = 1; index < nodes.Count; index++)
        {
            if (AddManualEdge(nodes[index - 1], nodes[index]))
            {
                edgeCount++;
            }
            if (AddBidirectionalEdge)
            {
                if (AddManualEdge(nodes[index], nodes[index - 1]))
                {
                    edgeCount++;
                }
            }
        }

        IsPathDrawing = false;
        DraftPathPoints = [];
        _draftCreatedNodeIds.Clear();
        OnPropertyChanged(nameof(DraftPathPointCount));
        ApplyFilters();
        StatusText = edgeCount > 0
            ? $"手绘完成：RDP 将 {sampledPointCount} 个采样点优化为 {nodes.Count} 个路径节点 / {edgeCount} 条连接，点击“保存补丁并重新加载”持久化"
            : "手绘点全部吸附到同一节点，没有生成连接";
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
                Id = $"graph-fix-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}",
                BaseGraphId = _snapshot.Graph.GraphId,
                Author = PatchAuthor?.Trim() ?? string.Empty,
                Reason = PatchReason?.Trim() ?? string.Empty,
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

    private bool AddManualEdge(RouteNavigationNode from, RouteNavigationNode to)
    {
        if (!Same(from.MapName, to.MapName))
        {
            StatusText = $"不能连接不同地图的节点：{from.MapName} → {to.MapName}";
            return false;
        }

        var moveMode = string.IsNullOrWhiteSpace(EdgeMoveMode) ? MoveModeEnum.Walk.Code : EdgeMoveMode;
        if (_snapshot.Graph.Edges.Any(edge =>
                Same(edge.FromNodeId, from.NodeId) &&
                Same(edge.ToNodeId, to.NodeId) &&
                Same(edge.MoveMode, moveMode) &&
                edge.ReviewStatus is not (GraphReviewStatus.Disabled or GraphReviewStatus.Rejected)))
        {
            return false;
        }

        var edge = new RouteNavigationEdge
        {
            EdgeId = "manual_edge_" + Guid.NewGuid().ToString("N")[..16],
            SegmentId = "manual",
            FromNodeId = from.NodeId,
            ToNodeId = to.NodeId,
            MapName = from.MapName,
            MoveMode = moveMode,
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
        return true;
    }

    private RouteNavigationNode ResolveOrCreateDrawNode(RouteGraphPoint point)
    {
        var layer = string.IsNullOrWhiteSpace(NodeLayer) || NodeLayer == All ? "surface" : NodeLayer;
        var snapDistance = Math.Max(0, DrawSnapDistance);
        var indexedNodes = _snapshot.FindNearestNodes(FilterMap, point, 16, snapDistance)
            .Select(candidate => candidate.Node);
        var pendingManualNodes = _snapshot.Graph.Nodes.Where(node =>
            node.NodeId.StartsWith("manual_", StringComparison.OrdinalIgnoreCase) &&
            !_draftCreatedNodeIds.Contains(node.NodeId));
        var existing = indexedNodes
            .Concat(pendingManualNodes)
            .DistinctBy(node => node.NodeId, StringComparer.OrdinalIgnoreCase)
            .Where(node => Same(node.MapName, FilterMap) && Same(node.LayerId, layer))
            .Select(node => new
            {
                Node = node,
                Distance = RouteGraphGeometry.Distance(point, new RouteGraphPoint(node.X, node.Y))
            })
            .Where(item => item.Distance <= snapDistance)
            .OrderBy(item => item.Distance)
            .FirstOrDefault()?.Node;
        if (existing != null)
        {
            return existing;
        }

        var node = new RouteNavigationNode
        {
            NodeId = "manual_" + Guid.NewGuid().ToString("N")[..16],
            MapName = FilterMap,
            X = point.X,
            Y = point.Y,
            LayerId = layer,
            NodeType = "path",
            AreaTag = NodeAreaTag,
            Floor = NodeFloor,
            Underground = NodeUnderground
        };
        Stage(new RouteGraphOverrideOperation { Type = RouteGraphOverrideOperationType.AddNode, Node = node });
        _snapshot.Graph.Nodes.Add(node);
        _draftCreatedNodeIds.Add(node.NodeId);
        return node;
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
        ReplaceMapOptions(MapOptions, _snapshot.Nodes.Select(node => RouteGraphGeometry.NormalizeMapName(node.MapName)));
        ReplaceOptions(LayerOptions, _snapshot.Nodes.Select(node => string.IsNullOrWhiteSpace(node.LayerId) ? "surface" : node.LayerId));
        if (!MapOptions.Contains(FilterMap)) FilterMap = MapOptions.FirstOrDefault() ?? "Teyvat";
        if (!LayerOptions.Contains(FilterLayer)) FilterLayer = All;
    }

    private static void ReplaceMapOptions(ObservableCollection<string> target, IEnumerable<string> values)
    {
        target.Clear();
        foreach (var value in values
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Order())
        {
            target.Add(value);
        }
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
