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
using System.Web;
using BetterGenshinImpact.Core.Script.WebView;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Model;
using Microsoft.Web.WebView2.Core;

namespace BetterGenshinImpact.GameTask.AutoPathing;

/// <summary>
/// Singleton instance responsible for recording pathing trajectories and translating spatial telemetry to editable formats.
/// 负责记录寻路轨迹并将空间遥测数据转换为可编辑格式的单例实例。
/// </summary>
public class PathRecorder : Singleton<PathRecorder>
{
    private WebpageWindow? _webWindow;

    /// <summary>
    /// Default serialization format bounds ignoring null payloads strictly mapped to lowercase snake_case schemas.
    /// 默认的序列化格式边界，严格忽略 null 负载并映射为小写的 snake_case 格式。
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, 
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private PathingTask _pathingTask = new();

    /// <summary>
    /// Retrieves the current diagnostic record map classification scope setting bounding to dev environments.
    /// 检索当前用于记录测绘数据的地图分类作用域设置，锚定于开发环境。
    /// </summary>
    /// <returns>Geographic system map string. 地理系统地图名称字符串。</returns>
    private string GetMapName()
    {
        var mapName = TaskContext.Instance()?.Config?.DevConfig?.RecordMapName;
        if (string.IsNullOrEmpty(mapName))
        {
            mapName = nameof(MapTypes.Teyvat);
        }

        return mapName;
    }

    /// <summary>
    /// Originates a recording trajectory bootstrapping spatial engines with zero-point teleport contexts.
    /// 开启录制轨迹，使用初始传送锚点及零点计算环境来引导空间追踪引擎。
    /// </summary>
    public void Start()
    {
        var matchingMethod = TaskContext.Instance()?.Config?.PathingConditionConfig?.MapMatchingMethod;
        if (string.IsNullOrEmpty(matchingMethod)) return;

        Navigation.WarmUp(matchingMethod);
        _pathingTask = new PathingTask();
        TaskControl.Logger?.LogInformation("开始路径点记录");
        
        if (GetMapName() == nameof(MapTypes.Teyvat))
        {
            TaskControl.Logger?.LogInformation("如果需要切换其他地图，请在 {Msg} 中切换", "地图追踪——开发者工具");
        }

        var waypoint = new Waypoint();
        var screen = TaskControl.CaptureToRectArea();
        if (screen == null) return;

        var position = Navigation.GetPositionStable(screen, GetMapName(), matchingMethod);
        var mapBase = MapManager.GetMap(GetMapName(), matchingMethod);
        if (mapBase == null) return;
        
        var nullablePosition = mapBase.ConvertImageCoordinatesToGenshinMapCoordinates(position);
        
        if (nullablePosition == null)
        {
            TaskControl.Logger?.LogWarning("未识别到当前位置！");
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
            TaskControl.Logger?.LogInformation("已创建初始路径点({x},{y})", waypoint.X, waypoint.Y);
        }
        else
        {
            TaskControl.Logger?.LogInformation("已添加途径点({x},{y})", waypoint.X, waypoint.Y);
            AddPosToEditor(waypoint.X, waypoint.Y);
        }
    }

    /// <summary>
    /// Pushes a new coordinate vector capturing visual reality and aligning mapped contexts to telemetry JSON storage.
    /// 捕捉当前位置以映射关联遥测信息并将带有坐标向量的新节点推送入库留存。
    /// </summary>
    /// <param name="waypointType">The intrinsic functional descriptor classifying logical node interactions. 对逻辑节点交互进行分类的内在功能描述符。</param>
    public void AddWaypoint(string waypointType = "")
    {
        Waypoint waypoint = new();
        var screen = TaskControl.CaptureToRectArea();
        if (screen == null) return;

        var matchingMethod = TaskContext.Instance()?.Config?.PathingConditionConfig?.MapMatchingMethod;
        if (string.IsNullOrEmpty(matchingMethod)) return;

        var position = Navigation.GetPositionStable(screen, GetMapName(), matchingMethod);
        var mapBase = MapManager.GetMap(GetMapName(), matchingMethod);
        if (mapBase == null) return;
        
        var nullablePosition = mapBase.ConvertImageCoordinatesToGenshinMapCoordinates(position);
        
        if (nullablePosition == null)
        {
            TaskControl.Logger?.LogWarning("未识别到当前位置！");
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
        TaskControl.Logger?.LogInformation("已添加途径点({x},{y})", waypoint.X, waypoint.Y);
        AddPosToEditor(waypoint.X, waypoint.Y);
    }

    /// <summary>
    /// Flushes accumulated trajectory lists into persistent storage or delegating post-process rendering tasks to WebView frameworks.
    /// 将累加产生的移动轨迹列表刷新到底层持久性储存中或是指派WebView框架做后期重整。
    /// </summary>
    public void Save()
    {
        if (_webWindow == null)
        {
            var matchingMethod = TaskContext.Instance()?.Config?.PathingConditionConfig?.MapMatchingMethod;
            if (string.IsNullOrEmpty(matchingMethod)) matchingMethod = "Default";

            _pathingTask.Info = new PathingTaskInfo
            {
                Name = "未命名路线",
                Type = PathingTaskType.Collect.Code,
                MapName = GetMapName(),
                MapMatchMethod = matchingMethod,
                BgiVersion = Global.Version
            };
            var name = $@"{DateTime.Now:yyyyMMdd_HHmmss}.json";
            
            if (MapPathingViewModel.PathJsonPath != null)
            {
                _pathingTask.SaveToFile(Path.Combine(MapPathingViewModel.PathJsonPath, name));
            }
            
            TaskControl.Logger?.LogInformation("录制编辑器未打开，直接保存路径点记录:{Name}", name);
        }
        else
        {
            TaskControl.Logger?.LogInformation("路径点记录结束，请在录制编辑器中查看并编辑结果");
            TaskControl.Logger?.LogInformation("如果要重新录制新的路径，请在录制编辑器中删除已有路径或创建新的路径");
            TaskControl.Logger?.LogInformation("修改完毕后请务必记得导出路径！");
        }
    }

    /// <summary>
    /// Invokes internal Javascript bridging to instantly notify web view editor about localized point.
    /// 调用底层 Javascript 通信桥梁，通知网页视图编辑器刷新当前即时收集的节点。
    /// </summary>
    /// <param name="x">Geographic plane coordinate X. 地理平面横坐标。</param>
    /// <param name="y">Geographic plane coordinate Y. 地理平面纵坐标。</param>
    private void AddPosToEditor(double x, double y)
    {
        if (_webWindow?.WebView == null) return;
        
        UIDispatcherHelper.Invoke(() => 
        { 
            try 
            {
                _webWindow.WebView.ExecuteScriptAsync($"addNewPoint({x},{y})"); 
            }
            catch (Exception ex)
            {
                // UI 线程中的非致命异常，记录日志后继续，以防直接导致应用程序崩溃
                TaskControl.Logger?.LogError($"执行WebView前端脚本失败: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Opens the specialized HTML/JS mapped web editor to manipulate recorded topological lines actively.
    /// 打开具备专有 HTML/JS 功能的网页编辑器用作直观的动态重加工修改录制的拓扑线。
    /// </summary>
    /// <param name="mapName">Bound Teyvat realm domain mapping config identifier. 指向匹配的当前所处提瓦特大陆板块配置ID。</param>
    public void OpenEditorInWebView(string mapName = "Teyvat")
    {
        if (_webWindow is not { IsVisible: true })
        {
            _webWindow = new WebpageWindow
            {
                Title = "地图路径点编辑器",
                Width = 1366,
                Height = 768,
                WindowState = WindowState.Maximized
            };
            
            _webWindow.Closed += (s, e) => _webWindow = null;
            
            if (_webWindow.Panel != null)
            {
                _webWindow.Panel.DownloadFolderPath = MapPathingViewModel.PathJsonPath;
            }

            var htmlPath = Global.Absolute(@"Assets\Map\Editor\index.html");
            if (htmlPath == null) return;

            var uri = new UriBuilder(htmlPath);
            var query = HttpUtility.ParseQueryString(string.Empty);
            query["map"] = mapName;
            uri.Query = query.ToString() ?? string.Empty;
            
            _webWindow.NavigateToFile(uri.ToString());
            
            if (_webWindow.Panel != null)
            {
                _webWindow.Panel.OnWebViewInitializedAction = () =>
                {
                    if (_webWindow.Panel.WebView?.CoreWebView2 != null)
                    {
                        string userPath = Global.Absolute("User/AutoPathing") ?? "User/AutoPathing";
                        _webWindow.Panel.WebView.CoreWebView2.AddHostObjectToScript("mapEditorWebBridge", new MapEditorWebBridge());
                        _webWindow.Panel.WebView.CoreWebView2.AddHostObjectToScript("fileAccessBridge", new FileAccessBridge(userPath));
                    }
                };
            }
            
            _webWindow.Show();
        }
        else
        {
            _webWindow.Activate();
        }
    }
}