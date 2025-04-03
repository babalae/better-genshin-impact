﻿using BetterGenshinImpact.Core.Config;
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

    public void Start()
    {
        Navigation.WarmUp();
        _pathingTask = new PathingTask();
        TaskControl.Logger.LogInformation("开始路径点记录");
        var waypoint = new Waypoint();
        var screen = TaskControl.CaptureToRectArea();
        var position = Navigation.GetPositionStable(screen);
        position = MapCoordinate.Main2048ToGame(position);
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
        var position = Navigation.GetPositionStable(screen);
        position = MapCoordinate.Main2048ToGame(position);
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

    public void AddPosToEditor(double x, double y)
    {
        if (_webWindow != null)
        {
            _webWindow.WebView.ExecuteScriptAsync($"addNewPoint({x},{y})");
        }
    }

    public void OpenEditorInWebView()
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
            _webWindow.NavigateToFile(Global.Absolute(@"Assets\Map\Editor\index.html"));
            _webWindow.Show();
        }
        else
        {
            _webWindow.Activate();
        }
    }
}
