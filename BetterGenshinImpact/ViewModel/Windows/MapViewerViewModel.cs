using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.Common.Map;
using BetterGenshinImpact.Helpers;
using System.Linq;
using System.Text.Json;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.GameTask.AutoPathing.Telemetry;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Helpers.Extensions;
using BetterGenshinImpact.View.Controls;
using BetterGenshinImpact.ViewModel.Pages;
using BetterGenshinImpact.View.Windows;
using Microsoft.Win32;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;
using WpfPoint = System.Windows.Point;
using WpfWindow = System.Windows.Window;

namespace BetterGenshinImpact.ViewModel.Windows;

/// <summary>
/// TODO 需要支持更多地图
/// </summary>
public partial class MapViewerViewModel : ObservableObject
{
    private const int CoordinateStorageDecimals = 4;
    private const int CoordinateDisplayDecimals = 2;
    private readonly string _routeSaveDir = Global.Absolute(Path.Combine("User", "AutoPathing", "Routes"));
    private readonly RouteNavigationGraphProvider _graphProvider;
    private readonly RouteNavigationPlanner _routeNavigationPlanner;

    [ObservableProperty]
    private WriteableBitmap _mapBitmap;

    [ObservableProperty]
    private string _mapName = nameof(MapTypes.Teyvat);

    [ObservableProperty]
    private string _mapDisplayName = string.Empty;

    [ObservableProperty]
    private string _currentPositionText = "当前位置：等待坐标";

    [ObservableProperty]
    private string _selectedTargetText = "目标点：点击地图选择";

    [ObservableProperty]
    private string _clipRectText = "视野：-";

    [ObservableProperty]
    private string _lastRefreshText = "刷新：-";

    [ObservableProperty]
    private string _taskName = "未加载路径";

    [ObservableProperty]
    private string _taskMetaText = "等待规划结果或当前追踪任务";

    [ObservableProperty]
    private string _routeDistanceText = "距离：-";

    [ObservableProperty]
    private double _routeProgressValue;

    [ObservableProperty]
    private string _routeProgressText = "0%";

    [ObservableProperty]
    private string _routeProgressPointText = "0 / 0 个点";

    [ObservableProperty]
    private string _nextWaypointText = "-";

    [ObservableProperty]
    private string _nextWaypointActionText = "-";

    [ObservableProperty]
    private string _nextWaypointActionParamsText = "-";

    [ObservableProperty]
    private string _moveModeSummary = "-";

    [ObservableProperty]
    private bool _hasCurrentPathing;

    [ObservableProperty]
    private bool _isTopmost = TaskContext.Instance().Config.DevConfig.MapViewerTopmost;

    [ObservableProperty]
    private bool _isFollowingCurrent = true;

    [ObservableProperty]
    private bool _showTeleportPoints = true;

    [ObservableProperty]
    private double _followZoom = 5.0;

    [ObservableProperty]
    private string _mapZoomText = "0.35x";

    [ObservableProperty]
    private bool _isRecorderMode;

    [ObservableProperty]
    private bool _isDebugMode = true;

    [ObservableProperty]
    private bool _isViewSettingsOpen;

    [ObservableProperty]
    private bool _isRecordAdvancedSettingsOpen;

    [ObservableProperty]
    private bool _isSidePanelVisible = true;

    [ObservableProperty]
    private string _debugJsonText = "{}";

    [ObservableProperty]
    private bool _hasJsonEdits;

    [ObservableProperty]
    private bool _autoSaveJsonEdits;

    [ObservableProperty]
    private bool _isJsonEditorMode;

    [ObservableProperty]
    private string _recordStatusText = "录制器：未开始";

    [ObservableProperty]
    private bool _isPathRecorderRecording;

    [ObservableProperty]
    private string _recordFileName = "未命名路线";

    [ObservableProperty]
    private string _recordFilePathText = "文件：未保存";

    [ObservableProperty]
    private string _recordDescription = string.Empty;

    [ObservableProperty]
    private string _recordAuthorName = string.Empty;

    [ObservableProperty]
    private string _recordAuthorLinks = string.Empty;

    [ObservableProperty]
    private string _defaultRecordAuthorName = string.Empty;

    [ObservableProperty]
    private string _defaultRecordAuthorLinks = string.Empty;

    [ObservableProperty]
    private string _recordVersion = "1.0";

    [ObservableProperty]
    private string _recordTagsText = string.Empty;

    [ObservableProperty]
    private bool _recordEnableMonsterLootSplit;

    [ObservableProperty]
    private string _recordMapMatchMethod = TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod;

    [ObservableProperty]
    private bool _updateSelectedPointOnMapClick;

    [ObservableProperty]
    private string _mapClickEditModeText = "地图点击：追加点";

    [ObservableProperty]
    private RecordedWaypointViewModel? _selectedRecordedWaypoint;

    [ObservableProperty]
    private RecordedRouteViewModel? _selectedRecordedRoute;

    [ObservableProperty]
    private bool _isRouteFileBrowserOpen;

    [ObservableProperty]
    private string _routeBrowserRelativePath = string.Empty;

    [ObservableProperty]
    private string _routeBrowserStatusText = "User\\AutoPathing";

    [ObservableProperty]
    private string _routeBrowserImportStatusText = "选择 JSON 文件后导入";

    [ObservableProperty]
    private bool _isCombatScriptManagerOpen;

    [ObservableProperty]
    private string _newCombatScriptValue = string.Empty;

    [ObservableProperty]
    private bool _newCombatScriptIsDefault;

    [ObservableProperty]
    private double _currentImageX = 1024;

    [ObservableProperty]
    private double _currentImageY = 1024;

    [ObservableProperty]
    private bool _followRoutePlanningCurrentPosition = true;

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
    private string _targetPickSummary = "点击地图可选择目标点";

    [ObservableProperty]
    private bool _hasPlan;

    [ObservableProperty]
    private bool _isPlanning;

    [ObservableProperty]
    private bool _isRefreshingRouteDiagnostics;

    private List<Waypoint>? _recordedWaypointClipboard;
    private string? _recordedWaypointClipboardText;

    public ObservableCollection<RecordedWaypointViewModel> RecordedWaypoints { get; } = [];

    public ObservableCollection<RecordedRouteViewModel> RecordedRoutes { get; } = [];

    public ObservableCollection<RouteFileBrowserItemViewModel> RouteBrowserItems { get; } = [];

    public ObservableCollection<CombatScriptOptionViewModel> CombatScripts { get; } = [];

    public ObservableCollection<RoutePlanEdgeRow> PlannedEdges { get; } = [];

    public ObservableCollection<RouteNearbyNodeRow> NearbyNodes { get; } = [];

    public ObservableCollection<RouteNearbyEdgeRow> NearbyEdges { get; } = [];

    public ObservableCollection<RouteHealthRow> HealthRows { get; } = [];

    public ICollectionView RecordedWaypointView { get; }

    public ObservableCollection<MapEditorOption> WaypointTypeOptions { get; } = new(
        WaypointType.Values.Select(i => new MapEditorOption(i.Code, i.Msg)));

    public ObservableCollection<MapEditorOption> MoveModeOptions { get; } = new(
        MoveModeEnum.Values.Select(i => new MapEditorOption(i.Code, i.Msg)));

    public ObservableCollection<MapEditorOption> TargetMoveModeOptions { get; } = new(
        new[] { new MapEditorOption(string.Empty, "沿用最后一段") }.Concat(
            MoveModeEnum.Values.Select(i => new MapEditorOption(i.Code, i.Msg))));

    public ObservableCollection<MapEditorOption> ActionOptions { get; } = new(
        new[] { new MapEditorOption(string.Empty, "无") }.Concat(
            ActionEnum.Values.Select(i => new MapEditorOption(i.Code, i.Msg))));

    public ObservableCollection<MapEditorOption> WaypointFilterDimensions { get; } =
    [
        new("type", "点位类型"),
        new("moveMode", "移动方式"),
        new("action", "动作")
    ];

    public ObservableCollection<WaypointFilterOption> ActiveWaypointFilterOptions { get; } = [];

    [ObservableProperty]
    private MapEditorOption? _selectedWaypointFilterDimension;

    [ObservableProperty]
    private bool _isWaypointFilterPopupOpen;

    [ObservableProperty]
    private string _waypointFilterSummary = "全部";

    public ObservableCollection<MapEditorOption> MapLayerOptions { get; } = new(
        Enum.GetValues<MapTypes>().Select(i => new MapEditorOption(i.ToString(), i.GetDescription())));

    public ObservableCollection<MapEditorOption> MapMatchMethodOptions { get; } = new(
    [
        new MapEditorOption("TemplateMatch", "模板匹配"),
        new MapEditorOption("SIFT", "SIFT")
    ]);

    public System.Windows.GridLength MapColumnWidth => new(1, System.Windows.GridUnitType.Star);

    public System.Windows.GridLength SplitterColumnWidth => IsSidePanelVisible ? new System.Windows.GridLength(6) : new System.Windows.GridLength(0);

    public System.Windows.GridLength SideColumnWidth => IsSidePanelVisible
        ? new System.Windows.GridLength(580)
        : new System.Windows.GridLength(0);

    public double SideColumnMinWidth => IsSidePanelVisible ? 520 : 0;

    public System.Windows.Visibility SidePanelVisibility => IsSidePanelVisible
        ? System.Windows.Visibility.Visible
        : System.Windows.Visibility.Collapsed;

    public System.Windows.Visibility SidePanelHiddenVisibility => IsSidePanelVisible
        ? System.Windows.Visibility.Collapsed
        : System.Windows.Visibility.Visible;

    public System.Windows.Visibility RecorderUiEditorVisibility => IsRecorderMode && !IsJsonEditorMode
        ? System.Windows.Visibility.Visible
        : System.Windows.Visibility.Collapsed;

    public System.Windows.Visibility RecorderJsonEditorVisibility => IsRecorderMode && IsJsonEditorMode
        ? System.Windows.Visibility.Visible
        : System.Windows.Visibility.Collapsed;

    public System.Windows.Visibility RecorderMainVisibility => IsRecorderMode
        ? System.Windows.Visibility.Visible
        : System.Windows.Visibility.Collapsed;

    public System.Windows.Visibility DebugMainVisibility => IsRecorderMode
        ? System.Windows.Visibility.Collapsed
        : System.Windows.Visibility.Visible;

    public System.Windows.Visibility RecordedWaypointListVisibility => RecordedWaypoints.Count > 0
        ? System.Windows.Visibility.Visible
        : System.Windows.Visibility.Collapsed;

    public System.Windows.Visibility RecordedWaypointEmptyVisibility => RecordedWaypoints.Count == 0
        ? System.Windows.Visibility.Visible
        : System.Windows.Visibility.Collapsed;

    public System.Windows.Visibility SelectedWaypointEditorVisibility => SelectedRecordedWaypoint == null
        ? System.Windows.Visibility.Collapsed
        : System.Windows.Visibility.Visible;

    public System.Windows.Visibility SelectedWaypointEmptyVisibility => SelectedRecordedWaypoint == null
        ? System.Windows.Visibility.Visible
        : System.Windows.Visibility.Collapsed;

    public string SidePanelToggleText => IsSidePanelVisible ? "隐藏信息" : "显示信息";

    public bool HasRecordedWaypoints => RecordedWaypoints.Count > 0;

    public bool CanRunTracking => !IsRecorderMode || HasRecordedWaypoints;

    public string RecordedWaypointCountText => $"{RecordedWaypoints.Count} 个点";

    public string RecordFileStateText => string.IsNullOrWhiteSpace(_recordFilePath)
        ? "未保存"
        : Path.GetFileName(_recordFilePath);

    public string RecordSummaryText => $"{(IsRecorderMode ? "录制模式" : "调试模式")} · {RecordedWaypoints.Count} 个点 · {RecordFileStateText}";

    public string RouteEmptyStatusText => RecordedWaypoints.Count == 0
        ? "暂无路线，请先录制或加载路线"
        : $"路线：{RecordFileName} · {RecordedWaypoints.Count} 个点";

    public bool HasRecordedRoutes => RecordedRoutes.Count > 0;

    public string RecordedRouteCountText => $"{RecordedRoutes.Count} 条路线";

    public string RouteListEmptyText => RecordedRoutes.Count == 0
        ? "暂无路线"
        : string.Empty;

    public string RouteBrowserSelectionText
    {
        get
        {
            var selectedCount = RouteBrowserItems.Count(i => i.IsSelected && i.IsJsonFile);
            var jsonCount = RouteBrowserItems.Count(i => i.IsJsonFile);
            return selectedCount == 0
                ? $"{jsonCount} 个 JSON"
                : $"已选 {selectedCount} / {jsonCount}";
        }
    }

    public string SelectedWaypointEditorTitle => SelectedRecordedWaypoint == null
        ? "当前编辑点位"
        : $"当前编辑点位：#{SelectedRecordedWaypoint.Index} {SelectedRecordedWaypoint.TypeDisplayText}";

    public string MapStatusText => $"模式：{(IsRecorderMode ? "录制" : "调试")} / {(IsFollowingCurrent ? "跟随" : "固定")}    缩放：{MapZoomText}";

    public string FollowZoomText => $"{FollowZoom:F1}x";

    public string CurrentPositionDetailText => TrimDebugValue(CurrentPositionText, "当前位置：");

    public string SelectedTargetDetailText => TrimDebugValue(SelectedTargetText, "目标点：");

    public string ClipRectDetailText => TrimDebugValue(ClipRectText, "视野：");

    public string LastRefreshDetailText => TrimDebugValue(LastRefreshText, "刷新：");

    public string RouteDistanceDetailText => TrimDebugValue(RouteDistanceText, "距离：");

    public string FollowHotkeyText => string.IsNullOrWhiteSpace(TaskContext.Instance().Config.HotKeyConfig.MapViewerFollowHotkey)
        ? "未绑定"
        : TaskContext.Instance().Config.HotKeyConfig.MapViewerFollowHotkey;

    public string PathRecorderHotkeyText => FormatHotkeyText(TaskContext.Instance().Config.HotKeyConfig.PathRecorderHotkey);

    public string AddWaypointHotkeyText => FormatHotkeyText(TaskContext.Instance().Config.HotKeyConfig.AddWaypointHotkey);

    public string RecordingToggleText => IsPathRecorderRecording ? "停止录制" : "开始录制";

    public string RecordingToggleToolTip =>
        $"启动/停止录制：{PathRecorderHotkeyText}\n添加当前位置：{AddWaypointHotkeyText}";

    public string RouteGraphFilePath => Path.Combine(_routeSaveDir, RouteNavigationGraphBuilder.GraphFileName);

    public string RouteHealthFilePath => Path.Combine(_routeSaveDir, "route_health.json");

    // private readonly Mat _all256Map = new(Global.Absolute(@"Assets/Map/mainMap256Block.png"));

    private Mat _currentPathingMap = new(); // 2048级别

    private Rect _currentPathingRect = new(); // 2048级别

    private readonly object _pathingMapLock = new();

    private Rect _lastClipGlobalRect = new(0, 0, 512, 512);

    private Size _lastClipPixelSize = new(512, 512);

    private Mat _mapImage = new();

    private int _scale = 1;

    private DateTime _lastMapBitmapRefreshUtc = DateTime.MinValue;

    private readonly TimeSpan _mapBitmapRefreshInterval = TimeSpan.FromMilliseconds(120);

    private readonly object _mapBitmapRefreshLock = new();

    private Point2f? _lastPosition;

    private Point2f? _selectedTargetPoint;

    private string? _recordFilePath;

    private PathingTask? _recordTaskTemplate;

    private bool _isRefreshingJson;

    private const int MaxRecorderHistory = 80;

    private readonly List<string> _recorderHistory = [];

    private int _recorderHistoryIndex = -1;

    private bool _isRestoringRecorderHistory;

    private bool _isSwitchingRecordedRoute;

    private bool _isLoadingRouteBrowser;

    private bool _isLoadingCombatScripts;

    private bool _isUpdatingWaypointFromMap;

    private bool _isRefreshingRouteDiagnosticsLite;

    private List<Waypoint> _currentRoutePoints = [];

    private double _routeTotalDistance;

    private double _routeCompletedDistance;

    private bool _showRouteProgressAsPercent = true;

    public WpfWindow? DialogOwner { get; set; }

    public MapViewerViewModel(string mapName)
    {
        _graphProvider = new RouteNavigationGraphProvider(_routeSaveDir);
        _routeNavigationPlanner = new RouteNavigationPlanner(_graphProvider);

        if (string.IsNullOrEmpty(mapName))
        {
            mapName = nameof(MapTypes.Teyvat);
        }

        MapName = mapName;
        MapDisplayName = GetMapDisplayName(mapName);
        DefaultRecordAuthorName = TaskContext.Instance().Config.DevConfig.RecordDefaultAuthorName;
        DefaultRecordAuthorLinks = TaskContext.Instance().Config.DevConfig.RecordDefaultAuthorLinks;
        RecordAuthorName = DefaultRecordAuthorName;
        RecordAuthorLinks = DefaultRecordAuthorLinks;
        _mapBitmap = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null);
        RecordedWaypointView = CollectionViewSource.GetDefaultView(RecordedWaypoints);
        RecordedWaypointView.Filter = FilterRecordedWaypoint;
        RecordedWaypoints.CollectionChanged += OnRecordedWaypointsCollectionChanged;
        RecordedRoutes.CollectionChanged += OnRecordedRoutesCollectionChanged;
        RouteBrowserItems.CollectionChanged += OnRouteBrowserItemsCollectionChanged;
        CombatScripts.CollectionChanged += OnCombatScriptsCollectionChanged;
        SelectedWaypointFilterDimension = WaypointFilterDimensions.FirstOrDefault();
        LoadCombatScripts();
        IsPathRecorderRecording = PathRecorder.Instance.IsRecording;
        WeakReferenceMessenger.Default.Register<PropertyChangedMessage<object>>(this, (_, msg) =>
        {
            if (msg.PropertyName == "SendCurrentPosition")
            {
                if (msg.NewValue is not Point2f point)
                {
                    return;
                }

                if (point.X == 0 && point.Y == 0)
                {
                    return;
                }

                if (!ShouldRefreshMapBitmap())
                {
                    if (FollowRoutePlanningCurrentPosition)
                    {
                        UIDispatcherHelper.BeginInvoke(() => UpdateRoutePlanningCurrentPosition(point));
                    }

                    return;
                }

                UIDispatcherHelper.BeginInvoke(() =>
                {
                    _lastPosition = point;
                    CurrentPositionText = FormatCurrentPosition(point);
                    LastRefreshText = $"刷新：{DateTime.Now:HH:mm:ss}";
                    UpdateRoutePlanningCurrentPosition(point);
                });
            }
            else if (msg.PropertyName == "UpdateCurrentPathing")
            {
                Debug.WriteLine("更新当前追踪的路径图像");
                var pathingTask = (PathingTask)msg.NewValue;
                UpdateTaskSummary(pathingTask);
            }
            else if (msg.PropertyName == "UpdateCurrentWaypoint" && msg.NewValue is WaypointForTrack waypoint)
            {
                UIDispatcherHelper.BeginInvoke(() => UpdateNextWaypoint(waypoint));
            }
            else if (msg.PropertyName == "MapFollowCurrentChanged" && msg.NewValue is bool followCurrent)
            {
                UIDispatcherHelper.BeginInvoke(() => IsFollowingCurrent = followCurrent);
            }
            else if (msg.PropertyName == "UpdateMapZoom" && msg.NewValue is double zoom)
            {
                UIDispatcherHelper.BeginInvoke(() => MapZoomText = $"{zoom:F2}x");
            }
            else if (msg.PropertyName == "ToggleMapFollowCurrent")
            {
                UIDispatcherHelper.BeginInvoke(() =>
                {
                    IsFollowingCurrent = !IsFollowingCurrent;
                    if (IsFollowingCurrent)
                    {
                        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "ResetMapView", new object(), new object()));
                    }
                });
            }
            else if (msg.PropertyName == "PreparePathRecorderStart")
            {
                UIDispatcherHelper.BeginInvoke(PrepareForRecordingStart);
            }
            else if (msg.PropertyName == "SelectPathingTargetPosition" && msg.NewValue is Point2f targetPoint)
            {
                UIDispatcherHelper.BeginInvoke(() => HandleMapPointSelected(targetPoint));
            }
            else if (msg.PropertyName == "UpdateRecorderPathing" && msg.Sender is not MapViewerViewModel && msg.NewValue is PathingTask recorderTask)
            {
                UIDispatcherHelper.BeginInvoke(() =>
                {
                    IsPathRecorderRecording = PathRecorder.Instance.IsRecording;
                    IsRecorderMode = true;
                    IsDebugMode = false;
                    LoadRecorderTask(recorderTask);
                });
            }
            else if (msg.PropertyName == "SelectRecorderWaypointIndex" && msg.NewValue is int waypointIndex)
            {
                UIDispatcherHelper.BeginInvoke(() => SelectRecordedWaypointByIndex(waypointIndex));
            }
            else if (msg.PropertyName == "MoveRecorderWaypointPosition" && msg.NewValue is RecorderWaypointMapUpdate update)
            {
                UIDispatcherHelper.BeginInvoke(() => MoveRecordedWaypointFromMap(update.Index, update.Point));
            }
        });

        _ = RefreshRouteDiagnosticsLiteAsync();
    }

    private void OnRecordedWaypointsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (RecordedWaypointViewModel waypoint in e.OldItems)
            {
                waypoint.PropertyChanged -= OnRecordedWaypointPropertyChanged;
            }
        }

        if (e.NewItems != null)
        {
            foreach (RecordedWaypointViewModel waypoint in e.NewItems)
            {
                waypoint.PropertyChanged += OnRecordedWaypointPropertyChanged;
            }
        }

        RefreshWaypointFilters();
        RefreshRecorderSummaryProperties();
    }

    private void OnRecordedRoutesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (RecordedRouteViewModel route in e.OldItems)
            {
                route.PropertyChanged -= OnRecordedRoutePropertyChanged;
            }
        }

        if (e.NewItems != null)
        {
            foreach (RecordedRouteViewModel route in e.NewItems)
            {
                route.PropertyChanged += OnRecordedRoutePropertyChanged;
            }
        }

        RefreshRouteListProperties();
        PublishRecordedRouteList();
    }

    private void OnRecordedRoutePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RecordedRouteViewModel.Name)
            or nameof(RecordedRouteViewModel.PointCount)
            or nameof(RecordedRouteViewModel.MapDisplayText)
            or nameof(RecordedRouteViewModel.FileDisplayText))
        {
            RefreshRouteListProperties();
            PublishRecordedRouteList();
        }
    }

    private void OnRouteBrowserItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (RouteFileBrowserItemViewModel item in e.OldItems)
            {
                item.PropertyChanged -= OnRouteBrowserItemPropertyChanged;
            }
        }

        if (e.NewItems != null)
        {
            foreach (RouteFileBrowserItemViewModel item in e.NewItems)
            {
                item.PropertyChanged += OnRouteBrowserItemPropertyChanged;
            }
        }

        OnPropertyChanged(nameof(RouteBrowserSelectionText));
    }

    private void OnRouteBrowserItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RouteFileBrowserItemViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(RouteBrowserSelectionText));
        }
    }

    private void OnCombatScriptsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (CombatScriptOptionViewModel item in e.OldItems)
            {
                item.PropertyChanged -= OnCombatScriptPropertyChanged;
            }
        }

        if (e.NewItems != null)
        {
            foreach (CombatScriptOptionViewModel item in e.NewItems)
            {
                item.PropertyChanged += OnCombatScriptPropertyChanged;
            }
        }

        if (_isLoadingCombatScripts)
        {
            return;
        }

        SaveCombatScripts();
    }

    private void OnCombatScriptPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isLoadingCombatScripts)
        {
            return;
        }

        if (e.PropertyName == nameof(CombatScriptOptionViewModel.IsDefault)
            && sender is CombatScriptOptionViewModel { IsDefault: true } selected)
        {
            foreach (var item in CombatScripts.Where(i => !ReferenceEquals(i, selected)))
            {
                item.IsDefault = false;
            }
        }

        SaveCombatScripts();
    }

    private void OnRecordedWaypointPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RecordedWaypointViewModel.Type)
            or nameof(RecordedWaypointViewModel.MoveMode)
            or nameof(RecordedWaypointViewModel.Action)
            or nameof(RecordedWaypointViewModel.TypeDisplayText)
            or nameof(RecordedWaypointViewModel.MoveModeDisplayText)
            or nameof(RecordedWaypointViewModel.ActionDisplayText))
        {
            RefreshWaypointFilters();
        }

        if (e.PropertyName is nameof(RecordedWaypointViewModel.X)
            or nameof(RecordedWaypointViewModel.Y)
            or nameof(RecordedWaypointViewModel.Type)
            or nameof(RecordedWaypointViewModel.MoveMode)
            or nameof(RecordedWaypointViewModel.Action)
            or nameof(RecordedWaypointViewModel.ActionParams)
            or nameof(RecordedWaypointViewModel.Description)
            or nameof(RecordedWaypointViewModel.MonsterTag)
            or nameof(RecordedWaypointViewModel.EnableMonsterLootSplit)
            or nameof(RecordedWaypointViewModel.MisidentificationTypesText)
            or nameof(RecordedWaypointViewModel.MisidentificationHandlingMode)
            or nameof(RecordedWaypointViewModel.MisidentificationArrivalTime))
        {
            if (_isUpdatingWaypointFromMap)
            {
                return;
            }

            if (e.PropertyName == nameof(RecordedWaypointViewModel.Action)
                && sender is RecordedWaypointViewModel waypoint
                && string.Equals(waypoint.Action, ActionEnum.CombatScript.Code, StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(waypoint.ActionParams))
            {
                waypoint.ActionParams = GetDefaultCombatScriptValue();
            }

            PublishRecorderPath();
            if (sender is RecordedWaypointViewModel selectedWaypoint && selectedWaypoint.IsSelected)
            {
                PublishSelectedRecorderWaypoints();
            }
        }
    }

    partial void OnSelectedWaypointFilterDimensionChanged(MapEditorOption? value)
    {
        RefreshWaypointFilters([]);
    }

    [RelayCommand]
    private void ToggleWaypointFilterPopup()
    {
        IsWaypointFilterPopupOpen = !IsWaypointFilterPopupOpen;
    }

    [RelayCommand]
    private void ClearWaypointFilter()
    {
        foreach (var option in ActiveWaypointFilterOptions)
        {
            option.IsSelected = false;
        }

        RefreshWaypointFilterView();
    }

    private bool FilterRecordedWaypoint(object item)
    {
        if (item is not RecordedWaypointViewModel waypoint)
        {
            return false;
        }

        var selectedCodes = ActiveWaypointFilterOptions
            .Where(i => i.IsSelected)
            .Select(i => i.Code)
            .ToHashSet(StringComparer.Ordinal);

        if (selectedCodes.Count == 0)
        {
            return true;
        }

        return selectedCodes.Contains(GetWaypointFilterCode(waypoint));
    }

    private void RefreshWaypointFilters(IEnumerable<string>? selectedCodes = null)
    {
        var retainedCodes = (selectedCodes ?? ActiveWaypointFilterOptions.Where(i => i.IsSelected).Select(i => i.Code))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var option in ActiveWaypointFilterOptions)
        {
            option.PropertyChanged -= OnWaypointFilterOptionPropertyChanged;
        }

        ActiveWaypointFilterOptions.Clear();
        var options = BuildWaypointFilterOptions()
            .OrderBy(i => i.DisplayName, StringComparer.CurrentCulture)
            .ToList();

        foreach (var option in options)
        {
            option.IsSelected = retainedCodes.Contains(option.Code);
            option.PropertyChanged += OnWaypointFilterOptionPropertyChanged;
            ActiveWaypointFilterOptions.Add(option);
        }

        RefreshWaypointFilterView();
    }

    private IEnumerable<WaypointFilterOption> BuildWaypointFilterOptions()
    {
        return (SelectedWaypointFilterDimension?.Code switch
            {
                "moveMode" => MoveModeOptions,
                "action" => ActionOptions,
                _ => WaypointTypeOptions
            })
            .Select(i => new WaypointFilterOption(i.Code, i.DisplayName));
    }

    private void OnWaypointFilterOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WaypointFilterOption.IsSelected))
        {
            RefreshWaypointFilterView();
        }
    }

    private void RefreshWaypointFilterView()
    {
        RecordedWaypointView.Refresh();
        var selected = ActiveWaypointFilterOptions.Where(i => i.IsSelected).Select(i => i.DisplayName).ToList();
        WaypointFilterSummary = selected.Count == 0
            ? "全部"
            : selected.Count <= 2
                ? string.Join("、", selected)
                : $"{selected[0]}、{selected[1]} +{selected.Count - 2}";
    }

    private string GetWaypointFilterCode(RecordedWaypointViewModel waypoint)
    {
        return SelectedWaypointFilterDimension?.Code switch
        {
            "moveMode" => string.IsNullOrWhiteSpace(waypoint.MoveMode) ? MoveModeEnum.Walk.Code : waypoint.MoveMode,
            "action" => string.IsNullOrWhiteSpace(waypoint.Action) ? string.Empty : waypoint.Action,
            _ => string.IsNullOrWhiteSpace(waypoint.Type) ? WaypointType.Path.Code : waypoint.Type
        };
    }

    private void RefreshRecorderSummaryProperties()
    {
        OnPropertyChanged(nameof(RecordedWaypointListVisibility));
        OnPropertyChanged(nameof(RecordedWaypointEmptyVisibility));
        OnPropertyChanged(nameof(HasRecordedWaypoints));
        OnPropertyChanged(nameof(CanRunTracking));
        OnPropertyChanged(nameof(RecordedWaypointCountText));
        OnPropertyChanged(nameof(RecordFileStateText));
        OnPropertyChanged(nameof(RecordSummaryText));
        OnPropertyChanged(nameof(RouteEmptyStatusText));
    }

    private void RefreshRouteListProperties()
    {
        OnPropertyChanged(nameof(HasRecordedRoutes));
        OnPropertyChanged(nameof(RecordedRouteCountText));
        OnPropertyChanged(nameof(RouteListEmptyText));
    }

    partial void OnIsFollowingCurrentChanged(bool value)
    {
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "SetMapFollowCurrent", new object(), value));
        OnPropertyChanged(nameof(MapStatusText));
    }

    partial void OnCurrentPositionTextChanged(string value)
    {
        OnPropertyChanged(nameof(CurrentPositionDetailText));
    }

    partial void OnSelectedTargetTextChanged(string value)
    {
        OnPropertyChanged(nameof(SelectedTargetDetailText));
    }

    partial void OnClipRectTextChanged(string value)
    {
        OnPropertyChanged(nameof(ClipRectDetailText));
    }

    partial void OnLastRefreshTextChanged(string value)
    {
        OnPropertyChanged(nameof(LastRefreshDetailText));
    }

    partial void OnRouteDistanceTextChanged(string value)
    {
        OnPropertyChanged(nameof(RouteDistanceDetailText));
    }

    partial void OnIsRecorderModeChanged(bool value)
    {
        IsDebugMode = !value;
        if (!value)
        {
            IsJsonEditorMode = false;
        }

        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "SetMapViewerRecorderMode", new object(), value));
        OnPropertyChanged(nameof(RecorderMainVisibility));
        OnPropertyChanged(nameof(DebugMainVisibility));
        RefreshLayoutProperties();
        RefreshRecorderSummaryProperties();
        OnPropertyChanged(nameof(MapStatusText));
        if (value && !HasJsonEdits)
        {
            PublishRecorderPath();
        }
    }

    partial void OnIsDebugModeChanged(bool value)
    {
        if (value != !IsRecorderMode)
        {
            IsRecorderMode = !value;
        }

        OnPropertyChanged(nameof(RecorderMainVisibility));
        OnPropertyChanged(nameof(DebugMainVisibility));
        RefreshLayoutProperties();
        RefreshRecorderSummaryProperties();
        OnPropertyChanged(nameof(MapStatusText));
    }

    partial void OnIsJsonEditorModeChanged(bool value)
    {
        OnPropertyChanged(nameof(RecorderUiEditorVisibility));
        OnPropertyChanged(nameof(RecorderJsonEditorVisibility));
    }

    partial void OnIsSidePanelVisibleChanged(bool value)
    {
        RefreshLayoutProperties();
    }

    partial void OnUpdateSelectedPointOnMapClickChanged(bool value)
    {
        MapClickEditModeText = value ? "地图点击：更新选中点" : "地图点击：追加点";
    }

    partial void OnSelectedRecordedWaypointChanged(RecordedWaypointViewModel? value)
    {
        OnPropertyChanged(nameof(SelectedWaypointEditorVisibility));
        OnPropertyChanged(nameof(SelectedWaypointEmptyVisibility));
        OnPropertyChanged(nameof(SelectedWaypointEditorTitle));

        if (!RecordedWaypoints.Any(i => i.IsSelected))
        {
            if (value != null && RecordedWaypoints.Contains(value))
            {
                value.IsSelected = true;
            }

            PublishSelectedRecorderWaypoints();
        }
    }

    partial void OnSelectedRecordedRouteChanged(RecordedRouteViewModel? value)
    {
        if (_isSwitchingRecordedRoute)
        {
            return;
        }

        LoadRecordedRoute(value);
    }

    partial void OnRouteBrowserRelativePathChanged(string value)
    {
        RouteBrowserStatusText = string.IsNullOrWhiteSpace(value)
            ? "User\\AutoPathing"
            : value;
    }

    partial void OnIsRouteFileBrowserOpenChanged(bool value)
    {
        if (value && !_isLoadingRouteBrowser)
        {
            LoadRouteBrowserItems(RouteBrowserRelativePath);
        }
    }

    public void SyncRecordedWaypointSelection(IList selectedItems)
    {
        var selected = selectedItems.OfType<RecordedWaypointViewModel>()
            .Where(i => RecordedWaypoints.Contains(i))
            .OrderBy(i => RecordedWaypoints.IndexOf(i))
            .ToList();
        var selectedSet = selected.ToHashSet();

        foreach (var waypoint in RecordedWaypoints)
        {
            var isSelected = selectedSet.Contains(waypoint);
            if (waypoint.IsSelected != isSelected)
            {
                waypoint.IsSelected = isSelected;
            }
        }

        PublishSelectedRecorderWaypoints(selected);
    }

    private void PublishSelectedRecorderWaypoints(IEnumerable<RecordedWaypointViewModel>? selectedWaypoints = null)
    {
        var selected = (selectedWaypoints ?? RecordedWaypoints.Where(i => i.IsSelected))
            .Where(i => RecordedWaypoints.Contains(i))
            .OrderBy(i => RecordedWaypoints.IndexOf(i))
            .Select(i => new Point2f((float)i.X, (float)i.Y))
            .ToList();

        if (selected.Count == 0)
        {
            WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(
                this, "ClearSelectedRecorderWaypoint", new object(), new object()));
            return;
        }

        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(
            this, "SelectRecorderWaypointPositions", new object(), selected));
    }

    partial void OnDebugJsonTextChanged(string value)
    {
        if (_isRefreshingJson)
        {
            return;
        }

        HasJsonEdits = true;
        RecordStatusText = "录制器：JSON 已修改，待应用";
    }

    partial void OnRecordFileNameChanged(string value)
    {
        OnPropertyChanged(nameof(RouteEmptyStatusText));
        PublishRecorderPath();
    }

    partial void OnIsTopmostChanged(bool value)
    {
        TaskContext.Instance().Config.DevConfig.MapViewerTopmost = value;
    }

    partial void OnRecordFilePathTextChanged(string value)
    {
        RefreshRecorderSummaryProperties();
    }

    partial void OnMapZoomTextChanged(string value)
    {
        OnPropertyChanged(nameof(MapStatusText));
    }

    partial void OnFollowZoomChanged(double value)
    {
        OnPropertyChanged(nameof(FollowZoomText));
    }

    partial void OnRecordStatusTextChanged(string value)
    {
        OnPropertyChanged(nameof(RecordSummaryText));
    }

    partial void OnIsPathRecorderRecordingChanged(bool value)
    {
        OnPropertyChanged(nameof(RecordingToggleText));
    }

    partial void OnRecordDescriptionChanged(string value)
    {
        PublishRecorderPath();
    }

    partial void OnRecordAuthorNameChanged(string value)
    {
        PublishRecorderPath();
    }

    partial void OnRecordAuthorLinksChanged(string value)
    {
        PublishRecorderPath();
    }

    partial void OnDefaultRecordAuthorNameChanged(string value)
    {
        TaskContext.Instance().Config.DevConfig.RecordDefaultAuthorName = value;
    }

    partial void OnDefaultRecordAuthorLinksChanged(string value)
    {
        TaskContext.Instance().Config.DevConfig.RecordDefaultAuthorLinks = value;
    }

    partial void OnRecordVersionChanged(string value)
    {
        PublishRecorderPath();
    }

    partial void OnRecordTagsTextChanged(string value)
    {
        PublishRecorderPath();
    }

    partial void OnRecordEnableMonsterLootSplitChanged(bool value)
    {
        PublishRecorderPath();
    }

    partial void OnRecordMapMatchMethodChanged(string value)
    {
        PublishRecorderPath();
    }

    partial void OnMapNameChanged(string value)
    {
        MapDisplayName = GetMapDisplayName(value);
        if (!string.IsNullOrWhiteSpace(value))
        {
            TaskContext.Instance().Config.DevConfig.RecordMapName = value;
        }

        SelectedTargetText = "目标点：点击地图选择";
        _selectedTargetPoint = null;
        ClearPathing();
        PublishRecordedRouteList();
        _ = RefreshRouteDiagnosticsLiteAsync();
    }

    private bool ShouldRefreshMapBitmap()
    {
        lock (_mapBitmapRefreshLock)
        {
            var now = DateTime.UtcNow;
            if (now - _lastMapBitmapRefreshUtc < _mapBitmapRefreshInterval)
            {
                return false;
            }

            _lastMapBitmapRefreshUtc = now;
            return true;
        }
    }

    private void Init(string mapName)
    {
        if (mapName == MapTypes.Teyvat.ToString())
        {
            _mapImage = new Mat(Global.Absolute(@"Assets/Map/Teyvat/Teyvat_0_256.png"));
            // 最特殊，图像坐标是 2048 级别的，路径展示窗口是 256 级别的
            _scale = 2048 / 256;
        }
        else if (mapName == MapTypes.TheChasm.ToString())
        {
            _mapImage = new Mat(Global.Absolute(@"Assets/Map/TheChasm/TheChasm_0_1024.png"));
        }
        else if (mapName == MapTypes.Enkanomiya.ToString())
        {
            _mapImage = new Mat(Global.Absolute(@"Assets/Map/Enkanomiya/Enkanomiya_0_1024.png"));
        }
        else if (mapName == MapTypes.SeaOfBygoneEras.ToString())
        {
            _mapImage = new Mat(Global.Absolute(@"Assets/Map/SeaOfBygoneEras/SeaOfBygoneEras_0_1024.png"));
        }
        else if (mapName == MapTypes.AncientSacredMountain.ToString())
        {
            _mapImage = new Mat(Global.Absolute(@"Assets/Map/AncientSacredMountain/AncientSacredMountain_0_1024.png"));
        }
        else if (mapName == MapTypes.TempleOfSpace.ToString())
        {
            _mapImage = new Mat(Global.Absolute(@"Assets/Map/TempleOfSpace/TempleOfSpace_0_1024.png"));
        }
        else
        {
            throw new Exception("暂时不支持展示路径的地图类型:" + mapName);
        }
    }

    public Mat ClipMat(Point2f pos)
    {
        try
        {
            var len = 256;
            pos = new Point2f(pos.X - _currentPathingRect.X, pos.Y - _currentPathingRect.Y);
            Rect rect = new((int)pos.X - len, (int)pos.Y - len, len * 2, len * 2);
            // 处理越界
            if (rect.X < 0)
            {
                rect.X = 0;
            }
            if (rect.Y < 0)
            {
                rect.Y = 0;
            }
            if (rect.X + rect.Width > _mapImage.Width)
            {
                rect.Width = _mapImage.Width - rect.X;
            }
            if (rect.Y + rect.Height > _mapImage.Height)
            {
                rect.Height = _mapImage.Height - rect.Y;
            }

            _lastClipGlobalRect = new Rect(
                rect.X + _currentPathingRect.X,
                rect.Y + _currentPathingRect.Y,
                rect.Width,
                rect.Height);
            ClipRectText = $"视野：{_lastClipGlobalRect.X}, {_lastClipGlobalRect.Y} / {_lastClipGlobalRect.Width}x{_lastClipGlobalRect.Height}";
            lock (_pathingMapLock)
            {
                // 实现剪切 Mat 的逻辑
                if (_currentPathingMap.Empty())
                {
                    Debug.WriteLine("_currentPathingMap 未初始化");
                    var baseRect = new Rect(rect.X / _scale, rect.Y / _scale, rect.Width / _scale, rect.Height / _scale);
                    var baseMat = new Mat(_mapImage, baseRect);
                    _lastClipPixelSize = new Size(baseMat.Width, baseMat.Height);
                    return baseMat;
                }

                Mat clipMat = new(_currentPathingMap, rect);
                clipMat = clipMat.Clone();
                _lastClipPixelSize = new Size(clipMat.Width, clipMat.Height);
                // 绘制中心点
                Cv2.Circle(clipMat, new Point(len, len), 3, new Scalar(0, 255, 0), 2);
                return clipMat;
            }
        }
        catch
        {
            // 返回一张全黑的图
            var blackMat = new Mat(256, 256, MatType.CV_8UC3, new Scalar(0, 0, 0));
            return blackMat;
        }

    }

    [RelayCommand]
    private void ClearPathing()
    {
        lock (_pathingMapLock)
        {
            _currentPathingMap.Dispose();
            _currentPathingMap = new Mat();
            _currentPathingRect = new Rect();
        }

        HasCurrentPathing = false;
        TaskName = "未加载路径";
        TaskMetaText = "等待规划结果或当前追踪任务";
        RouteDistanceText = "距离：-";
        RouteProgressValue = 0;
        RouteProgressText = "0%";
        RouteProgressPointText = "0 / 0 个点";
        _currentRoutePoints = [];
        _routeTotalDistance = 0;
        _routeCompletedDistance = 0;
        NextWaypointText = "-";
        NextWaypointActionText = "-";
        NextWaypointActionParamsText = "-";
        MoveModeSummary = "-";
        RefreshDebugJson();

        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "ClearCurrentPathing", new object(), new object()));
    }

    [RelayCommand]
    private void RefreshCurrentView()
    {
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "LocateCurrentMapView", new object(), new object()));
    }

    [RelayCommand]
    private void ZoomIn()
    {
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "MapZoomIn", new object(), new object()));
    }

    [RelayCommand]
    private void ZoomOut()
    {
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "MapZoomOut", new object(), new object()));
    }

    [RelayCommand]
    private void ToggleMapZoom()
    {
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "MapToggleZoom", new object(), new object()));
    }

    [RelayCommand]
    private void SwitchToNextMapLayer()
    {
        if (MapLayerOptions.Count == 0)
        {
            return;
        }

        var index = MapLayerOptions.ToList().FindIndex(i => string.Equals(i.Code, MapName, StringComparison.OrdinalIgnoreCase));
        var next = MapLayerOptions[(index + 1 + MapLayerOptions.Count) % MapLayerOptions.Count];
        MapName = next.Code;
    }

    [RelayCommand]
    private void SetMapLayer(MapEditorOption? option)
    {
        if (option == null || string.IsNullOrWhiteSpace(option.Code))
        {
            return;
        }

        MapName = option.Code;
    }

    [RelayCommand]
    private void ToggleViewSettings()
    {
        IsViewSettingsOpen = !IsViewSettingsOpen;
    }

    [RelayCommand]
    private void ToggleRecordAdvancedSettings()
    {
        IsRecordAdvancedSettingsOpen = !IsRecordAdvancedSettingsOpen;
    }

    [RelayCommand]
    private void ToggleSidePanel()
    {
        IsSidePanelVisible = !IsSidePanelVisible;
    }

    [RelayCommand]
    private void ToggleRouteProgressDisplay()
    {
        _showRouteProgressAsPercent = !_showRouteProgressAsPercent;
        RefreshRouteProgressText();
    }

    [RelayCommand]
    private async Task RebuildRouteGraphAsync()
    {
        if (IsRefreshingRouteDiagnostics)
        {
            return;
        }

        IsRefreshingRouteDiagnostics = true;
        GraphSummary = "正在重建路网...";
        try
        {
            await Task.Run(() =>
            {
                var healthEntries = new RouteHealthStore(_routeSaveDir).GetSnapshot();
                new RouteNavigationGraphBuilder(_routeSaveDir).BuildNow(healthEntries);
            });

            IsRefreshingRouteDiagnostics = false;
            await RefreshRouteGraphDiagnosticsAsync();
        }
        catch (Exception ex)
        {
            GraphSummary = $"重建失败：{ex.Message}";
        }
        finally
        {
            IsRefreshingRouteDiagnostics = false;
        }
    }

    [RelayCommand]
    private async Task RefreshRouteDiagnosticsLiteAsync()
    {
        if (_isRefreshingRouteDiagnosticsLite)
        {
            return;
        }

        _isRefreshingRouteDiagnosticsLite = true;
        HealthSummary = "正在后台刷新健康数据...";
        var mapName = MapName;

        try
        {
            var result = await Task.Run(() => BuildRouteLiteDiagnostics(mapName));

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
            _isRefreshingRouteDiagnosticsLite = false;
        }
    }

    private RouteLiteDiagnosticsResult BuildRouteLiteDiagnostics(string mapName)
    {
        var graphExists = File.Exists(RouteGraphFilePath);
        var graphSizeMb = graphExists ? new FileInfo(RouteGraphFilePath).Length / 1024.0 / 1024.0 : 0;
        var healthExists = File.Exists(RouteHealthFilePath);
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
                SegmentId = ShortRouteId(entry.SegmentId),
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
    private async Task RefreshRouteGraphDiagnosticsAsync()
    {
        if (IsRefreshingRouteDiagnostics)
        {
            return;
        }

        IsRefreshingRouteDiagnostics = true;
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
                    return RouteGraphDiagnosticsResult.Empty(File.Exists(RouteGraphFilePath) ? "路网为空或读取失败" : "路网文件不存在");
                }

                var normalizedMapName = RouteGraphGeometry.NormalizeMapName(MapName);
                var mapNodeCount = graph.Nodes.Count(n => string.Equals(RouteGraphGeometry.NormalizeMapName(n.MapName), normalizedMapName, StringComparison.OrdinalIgnoreCase));
                var mapEdgeCount = graph.Edges.Count(e => string.Equals(RouteGraphGeometry.NormalizeMapName(e.MapName), normalizedMapName, StringComparison.OrdinalIgnoreCase));
                var nearbyNodes = graph.FindNearestNodes(MapName, targetPoint, 8, 220)
                    .Select(candidate => new RouteNearbyNodeRow
                    {
                        NodeId = ShortRouteId(candidate.Node.NodeId),
                        X = candidate.Node.X,
                        Y = candidate.Node.Y,
                        Distance = candidate.Distance,
                        Anchors = candidate.Node.AnchorIds.Count,
                        Resources = candidate.Node.ResourceIds.Count + candidate.Node.ResourceLabelIds.Count
                    })
                    .ToList();
                var nearbyEdges = graph.FindNearestEdges(MapName, targetPoint, 8, 120)
                    .Select(projection => new RouteNearbyEdgeRow
                    {
                        EdgeId = ShortRouteId(projection.Edge.EdgeId),
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
            IsRefreshingRouteDiagnostics = false;
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
                MapName = MapName,
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
                return new { Succeeded = succeeded, Plan = plannedRoute };
            });

            var plan = result.Plan;

            if (!result.Succeeded)
            {
                PlanSummary = $"规划失败：{plan.FailureReason}";
                GraphStatus = File.Exists(RouteGraphFilePath) ? RouteGraphFilePath : "路网文件不存在";
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
                $"StartUnknown {FormatRouteBool(plan.RequiresUnknownStartConnector)} / " +
                $"TargetUnknown {FormatRouteBool(plan.RequiresUnknownTargetConnector)} / " +
                $"Frontier {plan.FrontierNode?.NodeId ?? "-"}";

            for (var i = 0; i < plan.Edges.Count; i++)
            {
                var edge = plan.Edges[i];
                PlannedEdges.Add(new RoutePlanEdgeRow
                {
                    Index = i + 1,
                    FromNodeId = ShortRouteId(edge.FromNodeId),
                    ToNodeId = ShortRouteId(edge.ToNodeId),
                    Cost = edge.Cost,
                    MoveMode = edge.MoveMode,
                    Action = edge.Action,
                    HealthStatus = edge.HealthStatus,
                    IsSyntheticReverse = edge.IsSyntheticReverse,
                    IsBidirectionalCandidate = edge.IsBidirectionalCandidate
                });
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

    [RelayCommand]
    private void SwitchToDebugMode()
    {
        if (!ConfirmJsonEditsBeforeLeavingRecorder(DialogOwner, saveToFileOnApply: false))
        {
            return;
        }

        IsRecorderMode = false;
        IsDebugMode = true;
    }

    [RelayCommand]
    private void SwitchToRecorderMode()
    {
        IsRecorderMode = true;
        IsDebugMode = false;
    }

    public bool HandleRecorderShortcut(Key key, ModifierKeys modifiers, IList selectedItems, bool isTextInputFocused)
    {
        if (!IsRecorderMode || IsJsonEditorMode)
        {
            return false;
        }

        var hasCtrl = modifiers.HasFlag(ModifierKeys.Control);
        var hasShift = modifiers.HasFlag(ModifierKeys.Shift);
        var onlyCtrl = modifiers == ModifierKeys.Control;
        var ctrlShift = modifiers == (ModifierKeys.Control | ModifierKeys.Shift);

        if (hasCtrl && key == Key.S)
        {
            if (hasShift)
            {
                SaveRecordingAs();
            }
            else
            {
                SaveRecording();
            }

            return true;
        }

        if (onlyCtrl && key == Key.Z)
        {
            UndoRecorderEdit();
            return true;
        }

        if ((onlyCtrl && key == Key.Y) || (ctrlShift && key == Key.Z))
        {
            RedoRecorderEdit();
            return true;
        }

        if (onlyCtrl && key == Key.N)
        {
            NewRecording();
            return true;
        }

        if (onlyCtrl && key == Key.O)
        {
            ImportRecording();
            return true;
        }

        if (ctrlShift && key == Key.O)
        {
            ImportAndMergeRecordings();
            return true;
        }

        if (isTextInputFocused)
        {
            return false;
        }

        if (onlyCtrl && key == Key.C)
        {
            CopySelectedRecordedWaypoints(selectedItems);
            return true;
        }

        if (onlyCtrl && key == Key.X)
        {
            CutSelectedRecordedWaypoints(selectedItems);
            return true;
        }

        if (onlyCtrl && key == Key.V)
        {
            PasteRecordedWaypoints(selectedItems);
            return true;
        }

        if (onlyCtrl && key == Key.A)
        {
            SelectAllRecordedWaypoints(selectedItems);
            return true;
        }

        if (onlyCtrl && key == Key.D)
        {
            DuplicateSelectedRecordedWaypoints(selectedItems);
            return true;
        }

        if (key == Key.Delete)
        {
            DeleteSelectedRecordedWaypoints(selectedItems);
            return true;
        }

        return false;
    }

    [RelayCommand]
    private void SwitchToRecorderUiEditor()
    {
        if (!TryApplyJsonEdits(saveToFile: false, allowFilePicker: false, owner: DialogOwner))
        {
            return;
        }

        IsJsonEditorMode = false;
    }

    [RelayCommand]
    private void SwitchToRecorderJsonEditor()
    {
        if (!IsRecorderMode)
        {
            return;
        }

        RefreshDebugJson();
        IsJsonEditorMode = true;
        RecordStatusText = "录制器：JSON 编辑中";
    }

    [RelayCommand]
    private void ApplyJsonEdits()
    {
        if (!CanEditRecorder())
        {
            return;
        }

        TryApplyJsonEdits(saveToFile: false, allowFilePicker: false, owner: DialogOwner);
    }

    [RelayCommand]
    private void RefreshJsonFromUi()
    {
        if (!CanEditRecorder())
        {
            return;
        }

        RefreshDebugJson();
        RecordStatusText = "录制器：已从表单重新生成 JSON";
    }

    private void PrepareForRecordingStart()
    {
        IsRecorderMode = true;
        IsTopmost = false;
        if (!string.IsNullOrWhiteSpace(MapName))
        {
            TaskContext.Instance().Config.DevConfig.RecordMapName = MapName;
        }

        if (!string.IsNullOrWhiteSpace(RecordMapMatchMethod))
        {
            TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod = RecordMapMatchMethod;
        }

        TryActivateGenshinWindow();
        RecordStatusText = "录制器：启动中...";
    }

    [RelayCommand]
    private async Task ToggleRecording()
    {
        if (PathRecorder.Instance.IsRecording)
        {
            await StopRecording();
            return;
        }

        await StartRecording();
    }

    [RelayCommand]
    private async Task StartRecording()
    {
        if (!ConfirmJsonEditsBeforeLeavingRecorder(DialogOwner))
        {
            return;
        }

        PrepareForRecordingStart();
        var started = await Task.Run(() => PathRecorder.Instance.Start());
        LoadRecorderTask(PathRecorder.Instance.CurrentTask);
        IsPathRecorderRecording = started && PathRecorder.Instance.IsRecording;
        RecordStatusText = started && PathRecorder.Instance.IsRecording
            ? $"录制器：录制中 / {RecordedWaypoints.Count} 点"
            : "录制器：启动失败 / 未识别当前位置";
    }

    [RelayCommand]
    private async Task StopRecording()
    {
        if (!PathRecorder.Instance.IsRecording)
        {
            IsPathRecorderRecording = false;
            RecordStatusText = "录制器：未开始";
            return;
        }

        await Task.Run(() => PathRecorder.Instance.Save());
        LoadRecorderTask(PathRecorder.Instance.CurrentTask);
        IsPathRecorderRecording = PathRecorder.Instance.IsRecording;
        RecordStatusText = $"录制器：已停止 / {RecordedWaypoints.Count} 点";
    }

    [RelayCommand]
    private async Task AddCurrentWaypoint()
    {
        if (!ConfirmJsonEditsBeforeLeavingRecorder(DialogOwner))
        {
            return;
        }

        IsRecorderMode = true;
        RecordStatusText = "录制器：识别当前位置...";
        await Task.Run(() => PathRecorder.Instance.AddWaypoint());
        LoadRecorderTask(PathRecorder.Instance.CurrentTask);
        IsPathRecorderRecording = PathRecorder.Instance.IsRecording;
        RecordStatusText = $"录制器：录制中 / {RecordedWaypoints.Count} 点";
    }

    [RelayCommand]
    private void SaveRecording()
    {
        if (!CanEditRecorder())
        {
            return;
        }

        if (!TryApplyJsonEdits(saveToFile: false, allowFilePicker: false, owner: DialogOwner))
        {
            return;
        }

        if (!TryGetRequiredRecordName(out var routeName))
        {
            return;
        }

        var filePath = ResolveRecordFilePath(false, routeName);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        var task = BuildRecordedTask();
        task.Info.Name = routeName;
        task.SaveToFile(filePath);
        _recordFilePath = filePath;
        task.FullPath = filePath;
        task.FileName = Path.GetFileName(filePath);
        RecordFilePathText = $"文件：{filePath}";
        PathRecorder.Instance.ReplaceTask(task, publish: false);
        LoadRecorderTask(task);
        UpdateSelectedRecordedRoute(task, filePath);
        RecordStatusText = $"录制器：已保存 / {Path.GetFileName(filePath)}";
    }

    [RelayCommand]
    private void SaveRecordingAs()
    {
        if (!CanEditRecorder())
        {
            return;
        }

        if (!TryApplyJsonEdits(saveToFile: false, allowFilePicker: false, owner: DialogOwner))
        {
            return;
        }

        if (!TryGetRequiredRecordName(out var routeName))
        {
            return;
        }

        var filePath = ResolveRecordFilePath(true, routeName);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        var task = BuildRecordedTask();
        task.Info.Name = routeName;
        task.SaveToFile(filePath);
        _recordFilePath = filePath;
        task.FullPath = filePath;
        task.FileName = Path.GetFileName(filePath);
        RecordFilePathText = $"文件：{filePath}";
        PathRecorder.Instance.ReplaceTask(task, publish: false);
        LoadRecorderTask(task);
        UpdateSelectedRecordedRoute(task, filePath);
        RecordStatusText = $"录制器：已另存 / {Path.GetFileName(filePath)}";
    }

    [RelayCommand]
    private void ImportRecording()
    {
        if (!CanEditRecorder())
        {
            return;
        }

        if (!ConfirmJsonEditsBeforeLeavingRecorder(DialogOwner))
        {
            return;
        }

        OpenRouteFileBrowser();
    }

    [RelayCommand]
    private void ImportFromSystemDialog()
    {
        ImportRecordingsFromSystemDialog(mergeIntoCurrent: false, multiselect: false);
    }

    [RelayCommand]
    private void ImportMultipleRecordings()
    {
        ImportRecordingsFromSystemDialog(mergeIntoCurrent: false, multiselect: true);
    }

    [RelayCommand]
    private void OpenLegacyRecorderEditor()
    {
        var mapName = string.IsNullOrWhiteSpace(MapName)
            ? TaskContext.Instance().Config.DevConfig.RecordMapName
            : MapName;
        if (string.IsNullOrWhiteSpace(mapName))
        {
            mapName = nameof(MapTypes.Teyvat);
        }

        TaskContext.Instance().Config.DevConfig.RecordMapName = mapName;
        PathRecorder.Instance.OpenEditorInWebView(mapName);
        RecordStatusText = "录制器：已打开旧版录制编辑器";
    }

    private void ImportRecordingsFromSystemDialog(bool mergeIntoCurrent, bool multiselect)
    {
        if (!CanEditRecorder())
        {
            return;
        }

        if (!ConfirmJsonEditsBeforeLeavingRecorder(DialogOwner))
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = mergeIntoCurrent ? "合并路线 JSON" : "导入路线 JSON",
            Filter = "路线 JSON (*.json)|*.json|所有文件 (*.*)|*.*",
            Multiselect = multiselect,
            InitialDirectory = Directory.Exists(MapPathingViewModel.PathJsonPath)
                ? MapPathingViewModel.PathJsonPath
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (mergeIntoCurrent)
        {
            MergeImportedFilesIntoCurrent(dialog.FileNames);
            return;
        }

        ImportRouteFilesToList(dialog.FileNames);
    }

    [RelayCommand]
    private void OpenRouteFileBrowser()
    {
        if (!CanEditRecorder())
        {
            return;
        }

        if (!ConfirmJsonEditsBeforeLeavingRecorder(DialogOwner))
        {
            return;
        }

        IsRouteFileBrowserOpen = true;
        LoadRouteBrowserItems(RouteBrowserRelativePath);
    }

    [RelayCommand]
    private void CloseRouteFileBrowser()
    {
        IsRouteFileBrowserOpen = false;
    }

    [RelayCommand]
    private void RouteBrowserGoBack()
    {
        if (string.IsNullOrWhiteSpace(RouteBrowserRelativePath))
        {
            return;
        }

        var parent = Path.GetDirectoryName(RouteBrowserRelativePath);
        LoadRouteBrowserItems(parent ?? string.Empty);
    }

    [RelayCommand]
    private void RouteBrowserGoRoot()
    {
        LoadRouteBrowserItems(string.Empty);
    }

    [RelayCommand]
    private void RouteBrowserRefresh()
    {
        LoadRouteBrowserItems(RouteBrowserRelativePath);
    }

    [RelayCommand]
    private void RouteBrowserOpenItem(RouteFileBrowserItemViewModel? item)
    {
        if (item == null)
        {
            return;
        }

        if (item.IsDirectory)
        {
            LoadRouteBrowserItems(item.RelativePath);
            return;
        }

        if (item.IsJsonFile)
        {
            item.IsSelected = !item.IsSelected;
        }
    }

    [RelayCommand]
    private void RouteBrowserSelectAll()
    {
        var jsonItems = RouteBrowserItems.Where(i => i.IsJsonFile).ToList();
        if (jsonItems.Count == 0)
        {
            return;
        }

        var shouldSelect = jsonItems.Any(i => !i.IsSelected);
        foreach (var item in jsonItems)
        {
            item.IsSelected = shouldSelect;
        }
    }

    [RelayCommand]
    private void ImportSelectedBrowserFiles()
    {
        var selectedFiles = RouteBrowserItems
            .Where(i => i is { IsSelected: true, IsJsonFile: true })
            .Select(i => i.FullPath)
            .ToList();
        if (selectedFiles.Count == 0)
        {
            RouteBrowserImportStatusText = "请选择至少一个 JSON 文件";
            return;
        }

        ImportRouteFilesToList(selectedFiles);
        RouteBrowserImportStatusText = $"已导入 {selectedFiles.Count} 个文件";
        IsRouteFileBrowserOpen = false;
    }

    private void LoadRouteBrowserItems(string? relativePath)
    {
        _isLoadingRouteBrowser = true;
        try
        {
            Directory.CreateDirectory(MapPathingViewModel.PathJsonPath);
            var safeRelativePath = NormalizeRouteBrowserRelativePath(relativePath);
            var directory = ResolveRouteBrowserPath(safeRelativePath);
            if (directory == null || !Directory.Exists(directory))
            {
                safeRelativePath = string.Empty;
                directory = MapPathingViewModel.PathJsonPath;
            }

            RouteBrowserRelativePath = safeRelativePath;
            RouteBrowserItems.Clear();
            foreach (var item in Directory.GetDirectories(directory)
                         .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
                         .Select(path => RouteFileBrowserItemViewModel.Create(path, MapPathingViewModel.PathJsonPath, isDirectory: true))
                         .Concat(Directory.GetFiles(directory)
                             .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
                             .Select(path => RouteFileBrowserItemViewModel.Create(path, MapPathingViewModel.PathJsonPath, isDirectory: false))))
            {
                RouteBrowserItems.Add(item);
            }

            RouteBrowserImportStatusText = RouteBrowserItems.Count == 0
                ? "当前目录为空"
                : "选择 JSON 文件后导入";
            OnPropertyChanged(nameof(RouteBrowserSelectionText));
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            RouteBrowserImportStatusText = $"读取目录失败：{ex.Message}";
        }
        finally
        {
            _isLoadingRouteBrowser = false;
        }
    }

    private static string NormalizeRouteBrowserRelativePath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath == ".")
        {
            return string.Empty;
        }

        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).Trim();
        normalized = normalized.Trim(Path.DirectorySeparatorChar);
        return normalized.Contains("..", StringComparison.Ordinal)
            ? string.Empty
            : normalized;
    }

    private static string? ResolveRouteBrowserPath(string? relativePath)
    {
        var root = Path.GetFullPath(MapPathingViewModel.PathJsonPath);
        var candidate = string.IsNullOrWhiteSpace(relativePath)
            ? root
            : Path.GetFullPath(Path.Combine(root, relativePath));
        return (candidate.Equals(root, StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            ? candidate
            : null;
    }

    [RelayCommand]
    private void OpenCombatScriptManager()
    {
        IsCombatScriptManagerOpen = true;
    }

    [RelayCommand]
    private void CloseCombatScriptManager()
    {
        IsCombatScriptManagerOpen = false;
    }

    [RelayCommand]
    private void AddCombatScript()
    {
        var value = (NewCombatScriptValue ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            RecordStatusText = "录制器：战斗策略不能为空";
            return;
        }

        if (CombatScripts.Any(i => string.Equals(i.Value, value, StringComparison.Ordinal)))
        {
            RecordStatusText = "录制器：战斗策略已存在";
            return;
        }

        var option = new CombatScriptOptionViewModel(value, NewCombatScriptIsDefault || CombatScripts.Count == 0);
        CombatScripts.Add(option);
        if (option.IsDefault)
        {
            SetDefaultCombatScript(option);
        }

        NewCombatScriptValue = string.Empty;
        NewCombatScriptIsDefault = false;
        RecordStatusText = "录制器：已添加战斗策略";
    }

    [RelayCommand]
    private void DeleteCombatScript(CombatScriptOptionViewModel? option)
    {
        if (option == null)
        {
            return;
        }

        var wasDefault = option.IsDefault;
        CombatScripts.Remove(option);
        if (wasDefault && CombatScripts.Count > 0)
        {
            CombatScripts[0].IsDefault = true;
        }

        RecordStatusText = "录制器：已删除战斗策略";
    }

    [RelayCommand]
    private void SetDefaultCombatScript(CombatScriptOptionViewModel? option)
    {
        if (option == null)
        {
            return;
        }

        foreach (var item in CombatScripts)
        {
            item.IsDefault = ReferenceEquals(item, option);
        }

        SaveCombatScripts();
    }

    private string GetDefaultCombatScriptValue()
    {
        return CombatScripts.FirstOrDefault(i => i.IsDefault)?.Value ?? string.Empty;
    }

    private void AddCombatScriptFromWaypointIfNeeded(Waypoint waypoint)
    {
        if (!string.Equals(waypoint.Action, ActionEnum.CombatScript.Code, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(waypoint.ActionParams)
            || CombatScripts.Any(i => string.Equals(i.Value, waypoint.ActionParams, StringComparison.Ordinal)))
        {
            return;
        }

        CombatScripts.Add(new CombatScriptOptionViewModel(waypoint.ActionParams.Trim(), CombatScripts.Count == 0));
    }

    private void LoadCombatScripts()
    {
        try
        {
            _isLoadingCombatScripts = true;
            var path = GetCombatScriptStorePath();
            if (!File.Exists(path))
            {
                return;
            }

            var json = File.ReadAllText(path);
            var items = Newtonsoft.Json.JsonConvert.DeserializeObject<List<CombatScriptOptionStoreItem>>(json) ?? [];
            foreach (var item in items.Where(i => !string.IsNullOrWhiteSpace(i.Value)))
            {
                CombatScripts.Add(new CombatScriptOptionViewModel(item.Value.Trim(), item.Def));
            }

            EnsureSingleDefaultCombatScript();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
        finally
        {
            _isLoadingCombatScripts = false;
        }
    }

    private void SaveCombatScripts()
    {
        try
        {
            var path = GetCombatScriptStorePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? MapPathingViewModel.PathJsonPath);
            EnsureSingleDefaultCombatScript();
            var items = CombatScripts
                .Where(i => !string.IsNullOrWhiteSpace(i.Value))
                .Select(i => new CombatScriptOptionStoreItem { Value = i.Value.Trim(), Def = i.IsDefault })
                .ToList();
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(items, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(path, json, new System.Text.UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private void EnsureSingleDefaultCombatScript()
    {
        var defaults = CombatScripts.Where(i => i.IsDefault).ToList();
        if (defaults.Count == 0 && CombatScripts.Count > 0)
        {
            CombatScripts[0].IsDefault = true;
            return;
        }

        foreach (var item in defaults.Skip(1))
        {
            item.IsDefault = false;
        }
    }

    private static string GetCombatScriptStorePath()
    {
        return Path.Combine(MapPathingViewModel.PathJsonPath, ".editor", "combat_scripts.json");
    }

    [RelayCommand]
    private void ImportAndMergeRecordings()
    {
        ImportRecordingsFromSystemDialog(mergeIntoCurrent: true, multiselect: true);
    }

    private void MergeImportedFilesIntoCurrent(IReadOnlyList<string> fileNames)
    {
        if (fileNames.Count == 0)
        {
            return;
        }

        var appendCount = 0;
        if (RecordedWaypoints.Count == 0)
        {
            var firstTask = PathingTask.BuildFromFilePath(fileNames[0]);
            if (firstTask == null)
            {
                ThemedMessageBox.Error("路线文件解析失败，可能是 JSON 格式无效或要求的 BetterGI 版本高于当前版本。", "导入路线失败", MessageBoxButton.OK, MessageBoxResult.OK);
                return;
            }

            MapName = string.IsNullOrWhiteSpace(firstTask.Info.MapName) ? MapName : firstTask.Info.MapName;
            _recordFilePath = firstTask.FullPath;
            RecordFilePathText = $"文件：{firstTask.FullPath}";
            LoadRecorderTask(firstTask);
            appendCount += firstTask.Positions.Count;
        }

        var startIndex = appendCount > 0 ? 1 : 0;
        for (var i = startIndex; i < fileNames.Count; i++)
        {
            var task = PathingTask.BuildFromFilePath(fileNames[i]);
            if (task == null)
            {
                continue;
            }

            foreach (var waypoint in task.Positions)
            {
                RecordedWaypoints.Add(CreateRecordedWaypointViewModel(waypoint));
                appendCount++;
            }
        }

        ReindexRecordedWaypoints();
        SelectedRecordedWaypoint = RecordedWaypoints.FirstOrDefault();
        RecordStatusText = $"录制器：已合并 {fileNames.Count} 个文件 / {appendCount} 点";
        PublishRecorderPath();
    }

    [RelayCommand]
    private void SplitRecordingByTeleport()
    {
        if (!CanEditRecorder())
        {
            return;
        }

        if (!TryApplyJsonEdits(saveToFile: false, allowFilePicker: false, owner: DialogOwner))
        {
            return;
        }

        var task = BuildRecordedTask();
        var groups = SplitWaypointsByTeleport(task.Positions);
        if (groups.Count <= 1)
        {
            RecordStatusText = "录制器：没有可按传送点拆分的路线";
            return;
        }

        var routeName = string.IsNullOrWhiteSpace(task.Info.Name) ? RecordFileName : task.Info.Name.Trim();
        var selectedIndex = SelectedRecordedRoute == null ? RecordedRoutes.Count : RecordedRoutes.IndexOf(SelectedRecordedRoute);
        if (selectedIndex < 0)
        {
            selectedIndex = RecordedRoutes.Count;
        }

        if (SelectedRecordedRoute != null)
        {
            RecordedRoutes.Remove(SelectedRecordedRoute);
        }

        var firstSplitRoute = default(RecordedRouteViewModel);
        for (var i = 0; i < groups.Count; i++)
        {
            var splitTask = ClonePathingTask(task);
            splitTask.Info.Name = i == 0 ? routeName : $"{routeName}_{i + 1}";
            splitTask.Positions = groups[i];
            splitTask.FullPath = string.Empty;
            splitTask.FileName = string.Empty;
            var splitRoute = CreateRecordedRoute(splitTask, string.Empty);
            RecordedRoutes.Insert(Math.Min(selectedIndex + i, RecordedRoutes.Count), splitRoute);
            firstSplitRoute ??= splitRoute;
        }

        SelectRecordedRouteWithoutReload(firstSplitRoute);
        if (firstSplitRoute != null)
        {
            LoadRecordedRoute(firstSplitRoute);
        }

        RecordStatusText = $"录制器：已按传送点拆分为 {groups.Count} 条路线";
    }

    [RelayCommand]
    private void DeleteSelectedRecordedRoute()
    {
        if (!CanEditRecorder() || SelectedRecordedRoute == null)
        {
            return;
        }

        var route = SelectedRecordedRoute;
        var index = RecordedRoutes.IndexOf(route);
        RecordedRoutes.Remove(route);
        if (RecordedRoutes.Count == 0)
        {
            NewRecording();
            return;
        }

        SelectedRecordedRoute = RecordedRoutes[Math.Clamp(index, 0, RecordedRoutes.Count - 1)];
        RecordStatusText = $"录制器：已删除路线 / {route.Name}";
    }

    [RelayCommand]
    private void MergeRecordedRoutes()
    {
        if (!CanEditRecorder())
        {
            return;
        }

        if (RecordedRoutes.Count <= 1)
        {
            RecordStatusText = "录制器：至少需要两条路线才能合并";
            return;
        }

        var baseRoute = SelectedRecordedRoute ?? RecordedRoutes[0];
        var baseTask = ClonePathingTask(baseRoute.Task);
        baseTask.Positions = RecordedRoutes
            .SelectMany(route => route.Task.Positions.Select(CloneWaypoint))
            .ToList();
        baseTask.Info.Name = string.IsNullOrWhiteSpace(baseTask.Info.Name) ? baseRoute.Name : baseTask.Info.Name;
        baseTask.FullPath = string.Empty;
        baseTask.FileName = string.Empty;

        RecordedRoutes.Clear();
        var mergedRoute = CreateRecordedRoute(baseTask, string.Empty);
        RecordedRoutes.Add(mergedRoute);
        SelectRecordedRouteWithoutReload(mergedRoute);
        LoadRecordedRoute(mergedRoute);
        RecordStatusText = $"录制器：已合并为 1 条路线 / {baseTask.Positions.Count} 点";
    }

    private void ImportRouteFilesToList(IReadOnlyList<string> fileNames)
    {
        if (fileNames.Count == 0)
        {
            return;
        }

        var successCount = 0;
        var failedFiles = new List<string>();
        RecordedRouteViewModel? firstImported = null;
        foreach (var fileName in fileNames)
        {
            try
            {
                var task = PathingTask.BuildFromFilePath(fileName);
                if (task == null)
                {
                    failedFiles.Add(Path.GetFileName(fileName));
                    continue;
                }

                var route = AddRecordedRoute(task, fileName, select: firstImported == null && SelectedRecordedRoute == null);
                firstImported ??= route;
                successCount++;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                failedFiles.Add(Path.GetFileName(fileName));
            }
        }

        if (firstImported != null)
        {
            SelectedRecordedRoute = firstImported;
        }

        RecordStatusText = failedFiles.Count == 0
            ? $"录制器：已导入 {successCount} 条路线"
            : $"录制器：已导入 {successCount} 条路线 / {failedFiles.Count} 个失败";
    }

    private RecordedRouteViewModel AddRecordedRoute(PathingTask task, string? filePath, bool select)
    {
        task.Info ??= new PathingTaskInfo();
        task.Positions ??= [];
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            task.FullPath = filePath;
            task.FileName = Path.GetFileName(filePath);
        }

        if (string.IsNullOrWhiteSpace(task.Info.Name))
        {
            task.Info.Name = !string.IsNullOrWhiteSpace(filePath)
                ? Path.GetFileNameWithoutExtension(filePath)
                : "未命名路线";
        }

        var route = CreateRecordedRoute(task, filePath ?? task.FullPath);
        RecordedRoutes.Add(route);
        if (select)
        {
            SelectedRecordedRoute = route;
        }

        return route;
    }

    private RecordedRouteViewModel CreateRecordedRoute(PathingTask task, string? filePath)
    {
        return new RecordedRouteViewModel(ClonePathingTask(task), filePath ?? task.FullPath);
    }

    private void LoadRecordedRoute(RecordedRouteViewModel? route)
    {
        if (route == null)
        {
            return;
        }

        _isSwitchingRecordedRoute = true;
        try
        {
            foreach (var item in RecordedRoutes)
            {
                item.IsSelected = ReferenceEquals(item, route);
            }

            var task = ClonePathingTask(route.Task);
            MapName = string.IsNullOrWhiteSpace(task.Info.MapName) ? MapName : task.Info.MapName;
            _recordFilePath = string.IsNullOrWhiteSpace(route.FilePath) ? null : route.FilePath;
            RecordFilePathText = string.IsNullOrWhiteSpace(_recordFilePath) ? "文件：未保存" : $"文件：{_recordFilePath}";
            LoadRecorderTask(task);
            PathRecorder.Instance.ReplaceTask(task, publish: false);
            RecordStatusText = $"录制器：已选择路线 / {route.Name}";
        }
        finally
        {
            _isSwitchingRecordedRoute = false;
        }

        PublishRecorderPath();
    }

    private void SelectRecordedRouteWithoutReload(RecordedRouteViewModel? route)
    {
        _isSwitchingRecordedRoute = true;
        try
        {
            SelectedRecordedRoute = route;
            foreach (var item in RecordedRoutes)
            {
                item.IsSelected = ReferenceEquals(item, route);
            }
        }
        finally
        {
            _isSwitchingRecordedRoute = false;
        }
    }

    [RelayCommand]
    private async Task RunRecording()
    {
        if (!ConfirmJsonEditsBeforeLeavingRecorder(DialogOwner))
        {
            return;
        }

        await RunRecordedTaskFromIndex(0);
    }

    [RelayCommand]
    private async Task RunRecordingFromWaypoint(RecordedWaypointViewModel? waypoint)
    {
        if (!ConfirmJsonEditsBeforeLeavingRecorder(DialogOwner))
        {
            return;
        }

        waypoint ??= SelectedRecordedWaypoint;
        var index = waypoint == null ? 0 : RecordedWaypoints.IndexOf(waypoint);
        await RunRecordedTaskFromIndex(Math.Max(0, index));
    }

    [RelayCommand]
    private void NewRecording()
    {
        if (!CanEditRecorder())
        {
            return;
        }

        if (!ConfirmJsonEditsBeforeLeavingRecorder(DialogOwner))
        {
            return;
        }

        RecordedWaypoints.Clear();
        SelectedRecordedWaypoint = null;
        RecordFileName = "未命名路线";
        _recordFilePath = null;
        _recordTaskTemplate = null;
        RecordFilePathText = "文件：未保存";
        RecordDescription = string.Empty;
        RecordAuthorName = DefaultRecordAuthorName.Trim();
        RecordAuthorLinks = DefaultRecordAuthorLinks.Trim();
        RecordVersion = "1.0";
        RecordTagsText = string.Empty;
        RecordEnableMonsterLootSplit = false;
        RecordMapMatchMethod = TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod;
        RecordStatusText = "录制器：新路线";
        var newRoute = CreateRecordedRoute(BuildRecordedTask(), string.Empty);
        RecordedRoutes.Add(newRoute);
        SelectRecordedRouteWithoutReload(newRoute);
        RefreshDebugJson();
        PublishRecorderPath();
    }

    [RelayCommand]
    private void AddPathPointFromTarget()
    {
        if (!CanEditRecorder())
        {
            return;
        }

        if (!TryParseTargetPoint(out var targetPoint))
        {
            RecordStatusText = "录制器：请先点击地图选择点";
            return;
        }

        AddRecordedWaypoint(new Waypoint
        {
            X = targetPoint.X,
            Y = targetPoint.Y,
            Type = WaypointType.Path.Code,
            MoveMode = MoveModeEnum.Walk.Code
        });
    }

    [RelayCommand]
    private void CopyRecordedWaypoint(RecordedWaypointViewModel? waypoint)
    {
        if (!CanEditRecorder())
        {
            return;
        }

        waypoint ??= SelectedRecordedWaypoint;
        if (waypoint == null)
        {
            return;
        }

        var index = RecordedWaypoints.IndexOf(waypoint);
        var copied = new RecordedWaypointViewModel(waypoint.ToWaypoint());
        RecordedWaypoints.Insert(index + 1, copied);
        SelectedRecordedWaypoint = copied;
        ReindexRecordedWaypoints();
        PublishRecorderPath();
    }

    private void CopySelectedRecordedWaypoints(IList selectedItems)
    {
        if (!CanEditRecorder())
        {
            return;
        }

        var selected = GetSelectedRecordedWaypoints(selectedItems);
        if (selected.Count == 0)
        {
            return;
        }

        CopyWaypointsToClipboard(selected);
        RecordStatusText = $"录制器：已复制 {selected.Count} 个点";
    }

    [RelayCommand]
    private void CopyWaypointSelection()
    {
        if (!CanEditRecorder())
        {
            return;
        }

        var selected = GetSelectedRecordedWaypointsFromState();
        if (selected.Count == 0)
        {
            return;
        }

        CopyWaypointsToClipboard(selected);
        RecordStatusText = $"录制器：已复制 {selected.Count} 个点";
    }

    private void CutSelectedRecordedWaypoints(IList selectedItems)
    {
        if (!CanEditRecorder())
        {
            return;
        }

        var selected = GetSelectedRecordedWaypoints(selectedItems);
        if (selected.Count == 0)
        {
            return;
        }

        CopyWaypointsToClipboard(selected);
        RemoveRecordedWaypoints(selected);
        RecordStatusText = $"录制器：已剪切 {selected.Count} 个点";
    }

    [RelayCommand]
    private void CutWaypointSelection()
    {
        if (!CanEditRecorder())
        {
            return;
        }

        var selected = GetSelectedRecordedWaypointsFromState();
        if (selected.Count == 0)
        {
            return;
        }

        CopyWaypointsToClipboard(selected);
        RemoveRecordedWaypoints(selected);
        RecordStatusText = $"录制器：已剪切 {selected.Count} 个点";
    }

    private void PasteRecordedWaypoints(IList selectedItems)
    {
        if (!CanEditRecorder())
        {
            return;
        }

        if (TryReadPathingTaskFromClipboard(out var task, out var routeErrorMessage))
        {
            selectedItems.Clear();
            _recordFilePath = null;
            RecordFilePathText = "文件：未保存";
            LoadRecorderTask(task);
            PathRecorder.Instance.ReplaceTask(task, publish: false);
            IsJsonEditorMode = false;
            RecordStatusText = $"录制器：已从剪贴板载入路线 / {RecordFileName}";
            return;
        }

        if (!string.IsNullOrWhiteSpace(routeErrorMessage))
        {
            RecordStatusText = $"录制器：剪贴板路线无效 / {routeErrorMessage}";
            return;
        }

        var waypoints = ReadWaypointsFromClipboard();
        if (waypoints.Count == 0)
        {
            return;
        }

        var selected = GetSelectedRecordedWaypoints(selectedItems);
        var insertIndex = selected.Count == 0
            ? RecordedWaypoints.Count
            : RecordedWaypoints.IndexOf(selected.Last()) + 1;

        var pasted = waypoints.Select(i => CreateRecordedWaypointViewModel(i)).ToList();
        for (var i = 0; i < pasted.Count; i++)
        {
            RecordedWaypoints.Insert(insertIndex + i, pasted[i]);
        }

        selectedItems.Clear();
        foreach (var item in pasted)
        {
            selectedItems.Add(item);
        }

        SelectedRecordedWaypoint = pasted.FirstOrDefault();
        ReindexRecordedWaypoints();
        PublishRecorderPath();
        RecordStatusText = $"录制器：已粘贴 {pasted.Count} 个点";
    }

    [RelayCommand]
    private void PasteWaypointSelection()
    {
        if (!CanEditRecorder())
        {
            return;
        }

        if (TryReadPathingTaskFromClipboard(out var task, out var routeErrorMessage))
        {
            _recordFilePath = null;
            RecordFilePathText = "文件：未保存";
            LoadRecorderTask(task);
            PathRecorder.Instance.ReplaceTask(task, publish: false);
            IsJsonEditorMode = false;
            ClearWaypointSelectionState();
            RecordStatusText = $"录制器：已从剪贴板载入路线 / {RecordFileName}";
            return;
        }

        if (!string.IsNullOrWhiteSpace(routeErrorMessage))
        {
            RecordStatusText = $"录制器：剪贴板路线无效 / {routeErrorMessage}";
            return;
        }

        var waypoints = ReadWaypointsFromClipboard();
        if (waypoints.Count == 0)
        {
            return;
        }

        var selected = GetSelectedRecordedWaypointsFromState();
        var insertIndex = selected.Count == 0
            ? RecordedWaypoints.Count
            : RecordedWaypoints.IndexOf(selected.Last()) + 1;

        var pasted = waypoints.Select(i => CreateRecordedWaypointViewModel(i)).ToList();
        for (var i = 0; i < pasted.Count; i++)
        {
            RecordedWaypoints.Insert(insertIndex + i, pasted[i]);
        }

        ClearWaypointSelectionState();
        foreach (var item in pasted)
        {
            item.IsSelected = true;
        }

        SelectedRecordedWaypoint = pasted.FirstOrDefault();
        ReindexRecordedWaypoints();
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(
            this,
            "SelectRecordedWaypointRows",
            new object(),
            pasted));
        PublishSelectedRecorderWaypoints(pasted);
        PublishRecorderPath();
        RecordStatusText = $"录制器：已粘贴 {pasted.Count} 个点";
    }

    private void DuplicateSelectedRecordedWaypoints(IList selectedItems)
    {
        if (!CanEditRecorder())
        {
            return;
        }

        var selected = GetSelectedRecordedWaypoints(selectedItems);
        if (selected.Count == 0)
        {
            return;
        }

        CopyWaypointsToClipboard(selected);
        PasteRecordedWaypoints(selectedItems);
    }

    [RelayCommand]
    private void DuplicateWaypointSelection()
    {
        if (!CanEditRecorder())
        {
            return;
        }

        var selected = GetSelectedRecordedWaypointsFromState();
        if (selected.Count == 0)
        {
            return;
        }

        CopyWaypointsToClipboard(selected);
        PasteWaypointSelection();
    }

    private void DeleteSelectedRecordedWaypoints(IList selectedItems)
    {
        if (!CanEditRecorder())
        {
            return;
        }

        var selected = GetSelectedRecordedWaypoints(selectedItems);
        if (selected.Count == 0)
        {
            return;
        }

        RemoveRecordedWaypoints(selected);
        RecordStatusText = $"录制器：已删除 {selected.Count} 个点";
    }

    [RelayCommand]
    private void DeleteWaypointSelection()
    {
        if (!CanEditRecorder())
        {
            return;
        }

        var selected = GetSelectedRecordedWaypointsFromState();
        if (selected.Count == 0)
        {
            return;
        }

        RemoveRecordedWaypoints(selected);
        RecordStatusText = $"录制器：已删除 {selected.Count} 个点";
    }

    private void SelectAllRecordedWaypoints(IList selectedItems)
    {
        if (!CanEditRecorder())
        {
            return;
        }

        selectedItems.Clear();
        foreach (var item in RecordedWaypointView.Cast<RecordedWaypointViewModel>())
        {
            selectedItems.Add(item);
        }

        SelectedRecordedWaypoint = selectedItems.OfType<RecordedWaypointViewModel>().FirstOrDefault();
        RecordStatusText = $"录制器：已选中 {selectedItems.Count} 个点";
    }

    [RelayCommand]
    private void SelectAllWaypointSelection()
    {
        if (!CanEditRecorder())
        {
            return;
        }

        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(
            this,
            "SelectAllRecordedWaypointRows",
            new object(),
            new object()));
    }

    [RelayCommand]
    private void ClearWaypointSelection()
    {
        ClearWaypointSelectionState();
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(
            this,
            "ClearRecordedWaypointRowsSelection",
            new object(),
            new object()));
        PublishSelectedRecorderWaypoints();
        RecordStatusText = "录制器：已取消点位选择";
    }

    [RelayCommand]
    private void ToggleWaypointLock(RecordedWaypointViewModel? waypoint)
    {
        if (!CanEditRecorder())
        {
            return;
        }

        waypoint ??= SelectedRecordedWaypoint;
        if (waypoint == null)
        {
            return;
        }

        if (!waypoint.IsLocked)
        {
            foreach (var item in RecordedWaypoints)
            {
                item.IsLocked = false;
            }
        }

        waypoint.IsLocked = !waypoint.IsLocked;
        SelectedRecordedWaypoint = waypoint;
        RecordStatusText = waypoint.IsLocked
            ? $"录制器：第 {waypoint.Index} 点已设为插入位置"
            : "录制器：已取消插入位置";
    }

    [RelayCommand]
    private void OpenWaypointAdvancedEditor(RecordedWaypointViewModel? waypoint)
    {
        if (!CanEditRecorder())
        {
            return;
        }

        waypoint ??= SelectedRecordedWaypoint;
        if (waypoint == null)
        {
            return;
        }

        SelectedRecordedWaypoint = waypoint;
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(
            this,
            "OpenWaypointAdvancedEditor",
            new object(),
            waypoint));
    }

    [RelayCommand]
    private void EditRecordedWaypoint(RecordedWaypointViewModel? waypoint)
    {
        if (!CanEditRecorder())
        {
            return;
        }

        waypoint ??= SelectedRecordedWaypoint;
        if (waypoint == null)
        {
            return;
        }

        SelectedRecordedWaypoint = waypoint;
    }

    [RelayCommand]
    private void ClearRecordedWaypoints()
    {
        if (!CanEditRecorder())
        {
            return;
        }

        var result = ThemedMessageBox.Show(
            "确定要清除所有路线点吗？此操作可以用撤销恢复。",
            "清空路线点",
            MessageBoxButton.OKCancel,
            ThemedMessageBox.MessageBoxIcon.Warning,
            MessageBoxResult.Cancel,
            DialogOwner);
        if (result != System.Windows.MessageBoxResult.OK)
        {
            return;
        }

        RecordedWaypoints.Clear();
        SelectedRecordedWaypoint = null;
        RecordStatusText = "录制器：已清空路线点";
        PublishRecorderPath();
    }

    [RelayCommand]
    private void ApplyDefaultAuthor()
    {
        if (!CanEditRecorder())
        {
            return;
        }

        RecordAuthorName = DefaultRecordAuthorName.Trim();
        RecordAuthorLinks = DefaultRecordAuthorLinks.Trim();
        PublishRecorderPath();
    }

    [RelayCommand]
    private void UndoRecorderEdit()
    {
        if (!CanEditRecorder())
        {
            return;
        }

        if (_recorderHistoryIndex <= 0)
        {
            return;
        }

        RestoreRecorderHistory(_recorderHistoryIndex - 1);
    }

    [RelayCommand]
    private void RedoRecorderEdit()
    {
        if (!CanEditRecorder())
        {
            return;
        }

        if (_recorderHistoryIndex >= _recorderHistory.Count - 1)
        {
            return;
        }

        RestoreRecorderHistory(_recorderHistoryIndex + 1);
    }

    [RelayCommand]
    private void DeleteRecordedWaypoint(RecordedWaypointViewModel? waypoint)
    {
        if (!CanEditRecorder())
        {
            return;
        }

        waypoint ??= SelectedRecordedWaypoint;
        if (waypoint == null)
        {
            return;
        }

        var result = ThemedMessageBox.Show(
            $"确定要删除第 {waypoint.Index} 个路线点吗？此操作可以用撤销恢复。",
            "删除路线点",
            MessageBoxButton.OKCancel,
            ThemedMessageBox.MessageBoxIcon.Warning,
            MessageBoxResult.Cancel,
            DialogOwner);
        if (result != System.Windows.MessageBoxResult.OK)
        {
            return;
        }

        RecordedWaypoints.Remove(waypoint);
        SelectedRecordedWaypoint = RecordedWaypoints.FirstOrDefault();
        ReindexRecordedWaypoints();
        PublishRecorderPath();
    }

    [RelayCommand]
    private void MoveRecordedWaypointUp(RecordedWaypointViewModel? waypoint)
    {
        if (!CanEditRecorder())
        {
            return;
        }

        waypoint ??= SelectedRecordedWaypoint;
        if (waypoint == null)
        {
            return;
        }

        var index = RecordedWaypoints.IndexOf(waypoint);
        if (index <= 0)
        {
            return;
        }

        RecordedWaypoints.Move(index, index - 1);
        ReindexRecordedWaypoints();
        PublishRecorderPath();
    }

    [RelayCommand]
    private void MoveRecordedWaypointDown(RecordedWaypointViewModel? waypoint)
    {
        if (!CanEditRecorder())
        {
            return;
        }

        waypoint ??= SelectedRecordedWaypoint;
        if (waypoint == null)
        {
            return;
        }

        var index = RecordedWaypoints.IndexOf(waypoint);
        if (index < 0 || index >= RecordedWaypoints.Count - 1)
        {
            return;
        }

        RecordedWaypoints.Move(index, index + 1);
        ReindexRecordedWaypoints();
        PublishRecorderPath();
    }

    private void RefreshBitmap(Point2f point)
    {
        UIDispatcherHelper.BeginInvoke(() =>
        {
            try
            {
                MapBitmap.Lock();
                WriteableBitmapConverter.ToWriteableBitmap(ClipMat(point), MapBitmap);
                LastRefreshText = $"刷新：{DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
            finally
            {
                MapBitmap.Unlock();
            }
        });
    }

    public void SelectTargetFromViewerPoint(WpfPoint point, double viewWidth, double viewHeight)
    {
        if (viewWidth <= 0 || viewHeight <= 0 || _lastClipPixelSize.Width <= 0 || _lastClipPixelSize.Height <= 0)
        {
            return;
        }

        var imageAspect = _lastClipPixelSize.Width / (double)_lastClipPixelSize.Height;
        var viewAspect = viewWidth / viewHeight;
        double displayedWidth;
        double displayedHeight;
        double offsetX;
        double offsetY;

        if (viewAspect > imageAspect)
        {
            displayedHeight = viewHeight;
            displayedWidth = displayedHeight * imageAspect;
            offsetX = (viewWidth - displayedWidth) / 2.0;
            offsetY = 0;
        }
        else
        {
            displayedWidth = viewWidth;
            displayedHeight = displayedWidth / imageAspect;
            offsetX = 0;
            offsetY = (viewHeight - displayedHeight) / 2.0;
        }

        var localX = point.X - offsetX;
        var localY = point.Y - offsetY;
        if (localX < 0 || localY < 0 || localX > displayedWidth || localY > displayedHeight)
        {
            return;
        }

        var sourceX = localX / displayedWidth * _lastClipGlobalRect.Width;
        var sourceY = localY / displayedHeight * _lastClipGlobalRect.Height;
        var selectedPoint = new Point2f(
            (float)RoundCoordinate(_lastClipGlobalRect.X + sourceX),
            (float)RoundCoordinate(_lastClipGlobalRect.Y + sourceY));

        SelectedTargetText = $"目标点：{FormatCoordinate(selectedPoint.X)}, {FormatCoordinate(selectedPoint.Y)}";
        _selectedTargetPoint = selectedPoint;
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "SelectPathingTargetPosition", new object(), selectedPoint));
    }

    private void UpdateRoutePlanningCurrentPosition(Point2f point)
    {
        if (!FollowRoutePlanningCurrentPosition)
        {
            return;
        }

        CurrentImageX = Math.Round(point.X, 1);
        CurrentImageY = Math.Round(point.Y, 1);
    }

    private void HandleMapPointSelected(Point2f targetPoint)
    {
        _selectedTargetPoint = targetPoint;
        SelectedTargetText = $"目标点：{FormatCoordinate(targetPoint.X)}, {FormatCoordinate(targetPoint.Y)}";
        TargetImageX = Math.Round(targetPoint.X, 1);
        TargetImageY = Math.Round(targetPoint.Y, 1);
        TargetPickSummary = $"目标点：{TargetImageX:F1}, {TargetImageY:F1}";
        _ = RefreshRouteDiagnosticsLiteAsync();
        if (!IsRecorderMode)
        {
            return;
        }

        if (UpdateSelectedPointOnMapClick && SelectedRecordedWaypoint != null)
        {
            SelectedRecordedWaypoint.X = RoundCoordinate(targetPoint.X);
            SelectedRecordedWaypoint.Y = RoundCoordinate(targetPoint.Y);
            RecordStatusText = $"录制器：已更新第 {SelectedRecordedWaypoint.Index} 点";
            PublishRecorderPath();
            return;
        }

        AddRecordedWaypoint(new Waypoint
        {
            X = RoundCoordinate(targetPoint.X),
            Y = RoundCoordinate(targetPoint.Y),
            Type = WaypointType.Path.Code,
            MoveMode = MoveModeEnum.Walk.Code
        });
    }

    private void SelectRecordedWaypointByIndex(int waypointIndex)
    {
        if (waypointIndex < 0 || waypointIndex >= RecordedWaypoints.Count)
        {
            return;
        }

        SelectedRecordedWaypoint = RecordedWaypoints[waypointIndex];
        foreach (var item in RecordedWaypoints)
        {
            item.IsSelected = ReferenceEquals(item, SelectedRecordedWaypoint);
        }

        PublishSelectedRecorderWaypoints();
    }

    private void MoveRecordedWaypointFromMap(int waypointIndex, Point2f point)
    {
        if (!CanEditRecorder() || waypointIndex < 0 || waypointIndex >= RecordedWaypoints.Count)
        {
            return;
        }

        var waypoint = RecordedWaypoints[waypointIndex];
        _isUpdatingWaypointFromMap = true;
        try
        {
            waypoint.X = RoundCoordinate(point.X);
            waypoint.Y = RoundCoordinate(point.Y);
        }
        finally
        {
            _isUpdatingWaypointFromMap = false;
        }

        SelectedRecordedWaypoint = waypoint;
        RecordStatusText = $"录制器：已移动第 {waypoint.Index} 点";
        PublishSelectedRecorderWaypoints();
        PublishRecorderPath();
    }

    private bool TryParseTargetPoint(out Point2f targetPoint)
    {
        if (_selectedTargetPoint is { } point)
        {
            targetPoint = point;
            return true;
        }

        targetPoint = default;
        return false;
    }

    private void AddRecordedWaypoint(Waypoint waypoint)
    {
        if (!CanEditRecorder())
        {
            return;
        }

        var viewModel = CreateRecordedWaypointViewModel(waypoint);
        var lockedIndex = RecordedWaypoints.ToList().FindIndex(i => i.IsLocked);
        if (lockedIndex >= 0)
        {
            RecordedWaypoints.Insert(lockedIndex, viewModel);
            foreach (var item in RecordedWaypoints)
            {
                item.IsLocked = false;
            }
        }
        else
        {
            RecordedWaypoints.Add(viewModel);
        }

        SelectedRecordedWaypoint = viewModel;
        ReindexRecordedWaypoints();
        RecordStatusText = $"录制器：编辑中 / {RecordedWaypoints.Count} 点";
        PublishRecorderPath();
    }

    private List<RecordedWaypointViewModel> GetSelectedRecordedWaypoints(IList selectedItems)
    {
        var selected = selectedItems.OfType<RecordedWaypointViewModel>()
            .Where(i => RecordedWaypoints.Contains(i))
            .OrderBy(i => RecordedWaypoints.IndexOf(i))
            .ToList();

        if (selected.Count == 0 && SelectedRecordedWaypoint != null && RecordedWaypoints.Contains(SelectedRecordedWaypoint))
        {
            selected.Add(SelectedRecordedWaypoint);
        }

        return selected;
    }

    private List<RecordedWaypointViewModel> GetSelectedRecordedWaypointsFromState()
    {
        var selected = RecordedWaypoints
            .Where(i => i.IsSelected)
            .OrderBy(i => RecordedWaypoints.IndexOf(i))
            .ToList();

        if (selected.Count == 0 && SelectedRecordedWaypoint != null && RecordedWaypoints.Contains(SelectedRecordedWaypoint))
        {
            selected.Add(SelectedRecordedWaypoint);
        }

        return selected;
    }

    private void ClearWaypointSelectionState()
    {
        foreach (var waypoint in RecordedWaypoints)
        {
            waypoint.IsSelected = false;
        }

        SelectedRecordedWaypoint = null;
    }

    private void CopyWaypointsToClipboard(List<RecordedWaypointViewModel> selected)
    {
        _recordedWaypointClipboard = selected.Select(i => i.ToWaypoint()).ToList();
        _recordedWaypointClipboardText = JsonSerializer.Serialize(_recordedWaypointClipboard, PathRecorder.JsonOptions);
        try
        {
            System.Windows.Clipboard.SetText(_recordedWaypointClipboardText);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private bool TryReadPathingTaskFromClipboard(out PathingTask task, out string? errorMessage)
    {
        task = new PathingTask();
        errorMessage = null;

        try
        {
            if (!System.Windows.Clipboard.ContainsText())
            {
                return false;
            }

            var text = System.Windows.Clipboard.GetText();
            if (!LooksLikePathingTaskJson(text))
            {
                return false;
            }

            if (TryBuildTaskFromJsonEditor(text, out task, out var validationError))
            {
                return true;
            }

            errorMessage = validationError.Replace(Environment.NewLine, " ");
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return false;
        }
    }

    private static bool LooksLikePathingTaskJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                   && root.TryGetProperty("info", out var info)
                   && info.ValueKind == JsonValueKind.Object
                   && root.TryGetProperty("positions", out var positions)
                   && positions.ValueKind == JsonValueKind.Array;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return false;
        }
    }

    private List<Waypoint> ReadWaypointsFromClipboard()
    {
        var clipboardHadText = false;
        try
        {
            if (System.Windows.Clipboard.ContainsText())
            {
                clipboardHadText = true;
                var text = System.Windows.Clipboard.GetText();

                try
                {
                    var waypoints = JsonSerializer.Deserialize<List<Waypoint>>(text, PathRecorder.JsonOptions);
                    if (waypoints is { Count: > 0 })
                    {
                        return waypoints.Select(CloneWaypoint).ToList();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }

        if (clipboardHadText)
        {
            return [];
        }

        return !string.IsNullOrWhiteSpace(_recordedWaypointClipboardText)
               && _recordedWaypointClipboard is { Count: > 0 }
            ? _recordedWaypointClipboard.Select(CloneWaypoint).ToList()
            : [];
    }

    private void RemoveRecordedWaypoints(List<RecordedWaypointViewModel> selected)
    {
        var nextIndex = selected.Count == 0 ? -1 : RecordedWaypoints.IndexOf(selected.Last());
        foreach (var item in selected)
        {
            RecordedWaypoints.Remove(item);
        }

        ReindexRecordedWaypoints();
        if (RecordedWaypoints.Count == 0)
        {
            SelectedRecordedWaypoint = null;
        }
        else
        {
            SelectedRecordedWaypoint = RecordedWaypoints[Math.Clamp(nextIndex, 0, RecordedWaypoints.Count - 1)];
        }

        PublishRecorderPath();
    }

    private static Waypoint CloneWaypoint(Waypoint waypoint)
    {
        var json = JsonSerializer.Serialize(waypoint, PathRecorder.JsonOptions);
        return JsonSerializer.Deserialize<Waypoint>(json, PathRecorder.JsonOptions) ?? new Waypoint();
    }

    private bool CanEditRecorder()
    {
        if (IsRecorderMode)
        {
            return true;
        }

        RecordStatusText = "录制器：调试模式下不可修改路线信息";
        return false;
    }

    private static void TryActivateGenshinWindow()
    {
        var handle = SystemControl.FindGenshinImpactHandle();
        if (handle == 0)
        {
            handle = SystemControl.FindHandleByWindowName();
        }

        if (handle == 0)
        {
            return;
        }

        try
        {
            SystemControl.ActivateWindow(handle);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private bool TryGetRequiredRecordName(out string routeName)
    {
        routeName = (RecordFileName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(routeName))
        {
            ThemedMessageBox.Error("路线名称不能为空。保存路线前请先填写路线名称。", "路线名称必填", MessageBoxButton.OK, MessageBoxResult.OK);
            RecordStatusText = "录制器：路线名称不能为空";
            return false;
        }

        if (routeName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            ThemedMessageBox.Error("路线名称必须和文件名保持一致，因此不能包含 Windows 文件名非法字符。", "路线名称无效", MessageBoxButton.OK, MessageBoxResult.OK);
            RecordStatusText = "录制器：路线名称包含非法字符";
            return false;
        }

        RecordFileName = routeName;
        return true;
    }

    private string? ResolveRecordFilePath(bool forcePicker, string routeName)
    {
        if (!forcePicker && !string.IsNullOrWhiteSpace(_recordFilePath))
        {
            var directory = Path.GetDirectoryName(_recordFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                return Path.Combine(directory, $"{routeName}.json");
            }
        }

        var dialog = new SaveFileDialog
        {
            Title = "保存路线 JSON",
            Filter = "路线 JSON (*.json)|*.json|所有文件 (*.*)|*.*",
            FileName = $"{routeName}.json",
            InitialDirectory = Directory.Exists(MapPathingViewModel.PathJsonPath)
                ? MapPathingViewModel.PathJsonPath
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        var selectedDirectory = Path.GetDirectoryName(dialog.FileName);
        return string.IsNullOrWhiteSpace(selectedDirectory)
            ? null
            : Path.Combine(selectedDirectory, $"{routeName}.json");
    }

    private void LoadRecorderTask(PathingTask task)
    {
        var wasRestoring = _isRestoringRecorderHistory;
        _isRestoringRecorderHistory = true;
        _recordTaskTemplate = task;
        task.Info ??= new PathingTaskInfo();
        task.Positions ??= [];
        RecordedWaypoints.Clear();
        var taskName = !string.IsNullOrWhiteSpace(task.FullPath)
            ? Path.GetFileNameWithoutExtension(task.FullPath)
            : task.Info.Name;
        RecordFileName = string.IsNullOrWhiteSpace(taskName) ? "未命名路线" : taskName.Trim();
        task.Info.Name = RecordFileName;
        RecordDescription = task.Info.Description ?? string.Empty;
        var author = task.Info.Authors.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.Name));
        RecordAuthorName = author?.Name ?? task.Info.Author ?? string.Empty;
        RecordAuthorLinks = author?.Links ?? string.Empty;
        RecordVersion = task.Info.Version ?? "1.0";
        RecordTagsText = string.Join(", ", task.Info.Tags ?? []);
        RecordEnableMonsterLootSplit = task.Info.EnableMonsterLootSplit;
        RecordMapMatchMethod = string.IsNullOrWhiteSpace(task.Info.MapMatchMethod)
            ? TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod
            : task.Info.MapMatchMethod;
        foreach (var waypoint in task.Positions)
        {
            AddCombatScriptFromWaypointIfNeeded(waypoint);
            RecordedWaypoints.Add(CreateRecordedWaypointViewModel(waypoint));
        }

        ReindexRecordedWaypoints();
        SelectedRecordedWaypoint = RecordedWaypoints.FirstOrDefault();
        RecordStatusText = PathRecorder.Instance.IsRecording
            ? $"录制器：录制中 / {RecordedWaypoints.Count} 点"
            : $"录制器：编辑中 / {RecordedWaypoints.Count} 点";
        _isRestoringRecorderHistory = wasRestoring;
        RefreshDebugJson();
        PublishRecorderPath();
    }

    private RecordedWaypointViewModel CreateRecordedWaypointViewModel(Waypoint waypoint)
    {
        var viewModel = new RecordedWaypointViewModel(waypoint);
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(RecordedWaypointViewModel.IsLocked)
                or nameof(RecordedWaypointViewModel.IsSelected))
            {
                return;
            }

            if (ReferenceEquals(viewModel, SelectedRecordedWaypoint)
                && (e.PropertyName == nameof(RecordedWaypointViewModel.TypeDisplayText)
                    || e.PropertyName == nameof(RecordedWaypointViewModel.Index)))
            {
                OnPropertyChanged(nameof(SelectedWaypointEditorTitle));
            }

            PublishRecorderPath();
            if (viewModel.IsSelected
                && e.PropertyName is nameof(RecordedWaypointViewModel.X) or nameof(RecordedWaypointViewModel.Y))
            {
                PublishSelectedRecorderWaypoints();
            }
        };
        return viewModel;
    }

    private void ReindexRecordedWaypoints()
    {
        for (var i = 0; i < RecordedWaypoints.Count; i++)
        {
            RecordedWaypoints[i].Index = i + 1;
        }
    }

    private PathingTask BuildRecordedTask()
    {
        var safeName = (RecordFileName ?? string.Empty).Trim();
        var task = _recordTaskTemplate ?? new PathingTask();
        task.Info ??= new PathingTaskInfo();
        task.Info.Name = safeName;
        task.Info.Description = string.IsNullOrWhiteSpace(RecordDescription) ? null : RecordDescription.Trim();
        task.Info.Author = null;
        task.Info.Authors = string.IsNullOrWhiteSpace(RecordAuthorName)
            ? []
            :
            [
                new PathingTaskAuthor
                {
                    Name = RecordAuthorName.Trim(),
                    Links = RecordAuthorLinks.Trim()
                }
            ];
        task.Info.Version = string.IsNullOrWhiteSpace(RecordVersion) ? null : RecordVersion.Trim();
        task.Info.Tags = SplitTags(RecordTagsText);
        task.Info.EnableMonsterLootSplit = RecordEnableMonsterLootSplit;
        task.Info.Type = PathingTaskType.Collect.Code;
        task.Info.MapName = MapName;
        task.Info.MapMatchMethod = string.IsNullOrWhiteSpace(RecordMapMatchMethod)
            ? TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod
            : RecordMapMatchMethod;
        task.Info.BgiVersion = Global.Version;
        task.Positions = RecordedWaypoints.Select(i => i.ToWaypoint()).ToList();
        _recordTaskTemplate = task;
        return task;
    }

    public bool ConfirmJsonEditsBeforeLeavingRecorder(System.Windows.Window? owner, bool saveToFileOnApply = true)
    {
        if (!HasJsonEdits)
        {
            return true;
        }

        if (AutoSaveJsonEdits)
        {
            return TryApplyJsonEdits(saveToFile: saveToFileOnApply, allowFilePicker: saveToFileOnApply, owner);
        }

        var applyText = saveToFileOnApply ? "保存并应用" : "应用";
        var result = ThemedMessageBox.Show(
            $"JSON 已修改，是否{applyText}？\n选择“否”将丢弃本次 JSON 编辑，选择“取消”留在当前界面。",
            "保存 JSON 修改",
            System.Windows.MessageBoxButton.YesNoCancel,
            ThemedMessageBox.MessageBoxIcon.Warning,
            System.Windows.MessageBoxResult.Cancel,
            owner);

        if (result == System.Windows.MessageBoxResult.Cancel)
        {
            return false;
        }

        if (result == System.Windows.MessageBoxResult.No)
        {
            RefreshDebugJson();
            RecordStatusText = "录制器：已丢弃 JSON 修改";
            return true;
        }

        return TryApplyJsonEdits(saveToFile: saveToFileOnApply, allowFilePicker: saveToFileOnApply, owner);
    }

    private void PublishRecorderPath()
    {
        if (_isRestoringRecorderHistory || !IsRecorderMode)
        {
            return;
        }

        var task = BuildRecordedTask();
        UpdateSelectedRecordedRoute(task, _recordFilePath);
        PathRecorder.Instance.ReplaceTask(task, publish: false);
        RefreshDebugJson(task);
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(
            this,
            "UpdateRecorderPathing",
            new object(),
            task));
        PublishRecordedRouteList();
        SnapshotRecorderState(task);
    }

    private void UpdateSelectedRecordedRoute(PathingTask task, string? filePath)
    {
        if (_isSwitchingRecordedRoute)
        {
            return;
        }

        var route = SelectedRecordedRoute;
        if (route == null)
        {
            route = CreateRecordedRoute(task, filePath ?? string.Empty);
            RecordedRoutes.Add(route);
            SelectRecordedRouteWithoutReload(route);
            return;
        }

        route.ReplaceTask(ClonePathingTask(task), filePath ?? route.FilePath);
    }

    private void PublishRecordedRouteList()
    {
        if (!IsRecorderMode)
        {
            return;
        }

        var tasks = RecordedRoutes
            .Select(i => i.Task)
            .Where(task => string.IsNullOrWhiteSpace(task.Info?.MapName)
                           || string.Equals(task.Info.MapName, MapName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(
            this,
            "UpdateRecorderRouteList",
            new object(),
            tasks));
    }

    private void UpdateTaskSummary(PathingTask pathingTask)
    {
        var points = pathingTask.Positions ?? [];
        _currentRoutePoints = points.ToList();
        _routeTotalDistance = EstimateDistance(_currentRoutePoints);
        _routeCompletedDistance = 0;
        RouteProgressValue = 0;
        RouteProgressPointText = $"0 / {_currentRoutePoints.Count} 个点";
        RefreshRouteProgressText();
        HasCurrentPathing = points.Count > 0;
        TaskName = string.IsNullOrWhiteSpace(pathingTask.Info.Name)
            ? "路网规划结果"
            : pathingTask.Info.Name;
        TaskMetaText = string.IsNullOrWhiteSpace(pathingTask.Info.TypeDesc)
            ? $"{pathingTask.Info.MapName} / {pathingTask.FileName}"
            : $"{pathingTask.Info.TypeDesc} / {pathingTask.Info.MapName}";
        var nextWaypoint = points.FirstOrDefault(p => p.Type != WaypointType.Teleport.Code) ?? points.FirstOrDefault();
        UpdateNextWaypoint(nextWaypoint);

        RefreshDebugJson(pathingTask);
    }

    private void RefreshDebugJson(PathingTask? task = null)
    {
        try
        {
            _isRefreshingJson = true;
            DebugJsonText = (task ?? BuildRecordedTask()).ToJsonString();
            HasJsonEdits = false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            DebugJsonText = "{}";
        }
        finally
        {
            _isRefreshingJson = false;
        }
    }

    private bool TryApplyJsonEdits(bool saveToFile, bool allowFilePicker, System.Windows.Window? owner)
    {
        if (!HasJsonEdits)
        {
            return true;
        }

        if (!TryBuildTaskFromJsonEditor(out var task, out var errorMessage))
        {
            ThemedMessageBox.Error(errorMessage, "JSON 校验失败", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxResult.OK);
            RecordStatusText = "录制器：JSON 校验失败";
            return false;
        }

        MapName = string.IsNullOrWhiteSpace(task.Info.MapName) ? MapName : task.Info.MapName;
        LoadRecorderTask(task);
        PathRecorder.Instance.ReplaceTask(task, publish: false);

        if (saveToFile)
        {
            if (!TryGetRequiredRecordName(out var routeName))
            {
                return false;
            }

            task.Info.Name = routeName;
            var filePath = ResolveRecordFilePath(!allowFilePicker || string.IsNullOrWhiteSpace(_recordFilePath), routeName);
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            task.SaveToFile(filePath);
            _recordFilePath = filePath;
            task.FullPath = filePath;
            task.FileName = Path.GetFileName(filePath);
            RecordFilePathText = $"文件：{filePath}";
            RecordStatusText = $"录制器：JSON 已应用并保存 / {Path.GetFileName(filePath)}";
        }
        else
        {
            RecordStatusText = "录制器：JSON 已应用";
        }

        RefreshDebugJson(task);
        PublishRecorderPath();
        return true;
    }

    private static bool TryBuildTaskFromJsonEditor(string json, out PathingTask task, out string errorMessage)
    {
        try
        {
            task = PathingTask.BuildFromJson(json);
            task.Info ??= new PathingTaskInfo();
            task.Positions ??= [];
            if (string.IsNullOrWhiteSpace(task.Info.Name))
            {
                errorMessage = "路线名称 info.name 不能为空。";
                return false;
            }

            task.Info.Name = task.Info.Name.Trim();
            if (task.Info.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                errorMessage = "路线名称 info.name 必须和文件名保持一致，不能包含 Windows 文件名非法字符。";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(task.Info.BgiVersion) && Global.IsNewVersion(task.Info.BgiVersion))
            {
                errorMessage = $"路线要求 BetterGI 版本 {task.Info.BgiVersion}，当前版本为 {Global.Version}。";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            task = new PathingTask();
            errorMessage = $"JSON 无法解析为路线文件：\n{ex.Message}";
            return false;
        }
    }

    private bool TryBuildTaskFromJsonEditor(out PathingTask task, out string errorMessage)
    {
        return TryBuildTaskFromJsonEditor(DebugJsonText, out task, out errorMessage);
    }

    private void SnapshotRecorderState(PathingTask? task = null)
    {
        if (_isRestoringRecorderHistory)
        {
            return;
        }

        var snapshot = (task ?? BuildRecordedTask()).ToJsonString();
        if (_recorderHistoryIndex >= 0 && _recorderHistoryIndex < _recorderHistory.Count && _recorderHistory[_recorderHistoryIndex] == snapshot)
        {
            return;
        }

        if (_recorderHistoryIndex < _recorderHistory.Count - 1)
        {
            _recorderHistory.RemoveRange(_recorderHistoryIndex + 1, _recorderHistory.Count - _recorderHistoryIndex - 1);
        }

        _recorderHistory.Add(snapshot);
        if (_recorderHistory.Count > MaxRecorderHistory)
        {
            _recorderHistory.RemoveAt(0);
        }

        _recorderHistoryIndex = _recorderHistory.Count - 1;
    }

    private void RestoreRecorderHistory(int index)
    {
        if (index < 0 || index >= _recorderHistory.Count)
        {
            return;
        }

        if (!TryBuildTaskFromJsonEditor(_recorderHistory[index], out var task, out var errorMessage))
        {
            ThemedMessageBox.Error(errorMessage, "撤销失败", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxResult.OK);
            return;
        }

        _recorderHistoryIndex = index;
        _isRestoringRecorderHistory = true;
        try
        {
            LoadRecorderTask(task);
            PathRecorder.Instance.ReplaceTask(task, publish: false);
        }
        finally
        {
            _isRestoringRecorderHistory = false;
        }

        RefreshDebugJson(task);
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(
            this,
            "UpdateRecorderPathing",
            new object(),
            task));
        RecordStatusText = "录制器：已恢复历史状态";
    }

    private async Task RunRecordedTaskFromIndex(int startIndex)
    {
        var task = BuildRecordedTask();
        if (task.Positions.Count == 0)
        {
            RecordStatusText = "录制器：没有可运行的点位";
            return;
        }

        startIndex = Math.Clamp(startIndex, 0, task.Positions.Count - 1);
        task.Positions = task.Positions.Skip(startIndex).ToList();
        IsSidePanelVisible = true;
        IsRecorderMode = false;
        RecordStatusText = startIndex > 0
            ? $"录制器：从第 {startIndex + 1} 点开始运行"
            : "录制器：开始运行当前路线";
        await ScriptService.StartGameTask();
        SystemControl.ActivateWindow();
        await new TaskRunner().RunThreadAsync(async () =>
        {
            var pathExecutor = new PathExecutor(CancellationContext.Instance.Cts.Token)
            {
                PartyConfig = new PathingPartyConfig { AutoFightEnabled = false }
            };
            await pathExecutor.Pathing(task);
        });
        RecordStatusText = "录制器：运行结束";
    }

    private static List<List<Waypoint>> SplitWaypointsByTeleport(IEnumerable<Waypoint> waypoints)
    {
        var result = new List<List<Waypoint>>();
        var current = new List<Waypoint>();

        foreach (var waypoint in waypoints)
        {
            if (waypoint.Type == WaypointType.Teleport.Code && current.Count > 0)
            {
                result.Add(current);
                current = [];
            }

            current.Add(waypoint);
        }

        if (current.Count > 0)
        {
            result.Add(current);
        }

        return result;
    }

    private static PathingTask ClonePathingTask(PathingTask task)
    {
        var json = task.ToJsonString();
        return PathingTask.BuildFromJson(json);
    }

    private void RefreshLayoutProperties()
    {
        OnPropertyChanged(nameof(MapColumnWidth));
        OnPropertyChanged(nameof(SplitterColumnWidth));
        OnPropertyChanged(nameof(SideColumnWidth));
        OnPropertyChanged(nameof(SideColumnMinWidth));
        OnPropertyChanged(nameof(SidePanelVisibility));
        OnPropertyChanged(nameof(SidePanelHiddenVisibility));
        OnPropertyChanged(nameof(SidePanelToggleText));
        OnPropertyChanged(nameof(RecorderUiEditorVisibility));
        OnPropertyChanged(nameof(RecorderJsonEditorVisibility));
        OnPropertyChanged(nameof(RecordedWaypointListVisibility));
        OnPropertyChanged(nameof(RecordedWaypointEmptyVisibility));
    }

    private void UpdateNextWaypoint(Waypoint? waypoint)
    {
        NextWaypointText = waypoint == null
            ? "-"
            : $"{FormatCoordinate(GetWaypointGameX(waypoint))}, {FormatCoordinate(GetWaypointGameY(waypoint))}";
        NextWaypointActionText = FormatAction(waypoint?.Action);
        NextWaypointActionParamsText = string.IsNullOrWhiteSpace(waypoint?.ActionParams)
            ? "-"
            : waypoint.ActionParams!;
        MoveModeSummary = FormatMoveMode(waypoint?.MoveMode);
        UpdateRouteProgress(waypoint);
    }

    private void UpdateRouteProgress(Waypoint? waypoint)
    {
        if (waypoint == null || _currentRoutePoints.Count == 0 || _routeTotalDistance <= 0)
        {
            RouteProgressValue = 0;
            _routeCompletedDistance = 0;
            RouteProgressPointText = $"0 / {_currentRoutePoints.Count} 个点";
            RefreshRouteProgressText();
            return;
        }

        var currentIndex = FindRouteWaypointIndex(waypoint);
        if (currentIndex < 0)
        {
            return;
        }

        _routeCompletedDistance = EstimateDistance(_currentRoutePoints.Take(currentIndex).ToList());
        RouteProgressPointText = $"{currentIndex} / {_currentRoutePoints.Count} 个点";
        RouteProgressValue = Math.Clamp(_routeCompletedDistance / _routeTotalDistance * 100.0, 0, 100);
        RefreshRouteProgressText();
    }

    private int FindRouteWaypointIndex(Waypoint waypoint)
    {
        var x = GetWaypointGameX(waypoint);
        var y = GetWaypointGameY(waypoint);
        for (var i = 0; i < _currentRoutePoints.Count; i++)
        {
            if (Math.Abs(_currentRoutePoints[i].X - x) < 0.2 && Math.Abs(_currentRoutePoints[i].Y - y) < 0.2)
            {
                return i;
            }
        }

        return -1;
    }

    private void RefreshRouteProgressText()
    {
        if (_routeTotalDistance <= 0)
        {
            RouteProgressText = "0%";
            return;
        }

        RouteProgressText = $"{RouteProgressValue:F0}%";
    }

    private static double GetWaypointGameX(Waypoint waypoint)
    {
        return waypoint is WaypointForTrack waypointForTrack ? waypointForTrack.GameX : waypoint.X;
    }

    private static double GetWaypointGameY(Waypoint waypoint)
    {
        return waypoint is WaypointForTrack waypointForTrack ? waypointForTrack.GameY : waypoint.Y;
    }

    private string FormatCurrentPosition(Point2f imagePoint)
    {
        try
        {
            var matchingMethod = TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod;
            var gamePoint = MapManager.GetMap(MapName, matchingMethod).ConvertImageCoordinatesToGenshinMapCoordinates(imagePoint);
            if (gamePoint is { } point)
            {
                return $"当前位置：{FormatCoordinate(point.X)}, {FormatCoordinate(point.Y)}";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }

        return $"当前位置：{FormatCoordinate(imagePoint.X)}, {FormatCoordinate(imagePoint.Y)}";
    }

    internal static double RoundCoordinate(double value)
    {
        return Math.Round(value, CoordinateStorageDecimals, MidpointRounding.AwayFromZero);
    }

    internal static string FormatCoordinate(double value)
    {
        return RoundCoordinate(value).ToString($"F{CoordinateDisplayDecimals}", CultureInfo.InvariantCulture);
    }

    internal static bool TryParseCoordinate(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.Trim();
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed)
            && !double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
        {
            return false;
        }

        value = RoundCoordinate(parsed);
        return true;
    }

    private static string TrimDebugValue(string value, string prefix)
    {
        return value.StartsWith(prefix, StringComparison.Ordinal)
            ? value[prefix.Length..]
            : value;
    }

    private static string FormatRouteBool(bool value)
    {
        return value ? "是" : "否";
    }

    private static string FormatHotkeyText(string? hotkey)
    {
        return string.IsNullOrWhiteSpace(hotkey) ? "未绑定" : hotkey;
    }

    private static string ShortRouteId(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return "-";
        }

        return nodeId.Length <= 14 ? nodeId : nodeId[..14];
    }

    private static string FormatAction(string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return "-";
        }

        var message = ActionEnum.GetMsgByCode(action);
        return message;
    }

    private static string FormatMoveMode(string? moveMode)
    {
        if (string.IsNullOrWhiteSpace(moveMode))
        {
            return "-";
        }

        var message = MoveModeEnum.GetMsgByCode(moveMode);
        return message;
    }

    private static string GetMapDisplayName(string mapName)
    {
        try
        {
            return MapTypesExtensions.ParseFromName(mapName).GetDescription();
        }
        catch
        {
            return string.IsNullOrWhiteSpace(mapName) ? nameof(MapTypes.Teyvat) : mapName;
        }
    }

    private static double EstimateDistance(IReadOnlyList<Waypoint> points)
    {
        if (points.Count < 2)
        {
            return 0;
        }

        double distance = 0;
        for (var i = 1; i < points.Count; i++)
        {
            var dx = points[i].X - points[i - 1].X;
            var dy = points[i].Y - points[i - 1].Y;
            distance += Math.Sqrt(dx * dx + dy * dy);
        }

        return distance;
    }

    private static string[] SplitTags(string tagsText)
    {
        if (string.IsNullOrWhiteSpace(tagsText))
        {
            return [];
        }

        return tagsText
            .Replace('，', ',')
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct()
            .ToArray();
    }

    internal static string[] SplitExtTypes(string typesText)
    {
        if (string.IsNullOrWhiteSpace(typesText))
        {
            return ["unrecognized"];
        }

        return typesText
            .Replace('，', ',')
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }

    internal static string JoinExtTypes(IEnumerable<string>? types)
    {
        return string.Join(", ", types is null || !types.Any() ? ["unrecognized"] : types);
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="pathingTask"></param>
    /// <returns>2048级别的图像</returns>
    public Mat GenTaskMat(PathingTask pathingTask)
    {
        // 获取路径点位并转换为当前主要展示的地图点位 2048 级别
        var points = pathingTask.Positions;
        var mapPoints = points.Select(ConvertToMapPoint).ToList();
        var offsetRect = CalcRect(mapPoints);
        var offsetRectZoom = new Rect(offsetRect.X / _scale, offsetRect.Y / _scale, offsetRect.Width / _scale, offsetRect.Height / _scale);

        // 把 map 的局部转化为 实际展示图（提瓦特是256，其他的1024） 级别
        Mat taskMat = new Mat(_mapImage, offsetRectZoom);
        taskMat = ResizeHelper.Resize(taskMat, _scale);

        // 设置线条粗细
        int thickness = 2;

        // 绘制点位和连线
        for (int i = 0; i < mapPoints.Count - 1; i++)
        {
            var startPoint = mapPoints[i] - offsetRect.TopLeft;
            var endPoint = mapPoints[i + 1] - offsetRect.TopLeft;

            var lineColor = GetLineColor(points[i], points[i + 1]);
            var circleColor = GetCircleColor(points[i]);

            // 绘制点
            DrawCircle(taskMat, startPoint, circleColor, thickness);

            // 绘制线
            if (points[i + 1].Type != WaypointType.Teleport.Code)
            {
                DrawLine(taskMat, startPoint, endPoint, lineColor, 1);
            }
        }

        // 绘制最后一个点
        var lastPoint = mapPoints[^1] - offsetRect.TopLeft;
        var lastCircleColor = GetCircleColor(points[^1]);
        DrawCircle(taskMat, lastPoint, lastCircleColor, thickness);

        return taskMat;
    }

    private Rect CalcRect(List<Point> mapPoints)
    {
        // 计算其最大外接矩形
        _currentPathingRect = Cv2.BoundingRect(mapPoints);
        // 把矩形范围扩大一半
        _currentPathingRect.X -= 512;
        _currentPathingRect.Y -= 512;
        _currentPathingRect.Width += 1024;
        _currentPathingRect.Height += 1024;
        return _currentPathingRect;
    }

    private Point ConvertToMapPoint(Waypoint point)
    {
        var matchingMethod = TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod;
        var (x, y) = MapManager.GetMap(MapName, matchingMethod).ConvertGenshinMapCoordinatesToImageCoordinates(new Point2f((float)point.X, (float)point.Y));
        return new Point(x, y);
    }

    private Scalar GetLineColor(Waypoint startPoint, Waypoint endPoint)
    {
        if (endPoint.Type == WaypointType.Path.Code || startPoint.Type == WaypointType.Teleport.Code)
        {
            return new Scalar(255, 0, 0); // 蓝色
        }
        else if (endPoint.Type == WaypointType.Target.Code)
        {
            return new Scalar(0, 0, 255); // 红色
        }

        return new Scalar(0, 0, 255); // 默认红色
    }

    private Scalar GetCircleColor(Waypoint point)
    {
        if (point.Type == WaypointType.Path.Code || point.Type == WaypointType.Teleport.Code)
        {
            return new Scalar(255, 0, 0); // 蓝色
        }
        else if (point.Type == WaypointType.Target.Code)
        {
            return new Scalar(0, 0, 255); // 红色
        }

        return new Scalar(0, 0, 255); // 默认红色
    }

    private void DrawCircle(Mat mat, Point point, Scalar color, int thickness)
    {
        Cv2.Circle(mat, point, 3, color, thickness);
    }

    private void DrawLine(Mat mat, Point startPoint, Point endPoint, Scalar color, int thickness)
    {
        Cv2.Line(mat, startPoint, endPoint, color, thickness);
    }
}

public partial class WaypointFilterOption(string code, string displayName) : ObservableObject
{
    public string Code { get; } = code;

    public string DisplayName { get; } = displayName;

    [ObservableProperty]
    private bool _isSelected;
}

public sealed record MapEditorOption(string Code, string DisplayName);

public partial class RecordedRouteViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = "未命名路线";

    [ObservableProperty]
    private string _mapName = nameof(MapTypes.Teyvat);

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private int _pointCount;

    [ObservableProperty]
    private bool _isSelected;

    public PathingTask Task { get; private set; }

    public string PointCountText => $"{PointCount} 点";

    public string MapDisplayText => string.IsNullOrWhiteSpace(MapName) ? nameof(MapTypes.Teyvat) : MapName;

    public string FileDisplayText => string.IsNullOrWhiteSpace(FilePath) ? "未保存" : Path.GetFileName(FilePath);

    public RecordedRouteViewModel(PathingTask task, string? filePath)
    {
        Task = new PathingTask();
        ReplaceTask(task, filePath);
    }

    public void ReplaceTask(PathingTask task, string? filePath)
    {
        task.Info ??= new PathingTaskInfo();
        task.Positions ??= [];
        Task = PathingTask.BuildFromJson(task.ToJsonString());
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            Task.FullPath = filePath;
            Task.FileName = Path.GetFileName(filePath);
        }

        Name = string.IsNullOrWhiteSpace(Task.Info.Name) ? "未命名路线" : Task.Info.Name.Trim();
        MapName = string.IsNullOrWhiteSpace(Task.Info.MapName) ? nameof(MapTypes.Teyvat) : Task.Info.MapName;
        FilePath = filePath ?? Task.FullPath ?? string.Empty;
        PointCount = Task.Positions.Count;
    }

    partial void OnNameChanged(string value)
    {
        Task.Info ??= new PathingTaskInfo();
        Task.Info.Name = string.IsNullOrWhiteSpace(value) ? "未命名路线" : value.Trim();
    }

    partial void OnMapNameChanged(string value)
    {
        OnPropertyChanged(nameof(MapDisplayText));
    }

    partial void OnFilePathChanged(string value)
    {
        OnPropertyChanged(nameof(FileDisplayText));
    }

    partial void OnPointCountChanged(int value)
    {
        OnPropertyChanged(nameof(PointCountText));
    }
}

public partial class RouteFileBrowserItemViewModel : ObservableObject
{
    public string Name { get; private init; } = string.Empty;

    public string RelativePath { get; private init; } = string.Empty;

    public string FullPath { get; private init; } = string.Empty;

    public bool IsDirectory { get; private init; }

    public bool IsJsonFile => !IsDirectory && string.Equals(Path.GetExtension(Name), ".json", StringComparison.OrdinalIgnoreCase);

    public string KindText => IsDirectory ? "DIR" : IsJsonFile ? "JSON" : "FILE";

    public string ActionText => IsDirectory ? "进入" : "选择";

    [ObservableProperty]
    private bool _isSelected;

    public static RouteFileBrowserItemViewModel Create(string path, string root, bool isDirectory)
    {
        return new RouteFileBrowserItemViewModel
        {
            Name = Path.GetFileName(path),
            FullPath = path,
            RelativePath = Path.GetRelativePath(root, path),
            IsDirectory = isDirectory
        };
    }
}

public partial class CombatScriptOptionViewModel(string value, bool isDefault) : ObservableObject
{
    [ObservableProperty]
    private string _value = value;

    [ObservableProperty]
    private bool _isDefault = isDefault;

    public string DefaultText => IsDefault ? "默认" : string.Empty;

    partial void OnIsDefaultChanged(bool value)
    {
        OnPropertyChanged(nameof(DefaultText));
    }
}

public sealed class CombatScriptOptionStoreItem
{
    public string Value { get; set; } = string.Empty;

    public bool Def { get; set; }
}

public partial class RecordedWaypointViewModel : ObservableObject
{
    private readonly Dictionary<string, JsonElement>? _extensionData;

    private readonly Dictionary<string, JsonElement>? _extParamsExtensionData;

    private readonly Dictionary<string, JsonElement>? _misidentificationExtensionData;

    [ObservableProperty]
    private int _index;

    [ObservableProperty]
    private double _x;

    [ObservableProperty]
    private double _y;

    [ObservableProperty]
    private string _type = WaypointType.Path.Code;

    [ObservableProperty]
    private string _moveMode = MoveModeEnum.Walk.Code;

    [ObservableProperty]
    private string? _action;

    [ObservableProperty]
    private string? _actionParams;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _monsterTag = string.Empty;

    [ObservableProperty]
    private bool _enableMonsterLootSplit;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isLocked;

    [ObservableProperty]
    private string _misidentificationTypesText = "unrecognized";

    [ObservableProperty]
    private string _misidentificationHandlingMode = "previousDetectedPoint";

    [ObservableProperty]
    private int _misidentificationArrivalTime;

    [ObservableProperty]
    private string _actionParameterHint = "动作参数";

    public bool MisidentificationUnrecognized
    {
        get => HasMisidentificationType("unrecognized");
        set => SetMisidentificationType("unrecognized", value);
    }

    public bool MisidentificationPathTooFar
    {
        get => HasMisidentificationType("pathTooFar");
        set => SetMisidentificationType("pathTooFar", value);
    }

    public bool IsMisidentificationArrivalTimeEnabled => string.Equals(MisidentificationHandlingMode, "scheduledArrival", StringComparison.OrdinalIgnoreCase);

    public string CoordinateText => $"{MapViewerViewModel.FormatCoordinate(X)}, {MapViewerViewModel.FormatCoordinate(Y)}";

    public string CoordinateXText => MapViewerViewModel.FormatCoordinate(X);

    public string CoordinateYText => MapViewerViewModel.FormatCoordinate(Y);

    public string XText
    {
        get => MapViewerViewModel.FormatCoordinate(X);
        set
        {
            if (MapViewerViewModel.TryParseCoordinate(value, out var parsed))
            {
                X = parsed;
            }

            OnPropertyChanged();
        }
    }

    public string YText
    {
        get => MapViewerViewModel.FormatCoordinate(Y);
        set
        {
            if (MapViewerViewModel.TryParseCoordinate(value, out var parsed))
            {
                Y = parsed;
            }

            OnPropertyChanged();
        }
    }

    public string TypeDisplayText => WaypointType.GetMsgByCode(Type);

    public string MoveModeDisplayText => MoveModeEnum.GetMsgByCode(MoveMode);

    public string ActionDisplayText => string.IsNullOrWhiteSpace(Action)
        ? "无动作"
        : ActionEnum.GetMsgByCode(Action);

    public System.Windows.Visibility CombatScriptParameterVisibility =>
        string.Equals(Action, ActionEnum.CombatScript.Code, StringComparison.OrdinalIgnoreCase)
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;

    public System.Windows.Visibility GeneralActionParameterVisibility =>
        string.Equals(Action, ActionEnum.CombatScript.Code, StringComparison.OrdinalIgnoreCase)
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;

    public RecordedWaypointViewModel(Waypoint waypoint)
    {
        _extensionData = waypoint.ExtensionData;
        _extParamsExtensionData = waypoint.PointExtParams?.ExtensionData;
        _misidentificationExtensionData = waypoint.PointExtParams?.Misidentification?.ExtensionData;
        X = MapViewerViewModel.RoundCoordinate(waypoint.X);
        Y = MapViewerViewModel.RoundCoordinate(waypoint.Y);
        Type = string.IsNullOrWhiteSpace(waypoint.Type) ? WaypointType.Path.Code : waypoint.Type;
        MoveMode = string.IsNullOrWhiteSpace(waypoint.MoveMode) ? MoveModeEnum.Walk.Code : waypoint.MoveMode;
        Action = waypoint.Action ?? string.Empty;
        ActionParams = waypoint.ActionParams ?? string.Empty;
        Description = waypoint.PointExtParams?.Description ?? string.Empty;
        MonsterTag = waypoint.PointExtParams?.MonsterTag ?? string.Empty;
        EnableMonsterLootSplit = waypoint.PointExtParams?.EnableMonsterLootSplit ?? false;
        MisidentificationTypesText = MapViewerViewModel.JoinExtTypes(waypoint.PointExtParams?.Misidentification?.Type);
        MisidentificationHandlingMode = waypoint.PointExtParams?.Misidentification?.HandlingMode ?? "previousDetectedPoint";
        MisidentificationArrivalTime = waypoint.PointExtParams?.Misidentification?.ArrivalTime ?? 0;
        RefreshActionParameterHint();
    }

    partial void OnXChanged(double value)
    {
        OnPropertyChanged(nameof(CoordinateText));
        OnPropertyChanged(nameof(CoordinateXText));
        OnPropertyChanged(nameof(XText));
    }

    partial void OnYChanged(double value)
    {
        OnPropertyChanged(nameof(CoordinateText));
        OnPropertyChanged(nameof(CoordinateYText));
        OnPropertyChanged(nameof(YText));
    }

    partial void OnTypeChanged(string value)
    {
        OnPropertyChanged(nameof(TypeDisplayText));
    }

    partial void OnMoveModeChanged(string value)
    {
        OnPropertyChanged(nameof(MoveModeDisplayText));
    }

    partial void OnActionChanged(string? value)
    {
        OnPropertyChanged(nameof(ActionDisplayText));
        OnPropertyChanged(nameof(CombatScriptParameterVisibility));
        OnPropertyChanged(nameof(GeneralActionParameterVisibility));
        RefreshActionParameterHint();
        if (value == ActionEnum.CombatScript.Code && string.IsNullOrWhiteSpace(ActionParams))
        {
            ActionParams = "";
        }
        else if (value != ActionEnum.CombatScript.Code)
        {
            ActionParams = "";
        }
    }

    partial void OnMisidentificationTypesTextChanged(string value)
    {
        OnPropertyChanged(nameof(MisidentificationUnrecognized));
        OnPropertyChanged(nameof(MisidentificationPathTooFar));
    }

    partial void OnMisidentificationHandlingModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsMisidentificationArrivalTimeEnabled));
    }

    private bool HasMisidentificationType(string type)
    {
        return GetMisidentificationTypes()
            .Any(i => string.Equals(i, type, StringComparison.OrdinalIgnoreCase));
    }

    private void SetMisidentificationType(string type, bool value)
    {
        var types = GetMisidentificationTypes()
            .Where(i => !string.Equals(i, type, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (value)
        {
            types.Add(type);
        }

        MisidentificationTypesText = MapViewerViewModel.JoinExtTypes(types.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private string[] GetMisidentificationTypes()
    {
        if (string.IsNullOrWhiteSpace(MisidentificationTypesText))
        {
            return [];
        }

        return MapViewerViewModel.SplitExtTypes(MisidentificationTypesText);
    }

    private void RefreshActionParameterHint()
    {
        ActionParameterHint = Action switch
        {
            "log_output" => "录入需要输出的日志",
            "stop_flying" => "录入下落攻击等待时间(毫秒)",
            "set_time" => "录入需要设置的时间 HH:MM",
            "linnea_mining" => "射箭次数,旋转寻矿次数 默认 1,5",
            "combat_script" => "录入战斗策略脚本",
            _ => "动作参数"
        };
    }

    public Waypoint ToWaypoint()
    {
        return new Waypoint
        {
            ExtensionData = _extensionData,
            X = MapViewerViewModel.RoundCoordinate(X),
            Y = MapViewerViewModel.RoundCoordinate(Y),
            Type = string.IsNullOrWhiteSpace(Type) ? WaypointType.Path.Code : Type,
            MoveMode = string.IsNullOrWhiteSpace(MoveMode) ? MoveModeEnum.Walk.Code : MoveMode,
            Action = string.IsNullOrWhiteSpace(Action) ? null : Action,
            ActionParams = string.IsNullOrWhiteSpace(ActionParams) ? null : ActionParams,
            PointExtParams = new Waypoint.ExtParams
            {
                ExtensionData = _extParamsExtensionData,
                Description = Description ?? string.Empty,
                MonsterTag = MonsterTag ?? string.Empty,
                EnableMonsterLootSplit = EnableMonsterLootSplit,
                Misidentification = new Waypoint.Misidentification
                {
                    ExtensionData = _misidentificationExtensionData,
                    Type = MapViewerViewModel.SplitExtTypes(MisidentificationTypesText).ToList(),
                    HandlingMode = string.IsNullOrWhiteSpace(MisidentificationHandlingMode)
                        ? "previousDetectedPoint"
                        : MisidentificationHandlingMode,
                    ArrivalTime = MisidentificationArrivalTime
                }
            }
        };
    }
}
