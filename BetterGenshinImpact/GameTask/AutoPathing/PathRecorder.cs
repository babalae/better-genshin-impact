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
        waypoint.WaypointType = WaypointType.Teleport;
        waypoint.MoveType = MoveType.Walk;
        PathingTask.Waypoints.Add(waypoint);
        TaskControl.Logger.LogInformation("已创建初始路径点({x},{y})", waypoint.X, waypoint.Y);
    }

    public void AddWaypoint(WaypointType waypointType = WaypointType.Transit)
    {
        var waypoint = new Waypoint();
        var screen = TaskControl.CaptureToRectArea();
        var position = Navigation.GetPosition(screen);
        position = MapCoordinate.Main2048ToGame(position);
        waypoint.X = position.X;
        waypoint.Y = position.Y;
        waypoint.WaypointType = waypointType;
        var motionStatus = Bv.GetMotionStatus(screen);
        switch (motionStatus)
        {
            case MotionStatus.Fly:
                waypoint.MoveType = MoveType.Fly;
                break;

            case MotionStatus.Climb:
                waypoint.MoveType = MoveType.Jump;
                break;

            default:
                waypoint.MoveType = MoveType.Walk;
                break;
        }
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
