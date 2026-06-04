using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
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
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Helpers.Extensions;
using BetterGenshinImpact.ViewModel.Pages;
using BetterGenshinImpact.View.Windows;
using Microsoft.Win32;
using WpfPoint = System.Windows.Point;

namespace BetterGenshinImpact.ViewModel.Windows;

/// <summary>
/// TODO 需要支持更多地图
/// </summary>
public partial class MapViewerViewModel : ObservableObject
{
    private static readonly JsonSerializerOptions JsonDisplayOptions = new(PathRecorder.JsonOptions)
    {
        WriteIndented = true
    };

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
    private bool _isTopmost = true;

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

    public ObservableCollection<RecordedWaypointViewModel> RecordedWaypoints { get; } = [];

    public ObservableCollection<MapEditorOption> WaypointTypeOptions { get; } = new(
        WaypointType.Values.Select(i => new MapEditorOption(i.Code, $"{i.Msg} ({i.Code})")));

    public ObservableCollection<MapEditorOption> MoveModeOptions { get; } = new(
        MoveModeEnum.Values.Select(i => new MapEditorOption(i.Code, $"{i.Msg} ({i.Code})")));

    public ObservableCollection<MapEditorOption> ActionOptions { get; } = new(
        new[] { new MapEditorOption(string.Empty, "无") }.Concat(
            ActionEnum.Values.Select(i => new MapEditorOption(i.Code, $"{i.Msg} ({i.Code})"))));

    public ObservableCollection<MapEditorOption> MapLayerOptions { get; } = new(
        Enum.GetValues<MapTypes>().Select(i => new MapEditorOption(i.ToString(), i.GetDescription())));

    public ObservableCollection<MapEditorOption> MapMatchMethodOptions { get; } = new(
    [
        new MapEditorOption("TemplateMatch", "模板匹配"),
        new MapEditorOption("SIFT", "SIFT")
    ]);

    public System.Windows.GridLength MapColumnWidth => new(1, System.Windows.GridUnitType.Star);

    public System.Windows.GridLength SplitterColumnWidth => IsSidePanelVisible ? new System.Windows.GridLength(6) : new System.Windows.GridLength(0);

    public System.Windows.GridLength SideColumnWidth => !IsSidePanelVisible
        ? new System.Windows.GridLength(0)
        : IsRecorderMode
            ? new System.Windows.GridLength(1, System.Windows.GridUnitType.Star)
            : new System.Windows.GridLength(360);

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

    public System.Windows.Visibility RecordedWaypointListVisibility => RecordedWaypoints.Count > 0
        ? System.Windows.Visibility.Visible
        : System.Windows.Visibility.Collapsed;

    public System.Windows.Visibility RecordedWaypointEmptyVisibility => RecordedWaypoints.Count == 0
        ? System.Windows.Visibility.Visible
        : System.Windows.Visibility.Collapsed;

    public string SidePanelToggleText => IsSidePanelVisible ? "隐藏信息" : "显示信息";

    public string FollowHotkeyText => string.IsNullOrWhiteSpace(TaskContext.Instance().Config.HotKeyConfig.MapViewerFollowHotkey)
        ? "未绑定"
        : TaskContext.Instance().Config.HotKeyConfig.MapViewerFollowHotkey;

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

    private List<Waypoint> _currentRoutePoints = [];

    private double _routeTotalDistance;

    private double _routeCompletedDistance;

    private bool _showRouteProgressAsPercent = true;

    public MapViewerViewModel(string mapName)
    {
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
        RecordedWaypoints.CollectionChanged += OnRecordedWaypointsCollectionChanged;
        WeakReferenceMessenger.Default.Register<PropertyChangedMessage<object>>(this, (sender, msg) =>
        {
            if (msg.PropertyName == "SendCurrentPosition")
            {
                if (!ShouldRefreshMapBitmap())
                {
                    return;
                }

                UIDispatcherHelper.BeginInvoke(() =>
                {
                    var point = (Point2f)msg.NewValue;
                    if (point.X == 0 && point.Y == 0)
                    {
                        return;
                    }

                    _lastPosition = point;
                    CurrentPositionText = FormatCurrentPosition(point);
                    LastRefreshText = $"刷新：{DateTime.Now:HH:mm:ss}";
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
            else if (msg.PropertyName == "SelectPathingTargetPosition" && msg.NewValue is Point2f targetPoint)
            {
                UIDispatcherHelper.BeginInvoke(() => HandleMapPointSelected(targetPoint));
            }
            else if (msg.PropertyName == "UpdateRecorderPathing" && sender is not MapViewerViewModel && msg.NewValue is PathingTask recorderTask)
            {
                UIDispatcherHelper.BeginInvoke(() => LoadRecorderTask(recorderTask));
            }
        });
    }

    private void OnRecordedWaypointsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(RecordedWaypointListVisibility));
        OnPropertyChanged(nameof(RecordedWaypointEmptyVisibility));
    }

    partial void OnIsFollowingCurrentChanged(bool value)
    {
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "SetMapFollowCurrent", new object(), value));
    }

    partial void OnIsRecorderModeChanged(bool value)
    {
        IsDebugMode = !value;
        if (!value)
        {
            IsJsonEditorMode = false;
        }

        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "SetMapViewerRecorderMode", new object(), value));
        RefreshLayoutProperties();
        if (!HasJsonEdits)
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

        RefreshLayoutProperties();
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
        if (value == null)
        {
            WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(
                this, "ClearSelectedRecorderWaypoint", new object(), new object()));
            return;
        }

        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(
            this, "SelectRecorderWaypointPosition", new object(), new Point2f((float)value.X, (float)value.Y)));
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
        PublishRecorderPath();
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
        SelectedTargetText = "目标点：点击地图选择";
        _selectedTargetPoint = null;
        ClearPathing();
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
        IsFollowingCurrent = true;
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "ResetMapView", new object(), new object()));
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
    private void ToggleViewSettings()
    {
        IsViewSettingsOpen = !IsViewSettingsOpen;
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
    private void SwitchToDebugMode()
    {
        if (!ConfirmJsonEditsBeforeLeavingRecorder(null))
        {
            return;
        }

        IsRecorderMode = false;
    }

    [RelayCommand]
    private void SwitchToRecorderMode()
    {
        IsRecorderMode = true;
    }

    [RelayCommand]
    private void SwitchToRecorderUiEditor()
    {
        if (!TryApplyJsonEdits(saveToFile: false, allowFilePicker: false, owner: null))
        {
            return;
        }

        IsJsonEditorMode = false;
    }

    [RelayCommand]
    private void SwitchToRecorderJsonEditor()
    {
        RefreshDebugJson();
        IsRecorderMode = true;
        IsJsonEditorMode = true;
        RecordStatusText = "录制器：JSON 编辑中";
    }

    [RelayCommand]
    private void ApplyJsonEdits()
    {
        TryApplyJsonEdits(saveToFile: false, allowFilePicker: false, owner: null);
    }

    [RelayCommand]
    private void RefreshJsonFromUi()
    {
        RefreshDebugJson();
        RecordStatusText = "录制器：已从表单重新生成 JSON";
    }

    [RelayCommand]
    private async Task StartRecording()
    {
        if (!ConfirmJsonEditsBeforeLeavingRecorder(null))
        {
            return;
        }

        IsRecorderMode = true;
        RecordStatusText = "录制器：启动中...";
        await Task.Run(() => PathRecorder.Instance.Start());
        LoadRecorderTask(PathRecorder.Instance.CurrentTask);
        RecordStatusText = $"录制器：录制中 / {RecordedWaypoints.Count} 点";
    }

    [RelayCommand]
    private async Task AddCurrentWaypoint()
    {
        if (!ConfirmJsonEditsBeforeLeavingRecorder(null))
        {
            return;
        }

        IsRecorderMode = true;
        RecordStatusText = "录制器：识别当前位置...";
        await Task.Run(() => PathRecorder.Instance.AddWaypoint());
        LoadRecorderTask(PathRecorder.Instance.CurrentTask);
        RecordStatusText = $"录制器：录制中 / {RecordedWaypoints.Count} 点";
    }

    [RelayCommand]
    private void SaveRecording()
    {
        if (!TryApplyJsonEdits(saveToFile: false, allowFilePicker: false, owner: null))
        {
            return;
        }

        var task = BuildRecordedTask();
        var filePath = ResolveRecordFilePath(false);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        task.SaveToFile(filePath);
        _recordFilePath = filePath;
        RecordFilePathText = $"文件：{filePath}";
        PathRecorder.Instance.ReplaceTask(task);
        LoadRecorderTask(task);
        RecordStatusText = $"录制器：已保存 / {Path.GetFileName(filePath)}";
    }

    [RelayCommand]
    private void SaveRecordingAs()
    {
        if (!TryApplyJsonEdits(saveToFile: false, allowFilePicker: false, owner: null))
        {
            return;
        }

        var filePath = ResolveRecordFilePath(true);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        var task = BuildRecordedTask();
        task.SaveToFile(filePath);
        _recordFilePath = filePath;
        RecordFilePathText = $"文件：{filePath}";
        PathRecorder.Instance.ReplaceTask(task);
        LoadRecorderTask(task);
        RecordStatusText = $"录制器：已另存 / {Path.GetFileName(filePath)}";
    }

    [RelayCommand]
    private void ImportRecording()
    {
        if (!ConfirmJsonEditsBeforeLeavingRecorder(null))
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "导入路线 JSON",
            Filter = "路线 JSON (*.json)|*.json|所有文件 (*.*)|*.*",
            InitialDirectory = Directory.Exists(MapPathingViewModel.PathJsonPath)
                ? MapPathingViewModel.PathJsonPath
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var task = PathingTask.BuildFromFilePath(dialog.FileName);
        MapName = string.IsNullOrWhiteSpace(task.Info.MapName) ? MapName : task.Info.MapName;
        _recordFilePath = dialog.FileName;
        RecordFilePathText = $"文件：{dialog.FileName}";
        LoadRecorderTask(task);
        IsRecorderMode = true;
        RecordStatusText = $"录制器：已导入 / {Path.GetFileName(dialog.FileName)}";
    }

    [RelayCommand]
    private void ImportAndMergeRecordings()
    {
        if (!ConfirmJsonEditsBeforeLeavingRecorder(null))
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "合并路线 JSON",
            Filter = "路线 JSON (*.json)|*.json|所有文件 (*.*)|*.*",
            Multiselect = true,
            InitialDirectory = Directory.Exists(MapPathingViewModel.PathJsonPath)
                ? MapPathingViewModel.PathJsonPath
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog() != true || dialog.FileNames.Length == 0)
        {
            return;
        }

        var appendCount = 0;
        if (RecordedWaypoints.Count == 0)
        {
            var firstTask = PathingTask.BuildFromFilePath(dialog.FileNames[0]);
            if (firstTask == null)
            {
                return;
            }

            MapName = string.IsNullOrWhiteSpace(firstTask.Info.MapName) ? MapName : firstTask.Info.MapName;
            LoadRecorderTask(firstTask);
            appendCount += firstTask.Positions.Count;
        }

        var startIndex = appendCount > 0 ? 1 : 0;
        for (var i = startIndex; i < dialog.FileNames.Length; i++)
        {
            var task = PathingTask.BuildFromFilePath(dialog.FileNames[i]);
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
        RecordStatusText = $"录制器：已合并 {dialog.FileNames.Length} 个文件 / {appendCount} 点";
        PublishRecorderPath();
    }

    [RelayCommand]
    private void SplitRecordingByTeleport()
    {
        if (!TryApplyJsonEdits(saveToFile: false, allowFilePicker: false, owner: null))
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

        var baseFilePath = ResolveRecordFilePath(string.IsNullOrWhiteSpace(_recordFilePath));
        if (string.IsNullOrWhiteSpace(baseFilePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(baseFilePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var baseName = Path.GetFileNameWithoutExtension(baseFilePath);
        for (var i = 0; i < groups.Count; i++)
        {
            var splitTask = ClonePathingTask(task);
            splitTask.Info.Name = $"{task.Info.Name}_{i + 1}";
            splitTask.Positions = groups[i];
            splitTask.SaveToFile(Path.Combine(directory, $"{baseName}_{i + 1}.json"));
        }

        RecordStatusText = $"录制器：已按传送点拆分为 {groups.Count} 个文件";
    }

    [RelayCommand]
    private async Task RunRecording()
    {
        if (!ConfirmJsonEditsBeforeLeavingRecorder(null))
        {
            return;
        }

        await RunRecordedTaskFromIndex(0);
    }

    [RelayCommand]
    private async Task RunRecordingFromWaypoint(RecordedWaypointViewModel? waypoint)
    {
        if (!ConfirmJsonEditsBeforeLeavingRecorder(null))
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
        if (!ConfirmJsonEditsBeforeLeavingRecorder(null))
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
        RefreshDebugJson();
        PublishRecorderPath();
    }

    [RelayCommand]
    private void AddPathPointFromTarget()
    {
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

    [RelayCommand]
    private void ToggleWaypointLock(RecordedWaypointViewModel? waypoint)
    {
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
    private void ClearRecordedWaypoints()
    {
        var result = ThemedMessageBox.Warning(
            "确定要清除所有路线点吗？此操作可以用撤销恢复。",
            "清空路线点",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxResult.Cancel);
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
        RecordAuthorName = DefaultRecordAuthorName.Trim();
        RecordAuthorLinks = DefaultRecordAuthorLinks.Trim();
        PublishRecorderPath();
    }

    [RelayCommand]
    private void UndoRecorderEdit()
    {
        if (_recorderHistoryIndex <= 0)
        {
            return;
        }

        RestoreRecorderHistory(_recorderHistoryIndex - 1);
    }

    [RelayCommand]
    private void RedoRecorderEdit()
    {
        if (_recorderHistoryIndex >= _recorderHistory.Count - 1)
        {
            return;
        }

        RestoreRecorderHistory(_recorderHistoryIndex + 1);
    }

    [RelayCommand]
    private void DeleteRecordedWaypoint(RecordedWaypointViewModel? waypoint)
    {
        waypoint ??= SelectedRecordedWaypoint;
        if (waypoint == null)
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
            (float)Math.Round(_lastClipGlobalRect.X + sourceX, 1),
            (float)Math.Round(_lastClipGlobalRect.Y + sourceY, 1));

        SelectedTargetText = $"目标点：{selectedPoint.X:F1}, {selectedPoint.Y:F1}";
        _selectedTargetPoint = selectedPoint;
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "SelectPathingTargetPosition", new object(), selectedPoint));
    }

    private void HandleMapPointSelected(Point2f targetPoint)
    {
        _selectedTargetPoint = targetPoint;
        SelectedTargetText = $"目标点：{targetPoint.X:F1}, {targetPoint.Y:F1}";
        if (!IsRecorderMode)
        {
            return;
        }

        if (UpdateSelectedPointOnMapClick && SelectedRecordedWaypoint != null)
        {
            SelectedRecordedWaypoint.X = Math.Round(targetPoint.X, 1);
            SelectedRecordedWaypoint.Y = Math.Round(targetPoint.Y, 1);
            RecordStatusText = $"录制器：已更新第 {SelectedRecordedWaypoint.Index} 点";
            PublishRecorderPath();
            return;
        }

        AddRecordedWaypoint(new Waypoint
        {
            X = Math.Round(targetPoint.X, 1),
            Y = Math.Round(targetPoint.Y, 1),
            Type = WaypointType.Path.Code,
            MoveMode = MoveModeEnum.Walk.Code
        });
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

    private string? ResolveRecordFilePath(bool forcePicker)
    {
        if (!forcePicker && !string.IsNullOrWhiteSpace(_recordFilePath))
        {
            return _recordFilePath;
        }

        var safeName = string.IsNullOrWhiteSpace(RecordFileName)
            ? "未命名路线"
            : string.Join("_", RecordFileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        var dialog = new SaveFileDialog
        {
            Title = "保存路线 JSON",
            Filter = "路线 JSON (*.json)|*.json|所有文件 (*.*)|*.*",
            FileName = $"{safeName}.json",
            InitialDirectory = Directory.Exists(MapPathingViewModel.PathJsonPath)
                ? MapPathingViewModel.PathJsonPath
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private void LoadRecorderTask(PathingTask task)
    {
        var wasRestoring = _isRestoringRecorderHistory;
        _isRestoringRecorderHistory = true;
        _recordTaskTemplate = task;
        task.Info ??= new PathingTaskInfo();
        task.Positions ??= [];
        RecordedWaypoints.Clear();
        RecordFileName = string.IsNullOrWhiteSpace(task.Info.Name) ? "未命名路线" : task.Info.Name;
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
            if (e.PropertyName == nameof(RecordedWaypointViewModel.IsLocked))
            {
                return;
            }

            PublishRecorderPath();
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
        var safeName = string.IsNullOrWhiteSpace(RecordFileName) ? "未命名路线" : RecordFileName.Trim();
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

    public bool ConfirmJsonEditsBeforeLeavingRecorder(System.Windows.Window? owner)
    {
        if (!HasJsonEdits)
        {
            return true;
        }

        if (AutoSaveJsonEdits)
        {
            return TryApplyJsonEdits(saveToFile: true, allowFilePicker: true, owner);
        }

        var result = ThemedMessageBox.Show(
            "JSON 已修改，是否保存并应用？\n选择“否”将丢弃本次 JSON 编辑，选择“取消”留在当前界面。",
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

        return TryApplyJsonEdits(saveToFile: true, allowFilePicker: true, owner);
    }

    private void PublishRecorderPath()
    {
        if (_isRestoringRecorderHistory)
        {
            return;
        }

        RefreshDebugJson();
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(
            this,
            "UpdateRecorderPathing",
            new object(),
            BuildRecordedTask()));
        SnapshotRecorderState();
    }

    private void UpdateTaskSummary(PathingTask pathingTask)
    {
        var points = pathingTask.Positions ?? [];
        _currentRoutePoints = points.ToList();
        _routeTotalDistance = EstimateDistance(_currentRoutePoints);
        _routeCompletedDistance = 0;
        RouteProgressValue = 0;
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
            DebugJsonText = JsonSerializer.Serialize(task ?? BuildRecordedTask(), JsonDisplayOptions);
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
        PathRecorder.Instance.ReplaceTask(task);

        if (saveToFile)
        {
            var filePath = ResolveRecordFilePath(!allowFilePicker || string.IsNullOrWhiteSpace(_recordFilePath));
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            task.SaveToFile(filePath);
            _recordFilePath = filePath;
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

    private void SnapshotRecorderState()
    {
        if (_isRestoringRecorderHistory)
        {
            return;
        }

        var snapshot = JsonSerializer.Serialize(BuildRecordedTask(), JsonDisplayOptions);
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
            PathRecorder.Instance.ReplaceTask(task);
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
        var json = JsonSerializer.Serialize(task, PathRecorder.JsonOptions);
        return PathingTask.BuildFromJson(json);
    }

    private void RefreshLayoutProperties()
    {
        OnPropertyChanged(nameof(MapColumnWidth));
        OnPropertyChanged(nameof(SplitterColumnWidth));
        OnPropertyChanged(nameof(SideColumnWidth));
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
            : $"{GetWaypointGameX(waypoint):F1}, {GetWaypointGameY(waypoint):F1}";
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
            RefreshRouteProgressText();
            return;
        }

        var currentIndex = FindRouteWaypointIndex(waypoint);
        if (currentIndex < 0)
        {
            return;
        }

        _routeCompletedDistance = EstimateDistance(_currentRoutePoints.Take(currentIndex).ToList());
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
            RouteProgressText = _showRouteProgressAsPercent ? "0%" : "0 / 0";
            return;
        }

        RouteProgressText = _showRouteProgressAsPercent
            ? $"{RouteProgressValue:F0}%"
            : $"{_routeCompletedDistance:F1} / {_routeTotalDistance:F1}";
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
                return $"当前位置：{point.X:F1}, {point.Y:F1}";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }

        return $"当前位置：{imagePoint.X:F1}, {imagePoint.Y:F1}";
    }

    private static string FormatAction(string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return "-";
        }

        var message = ActionEnum.GetMsgByCode(action);
        return string.Equals(message, action, StringComparison.OrdinalIgnoreCase)
            ? action
            : $"{message} ({action})";
    }

    private static string FormatMoveMode(string? moveMode)
    {
        if (string.IsNullOrWhiteSpace(moveMode))
        {
            return "-";
        }

        var message = MoveModeEnum.GetMsgByCode(moveMode);
        return string.Equals(message, moveMode, StringComparison.OrdinalIgnoreCase)
            ? moveMode
            : $"{message} ({moveMode})";
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
            DrawLine(taskMat, startPoint, endPoint, lineColor, 1);
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

public sealed record MapEditorOption(string Code, string DisplayName);

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
    private bool _isLocked;

    [ObservableProperty]
    private string _misidentificationTypesText = "unrecognized";

    [ObservableProperty]
    private string _misidentificationHandlingMode = "previousDetectedPoint";

    [ObservableProperty]
    private int _misidentificationArrivalTime;

    [ObservableProperty]
    private string _actionParameterHint = "动作参数";

    public string CoordinateText => $"{X:F1}, {Y:F1}";

    public RecordedWaypointViewModel(Waypoint waypoint)
    {
        _extensionData = waypoint.ExtensionData;
        _extParamsExtensionData = waypoint.PointExtParams?.ExtensionData;
        _misidentificationExtensionData = waypoint.PointExtParams?.Misidentification?.ExtensionData;
        X = Math.Round(waypoint.X, 1);
        Y = Math.Round(waypoint.Y, 1);
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
    }

    partial void OnYChanged(double value)
    {
        OnPropertyChanged(nameof(CoordinateText));
    }

    partial void OnActionChanged(string? value)
    {
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
            X = X,
            Y = Y,
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
