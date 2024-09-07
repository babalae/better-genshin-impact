using System;
using System.Collections.Generic;
using System.Text;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Map;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using BetterGenshinImpact.Core.Config;
using System.IO;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.Common.BgiVision;

namespace BetterGenshinImpact.GameTask.AutoPathing;

public class PathRecorder
{
    public PathingTask PathingTask { get; set; } = new();

    public void Start()
    {
        TaskControl.Logger.LogInformation("开始路径点记录");
        var waypoint = new Waypoint();
        var screen = TaskControl.CaptureToRectArea();
        var position = Navigation.GetPosition(screen);
        position = MapCoordinate.Main2048ToGame(position);
        waypoint.X = position.X;
        waypoint.Y = position.Y;
        waypoint.Type = WaypointType.Teleport.Code;
        waypoint.MoveMode = MoveModeEnum.Walk.Code;
        PathingTask.Waypoints.Add(waypoint);
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
        PathingTask.Waypoints.Add(waypoint);
        TaskControl.Logger.LogInformation("已添加途径点({x},{y})", waypoint.X, waypoint.Y);
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(PathingTask);
        File.WriteAllText(Global.Absolute($@"log\way\{DateTime.Now:yyyy-MM-dd HH：mm：ss：ffff}.json"), json);
    }

    public void Clear()
    {
        PathingTask = new PathingTask();
    }
}
