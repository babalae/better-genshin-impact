using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Map;
using BetterGenshinImpact.View.Controls.Webview;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using BetterGenshinImpact.Core.Script.WebView;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Model;
using Microsoft.Web.WebView2.Core;

namespace BetterGenshinImpact.GameTask.AutoPathing;

public class PathRecorder : Singleton<PathRecorder>
{
    private WebpageWindow? _webWindow;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, // 下划线
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private PathingTask _pathingTask = new();


    private string GetMapName()
    {
        var mapName = TaskContext.Instance().Config.DevConfig.RecordMapName;
        if (string.IsNullOrEmpty(mapName))
        {
            mapName = nameof(MapTypes.Teyvat);
        }

        return mapName;
    }

    public void Start()
    {
        var matchingMethod = TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod;
        Navigation.WarmUp(matchingMethod);
        _pathingTask = new PathingTask();
        TaskControl.Logger.LogInformation(Lang.S["GameTask_11063_51f752"]);
        if (GetMapName() == nameof(MapTypes.Teyvat))
        {
            TaskControl.Logger.LogInformation(Lang.S["GameTask_11061_a54b78"], "地图追踪——开发者工具");
        }

        var waypoint = new Waypoint();
        var screen = TaskControl.CaptureToRectArea();
        var position = Navigation.GetPositionStable(screen, GetMapName(), matchingMethod);
        var nullablePosition = MapManager.GetMap(GetMapName(), matchingMethod).ConvertImageCoordinatesToGenshinMapCoordinates(position);
        if (nullablePosition == null)
        {
            TaskControl.Logger.LogWarning(Lang.S["GameTask_11059_50630b"]);
            return;
        }
        else
        {
            position = nullablePosition.Value;
        }
        waypoint.X = position.X;
        waypoint.Y = position.Y;
        waypoint.Type = WaypointType.Teleport.Code;
        waypoint.MoveMode = MoveModeEnum.Walk.Code;
        _pathingTask.Positions.Add(waypoint);
        if (_webWindow == null)
        {
            TaskControl.Logger.LogInformation(Lang.S["GameTask_11060_1f57da"], waypoint.X, waypoint.Y);
        }
        else
        {
            TaskControl.Logger.LogInformation(Lang.S["GameTask_11058_e3fcb1"], waypoint.X, waypoint.Y);
            AddPosToEditor(waypoint.X, waypoint.Y);
        }
    }

    public void AddWaypoint(string waypointType = "")
    {
        Waypoint waypoint = new();
        var screen = TaskControl.CaptureToRectArea();
        var matchingMethod = TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod;
        var position = Navigation.GetPositionStable(screen, GetMapName(), matchingMethod);
        var nullablePosition = MapManager.GetMap(GetMapName(), matchingMethod).ConvertImageCoordinatesToGenshinMapCoordinates(position);
        if (nullablePosition == null)
        {
            TaskControl.Logger.LogWarning(Lang.S["GameTask_11059_50630b"]);
            return;
        }
        else
        {
            position = nullablePosition.Value;
        }

        waypoint.X = position.X;
        waypoint.Y = position.Y;
        waypoint.Type = string.IsNullOrEmpty(waypointType) ? WaypointType.Path.Code : waypointType;
        _pathingTask.Positions.Add(waypoint);
        TaskControl.Logger.LogInformation(Lang.S["GameTask_11058_e3fcb1"], waypoint.X, waypoint.Y);
        AddPosToEditor(waypoint.X, waypoint.Y);
    }

    public void Save()
    {
        if (_webWindow == null)
        {
            var matchingMethod = TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod;
            _pathingTask.Info = new PathingTaskInfo
            {
                Name = Lang.S["GameTask_11057_b61afc"],
                Type = PathingTaskType.Collect.Code,
                MapName = GetMapName(),
                MapMatchMethod = matchingMethod,
                BgiVersion = Global.Version
            };
            var name = $@"{DateTime.Now:yyyyMMdd_HHmmss}.json";
            _pathingTask.SaveToFile(Path.Combine(MapPathingViewModel.PathJsonPath, name));
            TaskControl.Logger.LogInformation(Lang.S["GameTask_11056_713e7a"], name);
        }
        else
        {
            TaskControl.Logger.LogInformation(Lang.S["GameTask_11055_673b56"]);
            TaskControl.Logger.LogInformation(Lang.S["GameTask_11054_1b6a69"]);
            TaskControl.Logger.LogInformation(Lang.S["GameTask_11053_edd114"]);
        }
    }

    private void AddPosToEditor(double x, double y)
    {
        if (_webWindow != null)
        {
            UIDispatcherHelper.Invoke(() => { _webWindow.WebView.ExecuteScriptAsync($"addNewPoint({x},{y})"); });
        }
    }

    public void OpenEditorInWebView(string mapName = "Teyvat")
    {
        if (_webWindow is not { IsVisible: true })
        {
            _webWindow = new WebpageWindow
            {
                Title = Lang.S["GameTask_11052_c764b6"],
                Width = 1366,
                Height = 768,
                // Owner = Application.Current.MainWindow,
                WindowState = WindowState.Maximized
            };
            _webWindow.Closed += (s, e) => _webWindow = null;
            _webWindow.Panel!.DownloadFolderPath = MapPathingViewModel.PathJsonPath;

            var htmlPath = Global.Absolute(@"Assets\Map\Editor\index.html");
            var uri = new UriBuilder(htmlPath);
            var query = System.Web.HttpUtility.ParseQueryString(string.Empty);
            query["map"] = mapName;
            uri.Query = query.ToString();
            _webWindow.NavigateToFile(uri.ToString());
            _webWindow.Panel!.OnWebViewInitializedAction = () =>
            {
                _webWindow.Panel!.WebView.CoreWebView2.AddHostObjectToScript("mapEditorWebBridge", new MapEditorWebBridge());
                _webWindow.Panel!.WebView.CoreWebView2.AddHostObjectToScript("fileAccessBridge", new FileAccessBridge(Global.Absolute("User/AutoPathing")));
            };
            _webWindow.Show();
        }
        else
        {
            _webWindow.Activate();
        }
    }
}