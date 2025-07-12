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
        Navigation.WarmUp();
        _pathingTask = new PathingTask();
        TaskControl.Logger.LogInformation("开始路径点记录");
        if (GetMapName() == nameof(MapTypes.Teyvat))
        {
            TaskControl.Logger.LogInformation("如果需要切换其他地图，请在 {Msg} 中切换", "地图追踪——开发者工具");
        }

        var waypoint = new Waypoint();
        var screen = TaskControl.CaptureToRectArea();
        var position = Navigation.GetPositionStable(screen, GetMapName());
        if (position == default)
        {
            TaskControl.Logger.LogWarning("未识别到当前位置！");
            return;
        }

        position = MapManager.GetMap(GetMapName()).ConvertImageCoordinatesToGenshinMapCoordinates(position);
        waypoint.X = position.X;
        waypoint.Y = position.Y;
        waypoint.Type = WaypointType.Teleport.Code;
        waypoint.MoveMode = MoveModeEnum.Walk.Code;
        _pathingTask.Positions.Add(waypoint);
        if (_webWindow == null)
        {
            TaskControl.Logger.LogInformation("已创建初始路径点({x},{y})", waypoint.X, waypoint.Y);
        }
        else
        {
            TaskControl.Logger.LogInformation("已添加途径点({x},{y})", waypoint.X, waypoint.Y);
            AddPosToEditor(waypoint.X, waypoint.Y);
        }
    }

    public void AddWaypoint(string waypointType = "")
    {
        Waypoint waypoint = new();
        var screen = TaskControl.CaptureToRectArea();
        var position = Navigation.GetPositionStable(screen, GetMapName());
        position = MapManager.GetMap(GetMapName()).ConvertImageCoordinatesToGenshinMapCoordinates(position);
        if (position == default)
        {
            TaskControl.Logger.LogWarning("未识别到当前位置！");
            return;
        }

        waypoint.X = position.X;
        waypoint.Y = position.Y;
        waypoint.Type = string.IsNullOrEmpty(waypointType) ? WaypointType.Path.Code : waypointType;
        _pathingTask.Positions.Add(waypoint);
        TaskControl.Logger.LogInformation("已添加途径点({x},{y})", waypoint.X, waypoint.Y);
        AddPosToEditor(waypoint.X, waypoint.Y);
    }

    public void Save()
    {
        if (_webWindow == null)
        {
            _pathingTask.Info = new PathingTaskInfo
            {
                Name = "未命名路线",
                Type = PathingTaskType.Collect.Code,
                MapName = GetMapName(),
                BgiVersion = Global.Version
            };
            var name = $@"{DateTime.Now:yyyyMMdd_HHmmss}.json";
            _pathingTask.SaveToFile(Path.Combine(MapPathingViewModel.PathJsonPath, name));
            TaskControl.Logger.LogInformation("录制编辑器未打开，直接保存路径点记录:{Name}", name);
        }
        else
        {
            TaskControl.Logger.LogInformation("路径点记录结束，请在录制编辑器中查看并编辑结果");
            TaskControl.Logger.LogInformation("如果要重新录制新的路径，请在录制编辑器中删除已有路径或创建新的路径");
            TaskControl.Logger.LogInformation("修改完毕后请务必记得导出路径！");
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
                Title = "地图路径点编辑器",
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