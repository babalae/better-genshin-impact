using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
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
using System.Text.Json.Serialization;
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
    private const int OperationLogLimit = 100;
    private const int ClipboardRetryCount = 5;
    private const int ClipboardRetryDelayMilliseconds = 50;
    private const int ClipboardCannotOpenHResult = unchecked((int)0x800401D0);
    private const string NoRareActionCodesMarker = "__none";
    private static readonly string[] ElementalActionCodes =
    [
        ActionEnum.HydroCollect.Code,
        ActionEnum.ElectroCollect.Code,
        ActionEnum.AnemoCollect.Code,
        ActionEnum.PyroCollect.Code
    ];
    private static readonly string[] DefaultRareActionCodes =
    [
        ActionEnum.ForceTp.Code,
        ActionEnum.LogOutput.Code,
        ActionEnum.ExitAndRelogin.Code,
        ActionEnum.EnterAndExitWonderland.Code,
        ActionEnum.SetTime.Code
    ];
    private readonly string _routeSaveDir = Global.Absolute(Path.Combine("User", "AutoPathing", "Routes"));
    private readonly RouteNavigationGraphProvider _graphProvider;
    private readonly RouteNavigationPlanner _routeNavigationPlanner;
    private readonly HashSet<string> _rareActionCodes = new(StringComparer.OrdinalIgnoreCase);

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
    private bool _isMapMiniFollowWindowVisible = TaskContext.Instance().Config.DevConfig.MapMiniFollowVisible;

    [ObservableProperty]
    private bool _isMapMiniFollowTopmost = TaskContext.Instance().Config.DevConfig.MapMiniFollowTopmost;

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
    private bool _isRecordedRoutePropertiesOpen;

    [ObservableProperty]
    private bool _isRecordedRouteListExpanded;

    [ObservableProperty]
    private bool _isOperationLogOpen;

    [ObservableProperty]
    private bool _isActionUsageEditorOpen;

    [ObservableProperty]
    private bool _isRecorderPreferencesOpen;

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
    private double _recorderNudgeStep = 1.0;

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
    private ActionUsageEditorItemViewModel? _selectedCommonActionUsageOption;

    [ObservableProperty]
    private ActionUsageEditorItemViewModel? _selectedRareActionUsageOption;

    [ObservableProperty]
    private CommonRecordAuthorViewModel? _selectedCommonRecordAuthor;

    [ObservableProperty]
    private string _newCommonAuthorName = string.Empty;

    [ObservableProperty]
    private string _newCommonAuthorLinks = string.Empty;

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
    private List<PathingTask>? _recordedRouteClipboard;
    private string? _recordedRouteClipboardText;

    public ObservableCollection<RecordedWaypointViewModel> RecordedWaypoints { get; } = [];

    public ObservableCollection<RecordedRouteViewModel> RecordedRoutes { get; } = [];

    public ObservableCollection<RouteFileBrowserItemViewModel> RouteBrowserItems { get; } = [];

    public ObservableCollection<CombatScriptOptionViewModel> CombatScripts { get; } = [];

    public ObservableCollection<RecordAuthorViewModel> RecordAuthors { get; } = [];

    public ObservableCollection<CommonRecordAuthorViewModel> CommonRecordAuthors { get; } = [];

    public ObservableCollection<RoutePlanEdgeRow> PlannedEdges { get; } = [];

    public ObservableCollection<RouteNearbyNodeRow> NearbyNodes { get; } = [];

    public ObservableCollection<RouteNearbyEdgeRow> NearbyEdges { get; } = [];

    public ObservableCollection<RouteHealthRow> HealthRows { get; } = [];

    public ICollectionView RecordedWaypointView { get; }

    public ObservableCollection<OperationLogEntryViewModel> OperationLogs { get; } = [];

    public ObservableCollection<ActionMenuGroupViewModel> ActionMenuGroups { get; } = [];

    public ObservableCollection<ActionUsageEditorItemViewModel> CommonActionUsageOptions { get; } = [];

    public ObservableCollection<ActionUsageEditorItemViewModel> RareActionUsageOptions { get; } = [];

    public ObservableCollection<MapEditorOption> WaypointTypeOptions { get; } = new(
        WaypointType.Values.Select(i => new MapEditorOption(i.Code, i.Msg)));

    public ObservableCollection<MapEditorOption> MoveModeOptions { get; } = new(
        MoveModeEnum.Values.Select(i => new MapEditorOption(i.Code, i.Msg)));

    public ObservableCollection<MapEditorOption> TargetMoveModeOptions { get; } = new(
        new[] { new MapEditorOption(string.Empty, "沿用最后一段") }.Concat(
            MoveModeEnum.Values.Select(i => new MapEditorOption(i.Code, i.Msg))));

    public ObservableCollection<MapEditorOption> ActionOptions { get; } = [];

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

    public System.Windows.Visibility OperationLogListVisibility => OperationLogs.Count > 0
        ? System.Windows.Visibility.Visible
        : System.Windows.Visibility.Collapsed;

    public System.Windows.Visibility OperationLogEmptyVisibility => OperationLogs.Count == 0
        ? System.Windows.Visibility.Visible
        : System.Windows.Visibility.Collapsed;

    public string OperationLogCountText => $"{OperationLogs.Count} / {OperationLogLimit}";

    public bool HasSelectedRecorderEdge => _selectedRecorderEdgeInsertIndex >= 0 && _selectedRecorderEdgePoint != null;

    public System.Windows.Visibility MapWaypointContextMenuVisibility => SelectedRecordedWaypoint == null
        ? System.Windows.Visibility.Collapsed
        : System.Windows.Visibility.Visible;

    public System.Windows.Visibility MapEdgeContextMenuVisibility => HasSelectedRecorderEdge
        ? System.Windows.Visibility.Visible
        : System.Windows.Visibility.Collapsed;

    public System.Windows.Visibility RecordedRouteListExpandedVisibility => IsRecordedRouteListExpanded
        ? System.Windows.Visibility.Visible
        : System.Windows.Visibility.Collapsed;

    public System.Windows.Visibility RecordedRouteListCollapsedVisibility => IsRecordedRouteListExpanded
        ? System.Windows.Visibility.Collapsed
        : System.Windows.Visibility.Visible;

    public System.Windows.Visibility RecordAuthorListVisibility => RecordAuthors.Count > 0
        ? System.Windows.Visibility.Visible
        : System.Windows.Visibility.Collapsed;

    public System.Windows.Visibility RecordAuthorEmptyVisibility => RecordAuthors.Count == 0
        ? System.Windows.Visibility.Visible
        : System.Windows.Visibility.Collapsed;

    public string RecordAuthorSummaryText => RecordAuthors.Count == 0
        ? "未设置作者"
        : $"{RecordAuthors.Count} 位作者";

    public string CommonRecordAuthorSummaryText => CommonRecordAuthors.Count == 0
        ? "未加载常见作者"
        : $"{CommonRecordAuthors.Count} 位带链接作者";

    public string RecordedRouteListToggleText => IsRecordedRouteListExpanded ? "收起" : "展开";

    public string SidePanelToggleText => IsSidePanelVisible ? "隐藏信息" : "显示信息";

    public bool HasRecordedWaypoints => RecordedWaypoints.Count > 0;

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

    public int SelectedRecordedRouteCount => RecordedRoutes.Count(i => i.IsSelected);

    public System.Windows.Visibility SingleRecordedRouteSelectionVisibility => SelectedRecordedRouteCount == 1
        ? System.Windows.Visibility.Visible
        : System.Windows.Visibility.Collapsed;

    public System.Windows.Visibility MultipleRecordedRouteSelectionVisibility => SelectedRecordedRouteCount > 1
        ? System.Windows.Visibility.Visible
        : System.Windows.Visibility.Collapsed;

    public System.Windows.Visibility RecordedRouteSelectionActionSeparatorVisibility => SelectedRecordedRouteCount > 0
        ? System.Windows.Visibility.Visible
        : System.Windows.Visibility.Collapsed;

    public string RouteListEmptyText => RecordedRoutes.Count == 0
        ? "暂无路线"
        : string.Empty;

    public RecordedRouteViewModel? CollapsedRecordedRoute => SelectedRecordedRoute ?? RecordedRoutes.LastOrDefault();

    public IReadOnlyList<RecordedRouteViewModel> CollapsedRecordedRouteItems => CollapsedRecordedRoute == null
        ? Array.Empty<RecordedRouteViewModel>()
        : new[] { CollapsedRecordedRoute };

    public System.Windows.Visibility CollapsedRecordedRouteVisibility => CollapsedRecordedRoute == null
        ? System.Windows.Visibility.Collapsed
        : System.Windows.Visibility.Visible;

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

    public string MapMiniFollowTitleText => $"{(IsRecorderMode ? "录制" : "调试")} / {MapDisplayName}";

    public string MapMiniFollowHotkeyText => FormatHotkeyText(TaskContext.Instance().Config.HotKeyConfig.MapMiniFollowWindowHotkey);

    public string FollowZoomText => $"{FollowZoom:F1}x";

    public string MapClickEditModeToolTip => $"{MapClickEditModeText}（Tab 切换）";

    public string RecorderNudgeStepText
    {
        get => RecorderNudgeStep.ToString("0.##", CultureInfo.InvariantCulture);
        set
        {
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                || double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
            {
                RecorderNudgeStep = parsed;
            }
        }
    }

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

    public string ModeActionText => IsRecorderMode ? RecordingToggleText : "开始追踪";

    public string ModeActionToolTip => IsRecorderMode
        ? RecordingToggleToolTip
        : "运行当前路线";

    public string TargetActionDisplayText => string.IsNullOrWhiteSpace(TargetAction)
        ? "无动作"
        : ActionEnum.GetMsgByCode(TargetAction);

    public string TargetActionParameterHint => GetActionParameterHint(TargetAction);

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

    private string? _lastPositionMapName;

    private Point2f? _selectedTargetPoint;

    private Point2f? _selectedRecorderEdgePoint;

    private int _selectedRecorderEdgeInsertIndex = -1;

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

    private bool _isUpdatingRecordAuthors;

    private bool _isUpdatingWaypointFromMap;

    private bool _isRefreshingRouteDiagnosticsLite;

    private bool _isSynchronizingMapName;

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
        _mapBitmap = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null);
        LoadCommonRecordAuthors();
        LoadRareActionCodes();
        RebuildActionOptions();
        RecordedWaypointView = CollectionViewSource.GetDefaultView(RecordedWaypoints);
        RecordedWaypointView.Filter = FilterRecordedWaypoint;
        RecordedWaypoints.CollectionChanged += OnRecordedWaypointsCollectionChanged;
        RecordedRoutes.CollectionChanged += OnRecordedRoutesCollectionChanged;
        RouteBrowserItems.CollectionChanged += OnRouteBrowserItemsCollectionChanged;
        CombatScripts.CollectionChanged += OnCombatScriptsCollectionChanged;
        RecordAuthors.CollectionChanged += OnRecordAuthorsCollectionChanged;
        SetRecordAuthors(CreateDefaultRecordAuthors());
        SelectedWaypointFilterDimension = WaypointFilterDimensions.FirstOrDefault();
        LoadCombatScripts();
        IsPathRecorderRecording = PathRecorder.Instance.IsRecording;
        WeakReferenceMessenger.Default.Register<PropertyChangedMessage<object>>(this, (_, msg) =>
        {
            if (msg.PropertyName == "SendCurrentPosition")
            {
                if (!TryGetTrackedPosition(msg.NewValue, out var point, out var positionMapName))
                {
                    return;
                }

                if (point.X == 0 && point.Y == 0)
                {
                    return;
                }

                if (!ShouldRefreshMapBitmap())
                {
                    UIDispatcherHelper.BeginInvoke(() =>
                    {
                        SynchronizeTrackingMap(positionMapName);
                        _lastPositionMapName = ResolvePositionMapName(positionMapName);
                        if (FollowRoutePlanningCurrentPosition)
                        {
                            UpdateRoutePlanningCurrentPosition(point);
                        }
                    });

                    return;
                }

                UIDispatcherHelper.BeginInvoke(() =>
                {
                    SynchronizeTrackingMap(positionMapName);
                    _lastPositionMapName = ResolvePositionMapName(positionMapName);
                    _lastPosition = point;
                    CurrentPositionText = FormatCurrentPosition(point, _lastPositionMapName);
                    LastRefreshText = $"刷新：{DateTime.Now:HH:mm:ss}";
                    UpdateRoutePlanningCurrentPosition(point);
                });
            }
            else if (msg.PropertyName == "UpdateCurrentPathing")
            {
                Debug.WriteLine("更新当前追踪的路径图像");
                var pathingTask = (PathingTask)msg.NewValue;
                UIDispatcherHelper.BeginInvoke(() => UpdateTaskSummary(pathingTask));
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
            else if (msg.PropertyName == "ToggleMapMiniFollowWindow")
            {
                UIDispatcherHelper.BeginInvoke(ToggleMapMiniFollowWindow);
            }
            else if (msg.PropertyName == "RequestMapDisplaySnapshot")
            {
                UIDispatcherHelper.BeginInvoke(ReplayMapDisplaySnapshot);
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
                SelectRecordedWaypointByIndex(waypointIndex);
            }
            else if (msg.PropertyName == "MoveRecorderWaypointPosition" && msg.NewValue is RecorderWaypointMapUpdate update)
            {
                UIDispatcherHelper.BeginInvoke(() => MoveRecordedWaypointFromMap(update.Index, update.Point));
            }
            else if (msg.PropertyName == "SelectRecorderRouteEdge" && msg.NewValue is RecorderRouteEdgeSelection edgeSelection)
            {
                SelectRecorderRouteEdge(edgeSelection);
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

    private void OnRecordAuthorsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (RecordAuthorViewModel item in e.OldItems)
            {
                item.PropertyChanged -= OnRecordAuthorPropertyChanged;
            }
        }

        if (e.NewItems != null)
        {
            foreach (RecordAuthorViewModel item in e.NewItems)
            {
                item.PropertyChanged += OnRecordAuthorPropertyChanged;
            }
        }

        RefreshRecordAuthorProperties();
        if (!_isUpdatingRecordAuthors)
        {
            PublishRecorderPath();
        }
    }

    private void OnRecordAuthorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshRecordAuthorProperties();
        if (!_isUpdatingRecordAuthors)
        {
            PublishRecorderPath();
        }
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
        OnPropertyChanged(nameof(CollapsedRecordedRoute));
        OnPropertyChanged(nameof(CollapsedRecordedRouteItems));
        OnPropertyChanged(nameof(CollapsedRecordedRouteVisibility));
        RefreshRecordedRouteSelectionProperties();
    }

    private void RefreshRecordedRouteSelectionProperties()
    {
        OnPropertyChanged(nameof(SelectedRecordedRouteCount));
        OnPropertyChanged(nameof(SingleRecordedRouteSelectionVisibility));
        OnPropertyChanged(nameof(MultipleRecordedRouteSelectionVisibility));
        OnPropertyChanged(nameof(RecordedRouteSelectionActionSeparatorVisibility));
    }

    private void RefreshRecordAuthorProperties()
    {
        OnPropertyChanged(nameof(RecordAuthorListVisibility));
        OnPropertyChanged(nameof(RecordAuthorEmptyVisibility));
        OnPropertyChanged(nameof(RecordAuthorSummaryText));
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
        OnPropertyChanged(nameof(ModeActionText));
        OnPropertyChanged(nameof(ModeActionToolTip));
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
        OnPropertyChanged(nameof(ModeActionText));
        OnPropertyChanged(nameof(ModeActionToolTip));
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
        OnPropertyChanged(nameof(MapClickEditModeToolTip));
    }

    partial void OnRecorderNudgeStepChanged(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
        {
            RecorderNudgeStep = 1.0;
            return;
        }

        if (value > 1000)
        {
            RecorderNudgeStep = 1000;
            return;
        }

        OnPropertyChanged(nameof(RecorderNudgeStepText));
    }

    partial void OnSelectedRecordedWaypointChanged(RecordedWaypointViewModel? value)
    {
        OnPropertyChanged(nameof(SelectedWaypointEditorVisibility));
        OnPropertyChanged(nameof(SelectedWaypointEmptyVisibility));
        OnPropertyChanged(nameof(SelectedWaypointEditorTitle));
        OnPropertyChanged(nameof(MapWaypointContextMenuVisibility));

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
        OnPropertyChanged(nameof(CollapsedRecordedRoute));
        OnPropertyChanged(nameof(CollapsedRecordedRouteItems));
        OnPropertyChanged(nameof(CollapsedRecordedRouteVisibility));
        if (_isSwitchingRecordedRoute)
        {
            return;
        }

        LoadRecordedRoute(value);
    }

    partial void OnIsRecordedRouteListExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(RecordedRouteListExpandedVisibility));
        OnPropertyChanged(nameof(RecordedRouteListCollapsedVisibility));
        OnPropertyChanged(nameof(RecordedRouteListToggleText));
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
        if (selected.Count > 0)
        {
            ClearSelectedRecorderEdgeState(notifyMap: true);
        }

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

    public void SyncRecordedRouteSelection(IList selectedItems)
    {
        var selected = selectedItems.OfType<RecordedRouteViewModel>()
            .Where(i => RecordedRoutes.Contains(i))
            .OrderBy(i => RecordedRoutes.IndexOf(i))
            .ToList();

        var selectedSet = selected.ToHashSet();
        foreach (var route in RecordedRoutes)
        {
            var isSelected = selectedSet.Contains(route);
            if (route.IsSelected != isSelected)
            {
                route.IsSelected = isSelected;
            }
        }

        RefreshRecordedRouteSelectionProperties();
        if (selected.Count == 0)
        {
            OnPropertyChanged(nameof(CollapsedRecordedRoute));
            OnPropertyChanged(nameof(CollapsedRecordedRouteItems));
            OnPropertyChanged(nameof(CollapsedRecordedRouteVisibility));
            return;
        }

        var lastRoute = selected.Last();
        if (!ReferenceEquals(SelectedRecordedRoute, lastRoute))
        {
            SelectedRecordedRoute = lastRoute;
        }
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

    [RelayCommand]
    private void FitSelectedRecordedWaypoints()
    {
        if (!CanEditRecorder())
        {
            return;
        }

        var selected = GetSelectedRecordedWaypointsFromState();
        if (selected.Count == 0)
        {
            RecordStatusText = "录制器：请先选择路线点";
            return;
        }

        var points = selected
            .Select(i => new Point2f((float)i.X, (float)i.Y))
            .ToList();
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(
            this,
            "FitSelectedRecorderWaypointPositions",
            new object(),
            points));
        RecordStatusText = selected.Count == 1
            ? $"录制器：已聚焦第 {selected[0].Index} 点"
            : $"录制器：已聚焦 {selected.Count} 个点";
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

    partial void OnIsMapMiniFollowWindowVisibleChanged(bool value)
    {
        TaskContext.Instance().Config.DevConfig.MapMiniFollowVisible = value;
    }

    partial void OnIsMapMiniFollowTopmostChanged(bool value)
    {
        TaskContext.Instance().Config.DevConfig.MapMiniFollowTopmost = value;
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
        AddOperationLog(value);
    }

    partial void OnIsPathRecorderRecordingChanged(bool value)
    {
        if (value)
        {
            IsFollowingCurrent = true;
        }

        OnPropertyChanged(nameof(RecordingToggleText));
        OnPropertyChanged(nameof(ModeActionText));
        OnPropertyChanged(nameof(MapStatusText));
    }

    partial void OnTargetActionChanged(string value)
    {
        OnPropertyChanged(nameof(TargetActionDisplayText));
        OnPropertyChanged(nameof(TargetActionParameterHint));
    }

    partial void OnRecordDescriptionChanged(string value)
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
        OnPropertyChanged(nameof(MapMiniFollowTitleText));
        if (!string.IsNullOrWhiteSpace(value))
        {
            TaskContext.Instance().Config.DevConfig.RecordMapName = value;
        }

        SelectedTargetText = "目标点：点击地图选择";
        _selectedTargetPoint = null;
        if (!_isSynchronizingMapName && !_isSwitchingRecordedRoute)
        {
            ClearPathing();
        }

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

    private void SynchronizeTrackingMap(string? mapName)
    {
        var normalizedMapName = NormalizeKnownMapName(mapName);
        if (string.IsNullOrWhiteSpace(normalizedMapName) ||
            string.Equals(normalizedMapName, MapName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _isSynchronizingMapName = true;
        try
        {
            MapName = normalizedMapName;
        }
        finally
        {
            _isSynchronizingMapName = false;
        }
    }

    private string ResolvePositionMapName(string? mapName)
    {
        return NormalizeKnownMapName(mapName) ?? MapName;
    }

    private static string? NormalizeKnownMapName(string? mapName)
    {
        if (string.IsNullOrWhiteSpace(mapName))
        {
            return null;
        }

        return Enum.TryParse<MapTypes>(mapName, true, out var mapType)
            ? mapType.ToString()
            : null;
    }

    private static bool TryGetTrackedPosition(object? value, out Point2f point, out string? mapName)
    {
        switch (value)
        {
            case TrackedMapPosition tracked:
                point = tracked.ImagePosition;
                mapName = tracked.MapName;
                return true;
            case Point2f legacyPoint:
                point = legacyPoint;
                mapName = null;
                return true;
            default:
                point = default;
                mapName = null;
                return false;
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
    private void ToggleMapClickEditMode()
    {
        UpdateSelectedPointOnMapClick = !UpdateSelectedPointOnMapClick;
        RecordStatusText = UpdateSelectedPointOnMapClick
            ? "录制器：地图点击将更新选中点"
            : "录制器：地图点击将追加点";
    }

    [RelayCommand]
    private void OpenRecordedRouteProperties(RecordedRouteViewModel? route)
    {
        route = ResolveRecordedRoute(route);
        if (route == null)
        {
            RecordStatusText = "录制器：请先选择路线";
            return;
        }

        if (!ReferenceEquals(SelectedRecordedRoute, route))
        {
            SelectedRecordedRoute = route;
        }

        IsRecordedRoutePropertiesOpen = true;
    }

    [RelayCommand]
    private void CloseRecordedRouteProperties()
    {
        IsRecordedRoutePropertiesOpen = false;
    }

    [RelayCommand]
    private void ToggleRecordedRouteListExpanded()
    {
        IsRecordedRouteListExpanded = !IsRecordedRouteListExpanded;
    }

    [RelayCommand]
    private void SelectAllRecordedRoutes()
    {
        if (!CanEditRecorder())
        {
            return;
        }

        IsRecordedRouteListExpanded = true;
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(
            this,
            "SelectAllRecordedRouteRows",
            new object(),
            new object()));
    }

    [RelayCommand]
    private void RenameRecordedRoute(RecordedRouteViewModel? route)
    {
        route = ResolveRecordedRoute(route);
        if (route == null)
        {
            RecordStatusText = "录制器：请先选择路线";
            return;
        }

        if (!ReferenceEquals(SelectedRecordedRoute, route))
        {
            SelectedRecordedRoute = route;
        }

        foreach (var item in RecordedRoutes)
        {
            if (!ReferenceEquals(item, route))
            {
                item.EndRename();
            }
        }

        route.BeginRename();
        RecordStatusText = $"录制器：重命名路线 / {route.Name}";
    }

    public void CommitRecordedRouteRename(RecordedRouteViewModel? route, bool cancel)
    {
        route = ResolveRecordedRoute(route);
        if (route == null)
        {
            return;
        }

        if (!route.IsRenaming)
        {
            return;
        }

        if (cancel)
        {
            route.EditingName = route.Name;
            route.EndRename();
            return;
        }

        var routeName = (route.EditingName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(routeName))
        {
            route.EditingName = route.Name;
            route.EndRename();
            RecordStatusText = "录制器：路线名称不能为空，已取消重命名";
            return;
        }

        if (routeName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            route.EditingName = route.Name;
            route.EndRename();
            RecordStatusText = "录制器：路线名称包含非法字符，已取消重命名";
            return;
        }

        route.Name = routeName;
        route.EndRename();
        if (ReferenceEquals(route, SelectedRecordedRoute))
        {
            RecordFileName = routeName;
            _recordTaskTemplate.Info ??= new PathingTaskInfo();
            _recordTaskTemplate.Info.Name = routeName;
            PublishRecorderPath();
        }

        RecordStatusText = $"录制器：已重命名路线 / {routeName}";
    }

    [RelayCommand]
    private void CopyRecordedRoute(RecordedRouteViewModel? route)
    {
        if (!CanEditRecorder())
        {
            return;
        }

        route = ResolveRecordedRoute(route);
        if (route == null)
        {
            RecordStatusText = "录制器：请先选择路线";
            return;
        }

        var copiedTask = ClonePathingTask(route.Task);
        copiedTask.Info ??= new PathingTaskInfo();
        var sourceName = string.IsNullOrWhiteSpace(copiedTask.Info.Name) ? route.Name : copiedTask.Info.Name.Trim();
        copiedTask.Info.Name = CreateUniqueRecordedRouteName($"{sourceName}_副本");
        copiedTask.FullPath = string.Empty;
        copiedTask.FileName = string.Empty;

        var copiedRoute = CreateRecordedRoute(copiedTask, string.Empty);
        var insertIndex = RecordedRoutes.IndexOf(route);
        if (insertIndex < 0)
        {
            RecordedRoutes.Add(copiedRoute);
        }
        else
        {
            RecordedRoutes.Insert(insertIndex + 1, copiedRoute);
        }

        SelectedRecordedRoute = copiedRoute;
        RecordStatusText = $"录制器：已复制路线 / {copiedRoute.Name}";
    }

    [RelayCommand]
    private void OpenRecorderPreferences()
    {
        IsRecorderPreferencesOpen = true;
    }

    [RelayCommand]
    private void CloseRecorderPreferences()
    {
        IsRecorderPreferencesOpen = false;
    }

    [RelayCommand]
    private void ToggleMapMiniFollowWindow()
    {
        IsMapMiniFollowWindowVisible = !IsMapMiniFollowWindowVisible;
    }

    [RelayCommand]
    private void AddRecordAuthor()
    {
        RecordAuthors.Add(new RecordAuthorViewModel());
    }

    [RelayCommand]
    private void DeleteRecordAuthor(RecordAuthorViewModel? author)
    {
        if (author == null)
        {
            return;
        }

        RecordAuthors.Remove(author);
    }

    [RelayCommand]
    private void AddCommonRecordAuthor()
    {
        if (SelectedCommonRecordAuthor == null)
        {
            RecordStatusText = "录制器：请选择常见作者";
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedCommonRecordAuthor.Links))
        {
            RecordStatusText = "录制器：常见作者缺少链接，未加入";
            return;
        }

        AddRecordAuthorToRoute(SelectedCommonRecordAuthor.Name, SelectedCommonRecordAuthor.Links, "常见作者");
    }

    [RelayCommand]
    private void ImportCommonRecordAuthorsFromLibrary()
    {
        LoadCommonRecordAuthors(forceLibrary: true);
        SaveCommonRecordAuthors();
        RecordStatusText = $"录制器：已从作者库导入 {CommonRecordAuthors.Count} 位作者";
    }

    [RelayCommand]
    private void SetDefaultAuthorFromSelectedCommon()
    {
        if (SelectedCommonRecordAuthor == null)
        {
            RecordStatusText = "录制器：请选择作者库作者";
            return;
        }

        DefaultRecordAuthorName = SelectedCommonRecordAuthor.Name;
        DefaultRecordAuthorLinks = SelectedCommonRecordAuthor.Links;
        RecordStatusText = $"录制器：已设置默认作者 / {DefaultRecordAuthorName}";
    }

    [RelayCommand]
    private void AddDefaultAuthorToCommon()
    {
        var name = (DefaultRecordAuthorName ?? string.Empty).Trim();
        var links = (DefaultRecordAuthorLinks ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(links))
        {
            RecordStatusText = "录制器：默认作者名称和链接都不能为空";
            return;
        }

        AddCommonRecordAuthorToList(name, links, 0);
        SaveCommonRecordAuthors();
        RecordStatusText = $"录制器：已加入常用作者 / {name}";
    }

    [RelayCommand]
    private void AddCustomCommonAuthor()
    {
        var name = (NewCommonAuthorName ?? string.Empty).Trim();
        var links = (NewCommonAuthorLinks ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(links))
        {
            RecordStatusText = "录制器：常用作者名称和链接都不能为空";
            return;
        }

        AddCommonRecordAuthorToList(name, links, 0);
        NewCommonAuthorName = string.Empty;
        NewCommonAuthorLinks = string.Empty;
        SaveCommonRecordAuthors();
        RecordStatusText = $"录制器：已新增常用作者 / {name}";
    }

    [RelayCommand]
    private void DeleteCommonRecordAuthor(CommonRecordAuthorViewModel? author)
    {
        author ??= SelectedCommonRecordAuthor;
        if (author == null)
        {
            return;
        }

        var index = CommonRecordAuthors.IndexOf(author);
        CommonRecordAuthors.Remove(author);
        SelectedCommonRecordAuthor = CommonRecordAuthors.Count == 0
            ? null
            : CommonRecordAuthors[Math.Clamp(index, 0, CommonRecordAuthors.Count - 1)];
        SaveCommonRecordAuthors();
        OnPropertyChanged(nameof(CommonRecordAuthorSummaryText));
        RecordStatusText = $"录制器：已移除常用作者 / {author.Name}";
    }

    [RelayCommand]
    private void OpenOperationLog()
    {
        IsOperationLogOpen = true;
    }

    [RelayCommand]
    private void CloseOperationLog()
    {
        IsOperationLogOpen = false;
    }

    [RelayCommand]
    private void ClearOperationLogs()
    {
        OperationLogs.Clear();
        RefreshOperationLogProperties();
    }

    [RelayCommand]
    private void OpenActionUsageEditor()
    {
        RefreshActionUsageEditorOptions();
        IsActionUsageEditorOpen = true;
    }

    [RelayCommand]
    private void CloseActionUsageEditor()
    {
        IsActionUsageEditorOpen = false;
    }

    [RelayCommand]
    private void MoveActionToRare()
    {
        if (SelectedCommonActionUsageOption == null)
        {
            return;
        }

        var option = SelectedCommonActionUsageOption;
        _rareActionCodes.Add(option.Code);
        SaveRareActionCodes();
        RebuildActionMenuGroups();
        RefreshActionUsageEditorOptions();
        RecordStatusText = $"录制器：已将 {option.DisplayName} 设为其他动作";
    }

    [RelayCommand]
    private void MoveActionToCommon()
    {
        if (SelectedRareActionUsageOption == null)
        {
            return;
        }

        var option = SelectedRareActionUsageOption;
        _rareActionCodes.Remove(option.Code);
        SaveRareActionCodes();
        RebuildActionMenuGroups();
        RefreshActionUsageEditorOptions();
        RecordStatusText = $"录制器：已将 {option.DisplayName} 设为常用动作";
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

        var recorderTask = RecordedWaypoints.Count > 0
            ? ClonePathingTask(BuildRecordedTask())
            : null;

        IsRecorderMode = false;
        IsDebugMode = true;

        if (recorderTask != null)
        {
            LoadRecorderTaskAsCurrentPathing(recorderTask);
        }
    }

    [RelayCommand]
    private void SwitchToRecorderMode()
    {
        IsRecorderMode = true;
        IsDebugMode = false;
    }

    public bool HandleRecorderShortcut(
        Key key,
        ModifierKeys modifiers,
        IList selectedWaypointItems,
        IList selectedRouteItems,
        bool isRouteListFocused,
        bool isMapFocused,
        bool isTextInputFocused)
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

        if (modifiers == ModifierKeys.None && key == Key.Tab)
        {
            ToggleMapClickEditMode();
            return true;
        }

        if (!isRouteListFocused
            && modifiers == ModifierKeys.None
            && (isMapFocused || HasRecorderNudgeSelection())
            && TryGetRecorderNudgeDelta(key, RecorderNudgeStep, out var dx, out var dy))
        {
            return NudgeRecorderSelection(dx, dy);
        }

        if (isRouteListFocused)
        {
            if (modifiers == ModifierKeys.None && key == Key.F2)
            {
                RenameRecordedRoute(GetSelectedRecordedRoutes(selectedRouteItems).LastOrDefault());
                return true;
            }

            if (onlyCtrl && key == Key.A)
            {
                SelectAllRecordedRoutes(selectedRouteItems);
                return true;
            }

            if (onlyCtrl && key == Key.C)
            {
                CopySelectedRecordedRoutes(selectedRouteItems);
                return true;
            }

            if (onlyCtrl && key == Key.X)
            {
                CutSelectedRecordedRoutes(selectedRouteItems);
                return true;
            }

            if (onlyCtrl && key == Key.V)
            {
                PasteRecordedRoutes(selectedRouteItems);
                return true;
            }

            if (key == Key.Delete)
            {
                DeleteSelectedRecordedRoutes(selectedRouteItems);
                return true;
            }
        }

        if (modifiers == ModifierKeys.None && key == Key.Return)
        {
            return InsertWaypointOnSelectedRecorderEdge();
        }

        if (onlyCtrl && key == Key.C)
        {
            CopySelectedRecordedWaypoints(selectedWaypointItems);
            return true;
        }

        if (onlyCtrl && key == Key.X)
        {
            CutSelectedRecordedWaypoints(selectedWaypointItems);
            return true;
        }

        if (onlyCtrl && key == Key.V)
        {
            PasteRecordedWaypoints(selectedWaypointItems);
            return true;
        }

        if (onlyCtrl && key == Key.A)
        {
            SelectAllRecordedWaypoints(selectedWaypointItems);
            return true;
        }

        if (onlyCtrl && key == Key.D)
        {
            DuplicateSelectedRecordedWaypoints(selectedWaypointItems);
            return true;
        }

        if (key == Key.Delete)
        {
            DeleteSelectedRecordedWaypoints(selectedWaypointItems);
            return true;
        }

        return false;
    }

    private bool HasRecorderNudgeSelection()
    {
        return HasSelectedRecorderEdge
               || RecordedWaypoints.Any(i => i.IsSelected)
               || (SelectedRecordedWaypoint != null && RecordedWaypoints.Contains(SelectedRecordedWaypoint));
    }

    private static bool TryGetRecorderNudgeDelta(Key key, double step, out double dx, out double dy)
    {
        dx = 0;
        dy = 0;
        step = double.IsNaN(step) || double.IsInfinity(step) || step <= 0 ? 1.0 : Math.Min(step, 1000);

        switch (key)
        {
            case Key.Left:
            case Key.A:
                dx = step;
                return true;
            case Key.Right:
            case Key.D:
                dx = -step;
                return true;
            case Key.Up:
            case Key.W:
                dy = step;
                return true;
            case Key.Down:
            case Key.S:
                dy = -step;
                return true;
            default:
                return false;
        }
    }

    private bool NudgeRecorderSelection(double dx, double dy)
    {
        if (!CanEditRecorder())
        {
            return false;
        }

        if (HasSelectedRecorderEdge)
        {
            return NudgeSelectedRecorderEdge(dx, dy);
        }

        var selected = GetSelectedRecordedWaypointsFromState();
        if (selected.Count == 0)
        {
            return false;
        }

        _isUpdatingWaypointFromMap = true;
        try
        {
            foreach (var waypoint in selected)
            {
                waypoint.X = RoundCoordinate(waypoint.X + dx);
                waypoint.Y = RoundCoordinate(waypoint.Y + dy);
            }
        }
        finally
        {
            _isUpdatingWaypointFromMap = false;
        }

        if (selected.Count == 1)
        {
            SelectedRecordedWaypoint = selected[0];
        }

        PublishRecorderPath();
        PublishSelectedRecorderWaypoints(selected);
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(
            this,
            "SelectRecordedWaypointRows",
            new object(),
            selected));
        RecordStatusText = selected.Count == 1
            ? $"录制器：已微调第 {selected[0].Index} 点"
            : $"录制器：已微调 {selected.Count} 个点";
        return true;
    }

    private bool NudgeSelectedRecorderEdge(double dx, double dy)
    {
        if (_selectedRecorderEdgeInsertIndex <= 0
            || _selectedRecorderEdgeInsertIndex >= RecordedWaypoints.Count
            || _selectedRecorderEdgePoint == null)
        {
            ClearSelectedRecorderEdgeState(notifyMap: true);
            RecordStatusText = "录制器：路线边已失效";
            return true;
        }

        var start = RecordedWaypoints[_selectedRecorderEdgeInsertIndex - 1];
        var end = RecordedWaypoints[_selectedRecorderEdgeInsertIndex];
        _isUpdatingWaypointFromMap = true;
        try
        {
            start.X = RoundCoordinate(start.X + dx);
            start.Y = RoundCoordinate(start.Y + dy);
            end.X = RoundCoordinate(end.X + dx);
            end.Y = RoundCoordinate(end.Y + dy);
        }
        finally
        {
            _isUpdatingWaypointFromMap = false;
        }

        _selectedRecorderEdgePoint = new Point2f(
            (float)RoundCoordinate(_selectedRecorderEdgePoint.Value.X + dx),
            (float)RoundCoordinate(_selectedRecorderEdgePoint.Value.Y + dy));
        PublishRecorderPath();
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(
            this,
            "SelectRecorderRouteEdgeVisual",
            new object(),
            new RecorderRouteEdgeSelection(_selectedRecorderEdgeInsertIndex, _selectedRecorderEdgePoint.Value)));
        RecordStatusText = $"录制器：已微调第 {start.Index}-{end.Index} 段";
        return true;
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
        IsFollowingCurrent = true;
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
    private async Task RunModeAction()
    {
        if (IsRecorderMode)
        {
            await ToggleRecording();
            return;
        }

        await RunRecording();
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
        IsFollowingCurrent = true;
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

    private void LoadCommonRecordAuthors(bool forceLibrary = false)
    {
        try
        {
            var items = new List<CommonRecordAuthorStoreItem>();
            if (!forceLibrary && !string.IsNullOrWhiteSpace(TaskContext.Instance().Config.DevConfig.RecordCommonAuthorsJson))
            {
                items = JsonSerializer.Deserialize<List<CommonRecordAuthorStoreItem>>(
                    TaskContext.Instance().Config.DevConfig.RecordCommonAuthorsJson) ?? [];
            }

            if (items.Count == 0 || forceLibrary)
            {
                var resource = System.Windows.Application.GetResourceStream(
                    new Uri("pack://application:,,,/Resources/Data/CommonRecordAuthors.json", UriKind.Absolute));
                if (resource?.Stream == null)
                {
                    return;
                }

                using var stream = resource.Stream;
                items = JsonSerializer.Deserialize<List<CommonRecordAuthorStoreItem>>(stream) ?? [];
            }

            CommonRecordAuthors.Clear();
            foreach (var item in items
                         .Where(i => !string.IsNullOrWhiteSpace(i.Name) && !string.IsNullOrWhiteSpace(i.Links))
                         .OrderByDescending(i => i.Count)
                         .ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                AddCommonRecordAuthorToList(item.Name.Trim(), item.Links.Trim(), item.Count, save: false);
            }

            SelectedCommonRecordAuthor = CommonRecordAuthors.FirstOrDefault();
            OnPropertyChanged(nameof(CommonRecordAuthorSummaryText));
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private void AddCommonRecordAuthorToList(string name, string links, int count, bool save = true)
    {
        name = name.Trim();
        links = links.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(links))
        {
            return;
        }

        var existing = CommonRecordAuthors.FirstOrDefault(i =>
            string.Equals(i.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (existing != null)
        {
            SelectedCommonRecordAuthor = existing;
            return;
        }

        var author = new CommonRecordAuthorViewModel(name, links, count);
        CommonRecordAuthors.Add(author);
        SelectedCommonRecordAuthor = author;
        OnPropertyChanged(nameof(CommonRecordAuthorSummaryText));
        if (save)
        {
            SaveCommonRecordAuthors();
        }
    }

    private void SaveCommonRecordAuthors()
    {
        var items = CommonRecordAuthors
            .Where(i => !string.IsNullOrWhiteSpace(i.Name) && !string.IsNullOrWhiteSpace(i.Links))
            .Select(i => new CommonRecordAuthorStoreItem
            {
                Name = i.Name.Trim(),
                Links = i.Links.Trim(),
                Count = i.Count
            })
            .ToList();
        TaskContext.Instance().Config.DevConfig.RecordCommonAuthorsJson = JsonSerializer.Serialize(items);
    }

    private List<PathingTaskAuthor> CreateDefaultRecordAuthors()
    {
        var name = (DefaultRecordAuthorName ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(name)
            ? []
            :
            [
                new PathingTaskAuthor
                {
                    Name = name,
                    Links = (DefaultRecordAuthorLinks ?? string.Empty).Trim()
                }
            ];
    }

    private List<PathingTaskAuthor> MergeDefaultRecordAuthors(IEnumerable<PathingTaskAuthor> authors)
    {
        var result = authors
            .Where(i => !string.IsNullOrWhiteSpace(i.Name))
            .Select(i => new PathingTaskAuthor
            {
                Name = i.Name.Trim(),
                Links = (i.Links ?? string.Empty).Trim()
            })
            .ToList();

        foreach (var defaultAuthor in CreateDefaultRecordAuthors())
        {
            var existing = result.FirstOrDefault(i =>
                string.Equals(i.Name, defaultAuthor.Name, StringComparison.CurrentCultureIgnoreCase));
            if (existing == null)
            {
                result.Add(defaultAuthor);
                continue;
            }

            if (string.IsNullOrWhiteSpace(existing.Links) && !string.IsNullOrWhiteSpace(defaultAuthor.Links))
            {
                existing.Links = defaultAuthor.Links;
            }
        }

        return result;
    }

    private static List<PathingTaskAuthor> GetTaskAuthors(PathingTaskInfo info)
    {
        var authors = (info.Authors ?? [])
            .Where(i => !string.IsNullOrWhiteSpace(i.Name))
            .Select(i => new PathingTaskAuthor
            {
                Name = i.Name.Trim(),
                Links = (i.Links ?? string.Empty).Trim()
            })
            .ToList();

        if (authors.Count == 0 && !string.IsNullOrWhiteSpace(info.Author))
        {
            authors.Add(new PathingTaskAuthor { Name = info.Author.Trim() });
        }

        return authors;
    }

    private void SetRecordAuthors(IEnumerable<PathingTaskAuthor> authors)
    {
        _isUpdatingRecordAuthors = true;
        try
        {
            RecordAuthors.Clear();
            foreach (var author in authors)
            {
                if (string.IsNullOrWhiteSpace(author.Name))
                {
                    continue;
                }

                RecordAuthors.Add(new RecordAuthorViewModel(author.Name.Trim(), (author.Links ?? string.Empty).Trim()));
            }
        }
        finally
        {
            _isUpdatingRecordAuthors = false;
        }

        RefreshRecordAuthorProperties();
    }

    private List<PathingTaskAuthor> GetRecordAuthorsForSave()
    {
        return RecordAuthors
            .Where(i => !string.IsNullOrWhiteSpace(i.Name))
            .Select(i => new PathingTaskAuthor
            {
                Name = i.Name.Trim(),
                Links = (i.Links ?? string.Empty).Trim()
            })
            .ToList();
    }

    private void AddRecordAuthorToRoute(string name, string links, string sourceText)
    {
        if (!CanEditRecorder())
        {
            return;
        }

        name = (name ?? string.Empty).Trim();
        links = (links ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            RecordStatusText = $"录制器：{sourceText}名称为空";
            return;
        }

        var existing = RecordAuthors.FirstOrDefault(i => string.Equals(i.Name.Trim(), name, StringComparison.Ordinal));
        if (existing != null)
        {
            if (string.IsNullOrWhiteSpace(existing.Links) && !string.IsNullOrWhiteSpace(links))
            {
                existing.Links = links;
                RecordStatusText = $"录制器：已补全作者链接 / {name}";
                return;
            }

            RecordStatusText = $"录制器：作者已存在 / {name}";
            return;
        }

        var empty = RecordAuthors.FirstOrDefault(i =>
            string.IsNullOrWhiteSpace(i.Name)
            && string.IsNullOrWhiteSpace(i.Links));
        if (empty != null)
        {
            empty.Name = name;
            empty.Links = links;
        }
        else
        {
            RecordAuthors.Add(new RecordAuthorViewModel(name, links));
        }

        RecordStatusText = $"录制器：已加入{sourceText} / {name}";
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

        var selected = GetSelectedRecordedRoutesFromState();
        if (selected.Count != 1)
        {
            RecordStatusText = "录制器：请选择一条路线进行拆分";
            return;
        }

        var sourceRoute = selected[0];
        PathingTask task;
        if (ReferenceEquals(sourceRoute, SelectedRecordedRoute))
        {
            if (!TryApplyJsonEdits(saveToFile: false, allowFilePicker: false, owner: DialogOwner))
            {
                return;
            }

            task = BuildRecordedTask();
        }
        else
        {
            task = ClonePathingTask(sourceRoute.Task);
        }

        var groups = SplitWaypointsByTeleport(task.Positions);
        if (groups.Count <= 1)
        {
            RecordStatusText = "录制器：没有可按传送点拆分的路线";
            return;
        }

        var routeName = string.IsNullOrWhiteSpace(task.Info.Name) ? RecordFileName : task.Info.Name.Trim();
        var selectedIndex = RecordedRoutes.IndexOf(sourceRoute);
        if (selectedIndex < 0)
        {
            selectedIndex = RecordedRoutes.Count - 1;
        }

        var firstSplitRoute = default(RecordedRouteViewModel);
        for (var i = 0; i < groups.Count; i++)
        {
            var splitTask = ClonePathingTask(task);
            splitTask.Info.Name = CreateUniqueRecordedRouteName($"{routeName}_拆分{i + 1}");
            splitTask.Positions = groups[i];
            splitTask.FullPath = string.Empty;
            splitTask.FileName = string.Empty;
            var splitRoute = CreateRecordedRoute(splitTask, string.Empty);
            RecordedRoutes.Insert(Math.Min(selectedIndex + 1 + i, RecordedRoutes.Count), splitRoute);
            firstSplitRoute ??= splitRoute;
        }

        SelectRecordedRouteWithoutReload(firstSplitRoute);
        if (firstSplitRoute != null)
        {
            LoadRecordedRoute(firstSplitRoute);
        }

        RecordStatusText = $"录制器：已按传送点拆分为 {groups.Count} 条新路线，原路线已保留";
    }

    [RelayCommand]
    private void DeleteSelectedRecordedRoute(RecordedRouteViewModel? route = null)
    {
        if (!CanEditRecorder())
        {
            return;
        }

        route = ResolveRecordedRoute(route);
        if (route == null)
        {
            RecordStatusText = "录制器：请先选择路线";
            return;
        }

        var wasSelectedRoute = ReferenceEquals(route, SelectedRecordedRoute);
        var index = RecordedRoutes.IndexOf(route);
        if (index < 0)
        {
            return;
        }

        RecordedRoutes.Remove(route);
        if (wasSelectedRoute)
        {
            IsRecordedRoutePropertiesOpen = false;
        }

        if (RecordedRoutes.Count == 0)
        {
            NewRecording();
            return;
        }

        if (wasSelectedRoute || SelectedRecordedRoute == null)
        {
            SelectedRecordedRoute = RecordedRoutes[Math.Clamp(index, 0, RecordedRoutes.Count - 1)];
        }

        RecordStatusText = $"录制器：已删除路线 / {route.Name}";
    }

    private List<RecordedRouteViewModel> GetSelectedRecordedRoutes(IList selectedItems)
    {
        var selected = selectedItems.OfType<RecordedRouteViewModel>()
            .Where(i => RecordedRoutes.Contains(i))
            .OrderBy(i => RecordedRoutes.IndexOf(i))
            .ToList();

        if (selected.Count == 0 && SelectedRecordedRoute != null && RecordedRoutes.Contains(SelectedRecordedRoute))
        {
            selected.Add(SelectedRecordedRoute);
        }

        return selected;
    }

    private List<RecordedRouteViewModel> GetSelectedRecordedRoutesFromState()
    {
        var selected = RecordedRoutes
            .Where(i => i.IsSelected)
            .OrderBy(i => RecordedRoutes.IndexOf(i))
            .ToList();

        if (selected.Count == 0 && SelectedRecordedRoute != null && RecordedRoutes.Contains(SelectedRecordedRoute))
        {
            selected.Add(SelectedRecordedRoute);
        }

        return selected;
    }

    private void SelectAllRecordedRoutes(IList selectedItems)
    {
        if (!CanEditRecorder())
        {
            return;
        }

        selectedItems.Clear();
        foreach (var route in RecordedRoutes)
        {
            selectedItems.Add(route);
        }

        SyncRecordedRouteSelection(selectedItems);
        RecordStatusText = $"录制器：已选择 {RecordedRoutes.Count} 条路线";
    }

    private void CopySelectedRecordedRoutes(IList selectedItems)
    {
        if (!CanEditRecorder())
        {
            return;
        }

        var selected = GetSelectedRecordedRoutes(selectedItems);
        if (selected.Count == 0)
        {
            return;
        }

        CopyRoutesToClipboard(selected);
        RecordStatusText = $"录制器：已复制 {selected.Count} 条路线";
    }

    private void CutSelectedRecordedRoutes(IList selectedItems)
    {
        if (!CanEditRecorder())
        {
            return;
        }

        var selected = GetSelectedRecordedRoutes(selectedItems);
        if (selected.Count == 0)
        {
            return;
        }

        CopyRoutesToClipboard(selected);
        RemoveRecordedRoutes(selected);
        selectedItems.Clear();
        RecordStatusText = $"录制器：已剪切 {selected.Count} 条路线";
    }

    private void PasteRecordedRoutes(IList selectedItems)
    {
        if (!CanEditRecorder())
        {
            return;
        }

        var tasks = _recordedRouteClipboard?.Select(ClonePathingTask).ToList() ?? [];
        if (tasks.Count == 0)
        {
            tasks = ReadRoutesFromClipboard();
        }

        if (tasks.Count == 0)
        {
            RecordStatusText = "录制器：剪贴板中没有路线";
            return;
        }

        var selected = GetSelectedRecordedRoutes(selectedItems);
        var insertIndex = selected.Count == 0
            ? RecordedRoutes.Count
            : RecordedRoutes.IndexOf(selected.Last()) + 1;
        var pastedRoutes = new List<RecordedRouteViewModel>();
        for (var i = 0; i < tasks.Count; i++)
        {
            var task = tasks[i];
            task.Info ??= new PathingTaskInfo();
            var routeName = string.IsNullOrWhiteSpace(task.Info.Name) ? "未命名路线" : task.Info.Name.Trim();
            task.Info.Name = CreateUniqueRecordedRouteName(routeName);
            task.FullPath = string.Empty;
            task.FileName = string.Empty;
            var route = CreateRecordedRoute(task, string.Empty);
            RecordedRoutes.Insert(Math.Min(insertIndex + i, RecordedRoutes.Count), route);
            pastedRoutes.Add(route);
        }

        selectedItems.Clear();
        foreach (var route in pastedRoutes)
        {
            selectedItems.Add(route);
        }

        SyncRecordedRouteSelection(selectedItems);
        RecordStatusText = $"录制器：已粘贴 {pastedRoutes.Count} 条路线";
    }

    private void DeleteSelectedRecordedRoutes(IList selectedItems)
    {
        if (!CanEditRecorder())
        {
            return;
        }

        var selected = GetSelectedRecordedRoutes(selectedItems);
        if (selected.Count == 0)
        {
            return;
        }

        RemoveRecordedRoutes(selected);
        selectedItems.Clear();
        RecordStatusText = $"录制器：已删除 {selected.Count} 条路线";
    }

    private void RemoveRecordedRoutes(IReadOnlyList<RecordedRouteViewModel> routes)
    {
        if (routes.Count == 0)
        {
            return;
        }

        var selectedIndex = SelectedRecordedRoute == null ? -1 : RecordedRoutes.IndexOf(SelectedRecordedRoute);
        foreach (var route in routes.Where(RecordedRoutes.Contains).ToList())
        {
            RecordedRoutes.Remove(route);
        }

        IsRecordedRoutePropertiesOpen = false;
        if (RecordedRoutes.Count == 0)
        {
            NewRecording();
            return;
        }

        var nextIndex = Math.Clamp(selectedIndex, 0, RecordedRoutes.Count - 1);
        SelectedRecordedRoute = RecordedRoutes[nextIndex];
    }

    private void CopyRoutesToClipboard(IReadOnlyList<RecordedRouteViewModel> selected)
    {
        _recordedRouteClipboard = selected.Select(i => ClonePathingTask(i.Task)).ToList();
        _recordedRouteClipboardText = _recordedRouteClipboard.Count == 1
            ? _recordedRouteClipboard[0].ToJsonString()
            : $"[{string.Join(",", _recordedRouteClipboard.Select(i => i.ToJsonString()))}]";
        _ = TrySetClipboardText(_recordedRouteClipboardText);
    }

    private static List<PathingTask> ReadRoutesFromClipboard()
    {
        try
        {
            if (!TryGetClipboardText(out var text, out _) || string.IsNullOrWhiteSpace(text))
            {
                return [];
            }

            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return IsPathingTaskJson(document.RootElement)
                    ? [PathingTask.BuildFromJson(text)]
                    : [];
            }

            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var tasks = new List<PathingTask>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (!IsPathingTaskJson(item))
                {
                    continue;
                }

                tasks.Add(PathingTask.BuildFromJson(item.GetRawText()));
            }

            return tasks;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return [];
        }
    }

    private static bool IsPathingTaskJson(JsonElement root)
    {
        return root.ValueKind == JsonValueKind.Object
               && root.TryGetProperty("info", out var info)
               && info.ValueKind == JsonValueKind.Object
               && root.TryGetProperty("positions", out var positions)
               && positions.ValueKind == JsonValueKind.Array;
    }

    [RelayCommand]
    private void MergeRecordedRoutes()
    {
        if (!CanEditRecorder())
        {
            return;
        }

        var selected = GetSelectedRecordedRoutesFromState();
        if (selected.Count <= 1)
        {
            RecordStatusText = "录制器：请选择至少两条路线进行合并";
            return;
        }

        if (SelectedRecordedRoute != null && selected.Contains(SelectedRecordedRoute))
        {
            if (!TryApplyJsonEdits(saveToFile: false, allowFilePicker: false, owner: DialogOwner))
            {
                return;
            }
        }

        selected = GetSelectedRecordedRoutesFromState();
        var mapNames = selected
            .Select(route => RouteGraphGeometry.NormalizeMapName(route.MapName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (mapNames.Count > 1)
        {
            RecordStatusText = $"录制器：无法合并不同地图路线 / {string.Join("、", mapNames)}";
            return;
        }

        var baseRoute = selected[0];
        var baseTask = ClonePathingTask(baseRoute.Task);
        baseTask.Positions = selected
            .SelectMany(route => route.Task.Positions.Select(CloneWaypoint))
            .ToList();
        baseTask.Info.Name = CreateUniqueRecordedRouteName($"{baseRoute.Name}_合并");
        baseTask.Info.MapName = mapNames[0];
        baseTask.FullPath = string.Empty;
        baseTask.FileName = string.Empty;

        var mergedRoute = CreateRecordedRoute(baseTask, string.Empty);
        var insertIndex = RecordedRoutes.IndexOf(selected.Last());
        RecordedRoutes.Insert(Math.Min(insertIndex + 1, RecordedRoutes.Count), mergedRoute);
        SelectRecordedRouteWithoutReload(mergedRoute);
        LoadRecordedRoute(mergedRoute);
        RecordStatusText = $"录制器：已合并 {selected.Count} 条路线为新路线 / {baseTask.Positions.Count} 点，原路线已保留";
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

    private RecordedRouteViewModel? ResolveRecordedRoute(RecordedRouteViewModel? route)
    {
        if (route != null && RecordedRoutes.Contains(route))
        {
            return route;
        }

        return SelectedRecordedRoute != null && RecordedRoutes.Contains(SelectedRecordedRoute)
            ? SelectedRecordedRoute
            : null;
    }

    private string CreateUniqueRecordedRouteName(string preferredName)
    {
        var baseName = string.IsNullOrWhiteSpace(preferredName) ? "未命名路线" : preferredName.Trim();
        var existingNames = RecordedRoutes
            .Select(i => i.Name)
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        if (!existingNames.Contains(baseName))
        {
            return baseName;
        }

        for (var i = 2; ; i++)
        {
            var candidate = $"{baseName}_{i}";
            if (!existingNames.Contains(candidate))
            {
                return candidate;
            }
        }
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
            RefreshRecordedRouteSelectionProperties();

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
            RefreshRecordedRouteSelectionProperties();
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
        SetRecordAuthors(CreateDefaultRecordAuthors());
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
        var name = (DefaultRecordAuthorName ?? string.Empty).Trim();
        var links = (DefaultRecordAuthorLinks ?? string.Empty).Trim();
        AddRecordAuthorToRoute(name, links, "默认作者");
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

        RecordedWaypoints.Remove(waypoint);
        SelectedRecordedWaypoint = RecordedWaypoints.FirstOrDefault();
        ReindexRecordedWaypoints();
        PublishRecorderPath();
        RecordStatusText = $"录制器：已删除第 {waypoint.Index} 个点";
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

        ClearSelectedRecorderEdgeState(notifyMap: false);
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

        ClearSelectedRecorderEdgeState(notifyMap: false);
        SelectedRecordedWaypoint = RecordedWaypoints[waypointIndex];
        foreach (var item in RecordedWaypoints)
        {
            item.IsSelected = ReferenceEquals(item, SelectedRecordedWaypoint);
        }

        PublishSelectedRecorderWaypoints();
    }

    private void SelectRecorderRouteEdge(RecorderRouteEdgeSelection edgeSelection)
    {
        if (!IsRecorderMode)
        {
            return;
        }

        _selectedRecorderEdgeInsertIndex = Math.Clamp(edgeSelection.InsertIndex, 0, RecordedWaypoints.Count);
        _selectedRecorderEdgePoint = edgeSelection.Point;
        SelectedRecordedWaypoint = null;
        foreach (var item in RecordedWaypoints)
        {
            item.IsSelected = false;
        }

        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(
            this,
            "ClearRecordedWaypointRowsSelectionOnly",
            new object(),
            new object()));
        RecordStatusText = $"录制器：已选择路线边，按 Enter 插入点";
        RefreshRecorderEdgeSelectionProperties();
    }

    private void ClearSelectedRecorderEdgeState(bool notifyMap)
    {
        _selectedRecorderEdgeInsertIndex = -1;
        _selectedRecorderEdgePoint = null;
        RefreshRecorderEdgeSelectionProperties();
        if (!notifyMap)
        {
            return;
        }

        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(
            this,
            "ClearSelectedRecorderEdge",
            new object(),
            new object()));
    }

    private void RefreshRecorderEdgeSelectionProperties()
    {
        OnPropertyChanged(nameof(HasSelectedRecorderEdge));
        OnPropertyChanged(nameof(MapEdgeContextMenuVisibility));
        OnPropertyChanged(nameof(MapWaypointContextMenuVisibility));
    }

    [RelayCommand]
    private void InsertWaypointOnSelectedRecorderEdgeFromMenu()
    {
        _ = InsertWaypointOnSelectedRecorderEdge();
    }

    [RelayCommand]
    private void FitSelectedRecorderEdge()
    {
        if (_selectedRecorderEdgePoint == null)
        {
            RecordStatusText = "录制器：请先选择路线边";
            return;
        }

        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(
            this,
            "FitSelectedRecorderWaypointPositions",
            new object(),
            new[] { _selectedRecorderEdgePoint.Value }));
        RecordStatusText = "录制器：已定位到路线边插入点";
    }

    [RelayCommand]
    private void ClearSelectedRecorderEdgeSelection()
    {
        ClearSelectedRecorderEdgeState(notifyMap: true);
        RecordStatusText = "录制器：已取消路线边选择";
    }

    [RelayCommand]
    private void ReverseSelectedRecorderEdge()
    {
        if (!CanEditRecorder())
        {
            return;
        }

        if (_selectedRecorderEdgeInsertIndex <= 0 || _selectedRecorderEdgeInsertIndex >= RecordedWaypoints.Count)
        {
            ClearSelectedRecorderEdgeState(notifyMap: true);
            RecordStatusText = "录制器：路线边已失效";
            return;
        }

        var startIndex = _selectedRecorderEdgeInsertIndex - 1;
        var endIndex = _selectedRecorderEdgeInsertIndex;
        RecordedWaypoints.Move(endIndex, startIndex);
        ReindexRecordedWaypoints();
        PublishRecorderPath();
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(
            this,
            "SelectRecorderRouteEdgeVisual",
            new object(),
            new RecorderRouteEdgeSelection(_selectedRecorderEdgeInsertIndex, _selectedRecorderEdgePoint ?? new Point2f(
                (float)((RecordedWaypoints[startIndex].X + RecordedWaypoints[endIndex].X) / 2),
                (float)((RecordedWaypoints[startIndex].Y + RecordedWaypoints[endIndex].Y) / 2)))));
        RecordStatusText = $"录制器：已反转第 {RecordedWaypoints[startIndex].Index}-{RecordedWaypoints[endIndex].Index} 点";
    }

    [RelayCommand]
    private void ReverseRecordedWaypoints()
    {
        if (!CanEditRecorder())
        {
            return;
        }

        if (RecordedWaypoints.Count < 2)
        {
            RecordStatusText = "录制器：点位不足，无法反转";
            return;
        }

        var reversed = RecordedWaypoints.Reverse().ToList();
        RecordedWaypoints.Clear();
        foreach (var waypoint in reversed)
        {
            waypoint.IsSelected = false;
            waypoint.IsLocked = false;
            RecordedWaypoints.Add(waypoint);
        }

        SelectedRecordedWaypoint = null;
        ReindexRecordedWaypoints();
        ClearSelectedRecorderEdgeState(notifyMap: true);
        PublishSelectedRecorderWaypoints();
        PublishRecorderPath();
        RecordStatusText = $"录制器：已反转 {RecordedWaypoints.Count} 个点位";
    }

    private bool InsertWaypointOnSelectedRecorderEdge()
    {
        if (_selectedRecorderEdgeInsertIndex < 0 || _selectedRecorderEdgePoint == null)
        {
            return false;
        }

        if (!CanEditRecorder())
        {
            return true;
        }

        var insertIndex = Math.Clamp(_selectedRecorderEdgeInsertIndex, 0, RecordedWaypoints.Count);
        var point = _selectedRecorderEdgePoint.Value;
        var viewModel = CreateRecordedWaypointViewModel(new Waypoint
        {
            X = RoundCoordinate(point.X),
            Y = RoundCoordinate(point.Y),
            Type = WaypointType.Path.Code,
            MoveMode = MoveModeEnum.Walk.Code
        });

        RecordedWaypoints.Insert(insertIndex, viewModel);
        foreach (var item in RecordedWaypoints)
        {
            item.IsSelected = ReferenceEquals(item, viewModel);
        }

        SelectedRecordedWaypoint = viewModel;
        ClearSelectedRecorderEdgeState(notifyMap: true);
        ClearPathing();
        ReindexRecordedWaypoints();
        PublishRecorderPath();
        PublishSelectedRecorderWaypoints([viewModel]);
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(
            this,
            "SelectRecordedWaypointRows",
            new object(),
            new[] { viewModel }));
        RecordStatusText = $"录制器：已在第 {viewModel.Index} 点插入路线点";
        return true;
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
            RecordedWaypoints.Insert(Math.Min(lockedIndex + 1, RecordedWaypoints.Count), viewModel);
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
        _ = TrySetClipboardText(_recordedWaypointClipboardText);
    }

    private bool TryReadPathingTaskFromClipboard(out PathingTask task, out string? errorMessage)
    {
        task = new PathingTask();
        errorMessage = null;

        try
        {
            if (!TryGetClipboardText(out var text, out _) || !LooksLikePathingTaskJson(text))
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
        if (TryGetClipboardText(out var text, out clipboardHadText) && clipboardHadText)
        {
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

        if (clipboardHadText)
        {
            return [];
        }

        return !string.IsNullOrWhiteSpace(_recordedWaypointClipboardText)
               && _recordedWaypointClipboard is { Count: > 0 }
            ? _recordedWaypointClipboard.Select(CloneWaypoint).ToList()
            : [];
    }

    private static bool TryGetClipboardText(out string text, out bool clipboardHadText)
    {
        text = string.Empty;
        clipboardHadText = false;

        for (var attempt = 1; attempt <= ClipboardRetryCount; attempt++)
        {
            try
            {
                text = System.Windows.Clipboard.GetText();
                clipboardHadText = !string.IsNullOrEmpty(text);
                return true;
            }
            catch (ExternalException ex) when (IsClipboardCannotOpenException(ex) && attempt < ClipboardRetryCount)
            {
                Thread.Sleep(ClipboardRetryDelayMilliseconds);
            }
            catch (ExternalException ex) when (IsClipboardCannotOpenException(ex))
            {
                Debug.WriteLine($"OpenClipboard failed after {ClipboardRetryCount} retries: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                return false;
            }
        }

        return false;
    }

    private static bool TrySetClipboardText(string? text)
    {
        if (text == null)
        {
            return false;
        }

        for (var attempt = 1; attempt <= ClipboardRetryCount; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetText(text);
                return true;
            }
            catch (ExternalException ex) when (IsClipboardCannotOpenException(ex) && attempt < ClipboardRetryCount)
            {
                Thread.Sleep(ClipboardRetryDelayMilliseconds);
            }
            catch (ExternalException ex) when (IsClipboardCannotOpenException(ex))
            {
                Debug.WriteLine($"SetClipboardText failed after {ClipboardRetryCount} retries: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                return false;
            }
        }

        return false;
    }

    private static bool IsClipboardCannotOpenException(ExternalException ex)
    {
        return ex.HResult == ClipboardCannotOpenHResult;
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
        SetRecordAuthors(MergeDefaultRecordAuthors(GetTaskAuthors(task.Info)));
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
        task.Info.Authors = GetRecordAuthorsForSave();
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

    public void ReplayMapDisplaySnapshot()
    {
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(
            this,
            "SetMapViewerRecorderMode",
            new object(),
            IsRecorderMode));

        if (_lastPosition is { } currentPosition)
        {
            var currentMapName = ResolvePositionMapName(_lastPositionMapName);
            WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(
                this,
                "SendCurrentPosition",
                new object(),
                new TrackedMapPosition(currentMapName, currentPosition)));
        }

        if (_selectedTargetPoint is { } targetPoint)
        {
            WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(
                this,
                "SelectPathingTargetPosition",
                new object(),
                targetPoint));
        }

        if (_currentRoutePoints.Count > 0)
        {
            WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(
                this,
                "UpdateCurrentPathing",
                new object(),
                BuildCurrentPathingSnapshot()));
        }

        if (IsRecorderMode)
        {
            WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(
                this,
                "UpdateRecorderPathing",
                new object(),
                BuildRecordedTask()));
            PublishRecordedRouteList();
            PublishSelectedRecorderWaypoints();
        }
    }

    private PathingTask BuildCurrentPathingSnapshot()
    {
        return new PathingTask
        {
            Info = new PathingTaskInfo
            {
                Name = TaskName,
                MapName = MapName,
                Type = "pathing"
            },
            Positions = _currentRoutePoints.ToList()
        };
    }

    private void LoadRecorderTaskAsCurrentPathing(PathingTask task)
    {
        UpdateTaskSummary(task);
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(
            this,
            "UpdateCurrentPathing",
            new object(),
            task));
        RecordStatusText = string.IsNullOrWhiteSpace(_recordFilePath)
            ? $"调试：已加载未保存录制路线 / {task.Positions.Count} 点"
            : $"调试：已加载录制路线 / {task.Positions.Count} 点";
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
        SynchronizeTrackingMap(pathingTask.Info.MapName);
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

    private string FormatCurrentPosition(Point2f imagePoint, string? mapName = null)
    {
        try
        {
            var matchingMethod = TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod;
            var currentMapName = ResolvePositionMapName(mapName);
            var gamePoint = MapManager.GetMap(currentMapName, matchingMethod).ConvertImageCoordinatesToGenshinMapCoordinates(imagePoint);
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

    private void RebuildActionOptions()
    {
        ActionOptions.Clear();
        foreach (var option in BuildActionOptions())
        {
            ActionOptions.Add(option);
        }

        RebuildActionMenuGroups();
        RefreshActionUsageEditorOptions();
    }

    private IEnumerable<MapEditorOption> BuildActionOptions()
    {
        yield return new MapEditorOption(string.Empty, "无", ParameterHint: GetActionParameterHint(null));
        foreach (var action in ActionEnum.Values)
        {
            yield return new MapEditorOption(action.Code, action.Msg, ParameterHint: GetActionParameterHint(action.Code));
        }
    }

    private void RebuildActionMenuGroups()
    {
        var commonActions = ActionOptions
            .Where(i => !string.IsNullOrWhiteSpace(i.Code) || string.Equals(i.DisplayName, "无", StringComparison.Ordinal))
            .Where(i => !IsElementalAction(i.Code) && !IsRareAction(i.Code))
            .ToList();
        var elementalActions = ActionOptions
            .Where(i => IsElementalAction(i.Code))
            .ToList();
        var rareActions = ActionOptions
            .Where(i => IsRareAction(i.Code))
            .ToList();

        ActionMenuGroups.Clear();
        ActionMenuGroups.Add(new ActionMenuGroupViewModel("常用动作", commonActions));
        ActionMenuGroups.Add(new ActionMenuGroupViewModel("元素力采集", elementalActions));
        ActionMenuGroups.Add(new ActionMenuGroupViewModel("其他动作", rareActions, canEdit: true));
    }

    private void RefreshActionUsageEditorOptions()
    {
        var selectedCommonCode = SelectedCommonActionUsageOption?.Code;
        var selectedRareCode = SelectedRareActionUsageOption?.Code;
        CommonActionUsageOptions.Clear();
        RareActionUsageOptions.Clear();

        foreach (var option in ActionOptions.Where(i => !string.IsNullOrWhiteSpace(i.Code) && !IsElementalAction(i.Code)))
        {
            var item = new ActionUsageEditorItemViewModel(option.Code, option.DisplayName);
            if (IsRareAction(option.Code))
            {
                RareActionUsageOptions.Add(item);
            }
            else
            {
                CommonActionUsageOptions.Add(item);
            }
        }

        SelectedCommonActionUsageOption = CommonActionUsageOptions.FirstOrDefault(i => string.Equals(i.Code, selectedCommonCode, StringComparison.OrdinalIgnoreCase));
        SelectedRareActionUsageOption = RareActionUsageOptions.FirstOrDefault(i => string.Equals(i.Code, selectedRareCode, StringComparison.OrdinalIgnoreCase));
    }

    private void LoadRareActionCodes()
    {
        _rareActionCodes.Clear();
        var configured = TaskContext.Instance().Config.DevConfig.RecordRareActionCodes;
        var codes = string.Equals(configured, NoRareActionCodesMarker, StringComparison.Ordinal)
            ? []
            : string.IsNullOrWhiteSpace(configured)
            ? DefaultRareActionCodes
            : configured
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(IsKnownActionCode)
                .ToArray();

        foreach (var code in codes.Where(i => !IsElementalAction(i)))
        {
            _rareActionCodes.Add(code);
        }
    }

    private void SaveRareActionCodes()
    {
        TaskContext.Instance().Config.DevConfig.RecordRareActionCodes = _rareActionCodes.Count == 0
            ? NoRareActionCodesMarker
            : string.Join(",", _rareActionCodes.OrderBy(i => i, StringComparer.OrdinalIgnoreCase));
    }

    private bool IsRareAction(string? action)
    {
        return !string.IsNullOrWhiteSpace(action)
            && _rareActionCodes.Contains(action);
    }

    private static bool IsElementalAction(string? action)
    {
        return !string.IsNullOrWhiteSpace(action)
            && ElementalActionCodes.Any(i => string.Equals(i, action, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsKnownActionCode(string action)
    {
        return ActionEnum.GetEnumByCode(action) != null;
    }

    private void AddOperationLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (OperationLogs.FirstOrDefault()?.Message == message)
        {
            return;
        }

        OperationLogs.Insert(0, new OperationLogEntryViewModel(DateTime.Now.ToString("HH:mm:ss"), message, BuildOperationLogContext()));
        while (OperationLogs.Count > OperationLogLimit)
        {
            OperationLogs.RemoveAt(OperationLogs.Count - 1);
        }

        RefreshOperationLogProperties();
    }

    private string BuildOperationLogContext()
    {
        var selected = GetSelectedRecordedWaypointsFromState();
        var selectedText = selected.Count switch
        {
            0 => "未选点",
            1 => $"选中#{selected[0].Index}",
            _ => $"选中{selected.Count}点"
        };
        var currentText = _lastPosition.HasValue
            ? $"{FormatCoordinate(_lastPosition.Value.X)}, {FormatCoordinate(_lastPosition.Value.Y)}"
            : "等待坐标";
        var routeName = string.IsNullOrWhiteSpace(RecordFileName) ? "未命名路线" : RecordFileName;
        var fileText = string.IsNullOrWhiteSpace(_recordFilePath) ? "未保存" : Path.GetFileName(_recordFilePath);
        return $"{(IsRecorderMode ? "录制" : "调试")} · {MapDisplayName} · {routeName} · {RecordedWaypoints.Count}点 · {selectedText} · 当前 {currentText} · {fileText}";
    }

    private void RefreshOperationLogProperties()
    {
        OnPropertyChanged(nameof(OperationLogListVisibility));
        OnPropertyChanged(nameof(OperationLogEmptyVisibility));
        OnPropertyChanged(nameof(OperationLogCountText));
    }

    public static string GetActionParameterHint(string? action)
    {
        return action switch
        {
            null or "" => "当前动作不需要参数。",
            "stop_flying" => "可填等待时间（毫秒），如 500；仅飞行移动下有效。",
            "force_tp" => "不需要参数；仅传送点有效，按当前坐标强制传送。",
            "nahida_collect" => "不需要参数；队伍需包含纳西妲。",
            "pick_around" => "可填圈数，数字越大拾取范围越大；默认 1。",
            "fight" => "不需要参数；会在此点执行自动战斗。",
            "up_down_grab_leaf" => "可填 up 或 down 控制寻找方向；留空自动尝试。",
            "hydro_collect" => "不需要参数；队伍需有水元素角色。",
            "electro_collect" => "不需要参数；队伍需有雷元素角色。",
            "anemo_collect" => "不需要参数；队伍需有风元素角色。",
            "pyro_collect" => "不需要参数；队伍需有火元素角色。",
            "combat_script" => "填写战斗策略脚本，例如 j,wait(0.2),j；也可从战斗策略列表选择。",
            "mining" => "可填 disablePickupAround 禁用挖矿后的自动拾取。",
            "linnea_mining" => "格式：射箭次数,大循环次数，如 3 或 3,10；默认 1,1。",
            "log_output" => "填写要输出到遮罩日志的内容。",
            "fishing" => "不需要参数。",
            "exit_and_relogin" => "不需要参数。",
            "wonderland_cycle" => "不需要参数。",
            "set_time" => "格式：HH:MM 或 HH:MM:true，如 06:00:true。",
            "use_gadget" => "可填 not_wait/no_wait/once 表示不等 CD；留空默认等待。",
            "pick_up_collect" => "可填角色名或 角色-动作，如 琴 或 琴-短E；留空自动识别。",
            _ => "动作参数。"
        };
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

public sealed class OperationLogEntryViewModel(string timeText, string message, string context)
{
    public string TimeText { get; } = timeText;

    public string Message { get; } = message;

    public string Context { get; } = context;
}

public sealed class ActionMenuGroupViewModel(string displayName, IEnumerable<MapEditorOption> options, bool canEdit = false)
{
    public string DisplayName { get; } = displayName;

    public IReadOnlyList<MapEditorOption> Options { get; } = options.ToList();

    public bool CanEdit { get; } = canEdit;
}

public sealed class ActionUsageEditorItemViewModel(string code, string displayName)
{
    public string Code { get; } = code;

    public string DisplayName { get; } = displayName;

    public string DetailText => code;
}

public sealed record MapEditorOption(
    string Code,
    string DisplayName,
    string ParameterHint = "")
{
    public bool HasParameterHint => !string.IsNullOrWhiteSpace(ParameterHint);
}

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

    [ObservableProperty]
    private bool _isRenaming;

    [ObservableProperty]
    private string _editingName = string.Empty;

    public PathingTask Task { get; private set; }

    public string PointCountText => $"{PointCount} 点";

    public System.Windows.Visibility RenameDisplayVisibility => IsRenaming
        ? System.Windows.Visibility.Collapsed
        : System.Windows.Visibility.Visible;

    public System.Windows.Visibility RenameEditorVisibility => IsRenaming
        ? System.Windows.Visibility.Visible
        : System.Windows.Visibility.Collapsed;

    public string MapDisplayText => string.IsNullOrWhiteSpace(MapName) ? nameof(MapTypes.Teyvat) : MapName;

    public string FileDisplayText => string.IsNullOrWhiteSpace(FilePath) ? "未保存" : Path.GetFileName(FilePath);

    public System.Windows.Visibility UnsavedIndicatorVisibility => string.IsNullOrWhiteSpace(FilePath)
        ? System.Windows.Visibility.Visible
        : System.Windows.Visibility.Collapsed;

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

    public void BeginRename()
    {
        EditingName = Name;
        IsRenaming = true;
    }

    public void EndRename()
    {
        IsRenaming = false;
    }

    partial void OnNameChanged(string value)
    {
        Task.Info ??= new PathingTaskInfo();
        Task.Info.Name = string.IsNullOrWhiteSpace(value) ? "未命名路线" : value.Trim();
    }

    partial void OnIsRenamingChanged(bool value)
    {
        OnPropertyChanged(nameof(RenameDisplayVisibility));
        OnPropertyChanged(nameof(RenameEditorVisibility));
    }

    partial void OnMapNameChanged(string value)
    {
        OnPropertyChanged(nameof(MapDisplayText));
    }

    partial void OnFilePathChanged(string value)
    {
        OnPropertyChanged(nameof(FileDisplayText));
        OnPropertyChanged(nameof(UnsavedIndicatorVisibility));
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

public partial class RecordAuthorViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _links = string.Empty;

    public RecordAuthorViewModel()
    {
    }

    public RecordAuthorViewModel(string name, string links)
    {
        Name = name;
        Links = links;
    }
}

public partial class CommonRecordAuthorViewModel(string name, string links, int count) : ObservableObject
{
    public string Name { get; } = name;

    public string Links { get; } = links;

    public int Count { get; } = count;

    public string DisplayText => Count > 0 ? $"{Name} ({Count})" : Name;

    public string DetailText => string.IsNullOrWhiteSpace(Links)
        ? $"{Count} 次"
        : $"{Count} 次 · {Links}";

    public string ParameterHint => DetailText;

    public override string ToString()
    {
        return DisplayText;
    }
}

public sealed class CommonRecordAuthorStoreItem
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("links")]
    public string Links { get; set; } = string.Empty;

    [JsonPropertyName("count")]
    public int Count { get; set; }
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
        ActionParameterHint = MapViewerViewModel.GetActionParameterHint(Action);
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
