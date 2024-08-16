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

namespace BetterGenshinImpact.GameTask.AutoPathing;

public class PathRecorder
{
    public Model.PathingTask PathingTask { get; set; } = new Model.PathingTask();
    public void Start()
    {
        TaskControl.Logger.LogInformation("开始路径点记录");
        var waypoint = new Model.Waypoint();
        var position = Navigation.GetPosition();
        position = MapCoordinate.Main2048ToGame(position);
        waypoint.X = position.X;
        waypoint.Y = position.Y;
        waypoint.WaypointType = Model.WaypointType.Teleport;
        waypoint.MoveType = Model.MoveType.Walk;
        PathingTask.Waypoints.Add(waypoint);
        TaskControl.Logger.LogInformation("已创建初始路径点({x},{y})", waypoint.X, waypoint.Y);
    }

    public void AddWaypoint()
    {
        var waypoint = new Model.Waypoint();
        var position = Navigation.GetPosition();
        position = MapCoordinate.Main2048ToGame(position);
        waypoint.X = position.X;
        waypoint.Y = position.Y;
        waypoint.WaypointType = Model.WaypointType.Transit;
        waypoint.MoveType = Model.MoveType.Walk;
        PathingTask.Waypoints.Add(waypoint);
        TaskControl.Logger.LogInformation("已添加途径点({x},{y})", waypoint.X, waypoint.Y);
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(PathingTask);
        File.WriteAllText(Global.Absolute($@"log\way\{DateTime.Now:yyyy-MM-dd HH：mm：ss：ffff}.json"), json);
    }

    public void Clear() {
        PathingTask = new Model.PathingTask();
    }
}
