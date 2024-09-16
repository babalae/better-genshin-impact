using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Map;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.GameTask.AutoPathing;

public class PathRecorder
{
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
        _pathingTask = new PathingTask();
        TaskControl.Logger.LogInformation("开始路径点记录");
        var waypoint = new Waypoint();
        var screen = TaskControl.CaptureToRectArea();
        var position = Navigation.GetPosition(screen);
        position = MapCoordinate.Main2048ToGame(position);
        waypoint.X = position.X;
        waypoint.Y = position.Y;
        waypoint.Type = WaypointType.Teleport.Code;
        waypoint.MoveMode = MoveModeEnum.Walk.Code;
        _pathingTask.Positions.Add(waypoint);
        TaskControl.Logger.LogInformation("已创建初始路径点({x},{y})", waypoint.X, waypoint.Y);
    }

    public void AddWaypoint(string waypointType = "")
    {
        Waypoint waypoint = new();
        var screen = TaskControl.CaptureToRectArea();
        var position = Navigation.GetPosition(screen);
        position = MapCoordinate.Main2048ToGame(position);
        waypoint.X = position.X;
        waypoint.Y = position.Y;
        waypoint.Type = string.IsNullOrEmpty(waypointType) ? WaypointType.Path.Code : waypointType;
        // var motionStatus = Bv.GetMotionStatus(screen);
        // switch (motionStatus)
        // {
        //     case MotionStatus.Fly:
        //         waypoint.MoveStatus = MoveStatusType.Fly.Code;
        //         break;
        //
        //     case MotionStatus.Climb:
        //         waypoint.MoveStatus = MoveStatusType.Jump.Code;
        //         break;
        //
        //     default:
        //         waypoint.MoveStatus = MoveStatusType.Walk.Code;
        //         break;
        // }
        _pathingTask.Positions.Add(waypoint);
        TaskControl.Logger.LogInformation("已添加途径点({x},{y})", waypoint.X, waypoint.Y);
    }

    public void Save()
    {
        var name = $@"{DateTime.Now:yyyyMMdd_HHmmss}.json";
        _pathingTask.SaveToFile(Path.Combine(MapPathingViewModel.PathJsonPath, name));
        TaskControl.Logger.LogInformation("已保存路径点记录:{Name}", name);
    }
}
