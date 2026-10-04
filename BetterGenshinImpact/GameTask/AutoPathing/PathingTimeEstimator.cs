using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;

namespace BetterGenshinImpact.GameTask.AutoPathing;

/// <summary>
/// 地图追踪（Pathing）JSON 的耗时估算。
/// 估算值 = 相邻点位距离 / 移动方式速度 + 点位类型开销 + 动作开销 + action_params 中的显式等待。
/// 各项参数按实测任务标定，只用于展示量级正确的剩余时间，不是精确预测。
/// </summary>
public static partial class PathingTimeEstimator
{
    /// <summary>
    /// 移动速度，单位与地图追踪 JSON 中的坐标一致（单位/秒）
    /// </summary>
    private static readonly Dictionary<string, double> MoveSpeeds = new()
    {
        [MoveModeEnum.Walk.Code] = 4.0,
        [MoveModeEnum.Run.Code] = 5.0,
        [MoveModeEnum.Dash.Code] = 5.5,
        [MoveModeEnum.Fly.Code] = 4.0,
        [MoveModeEnum.Jump.Code] = 2.5,
        [MoveModeEnum.Climb.Code] = 2.0,
        [MoveModeEnum.Swim.Code] = 3.0,
    };

    /// <summary>
    /// 到达点位后固定消耗的时间（秒），如传送、转向、目标点交互
    /// </summary>
    private static readonly Dictionary<string, double> WaypointOverheads = new()
    {
        [WaypointType.Path.Code] = 1.0,
        [WaypointType.Target.Code] = 2.5,
        [WaypointType.Teleport.Code] = 8.0,
        [WaypointType.Orientation.Code] = 2.0,
    };

    /// <summary>
    /// 执行动作固定消耗的时间（秒）
    /// </summary>
    private static readonly Dictionary<string, double> ActionOverheads = new()
    {
        [ActionEnum.CombatScript.Code] = 10.0,
        [ActionEnum.StopFlying.Code] = 1.5,
    };

    private const double DefaultMoveSpeed = 4.0;
    private const double DefaultWaypointOverhead = 1.0;

    /// <summary>
    /// 匹配 action_params 中的 wait(0.4) 形式
    /// </summary>
    [GeneratedRegex(@"wait\(\s*(\d+(?:\.\d+)?)\s*\)")]
    private static partial Regex WaitParamRegex();

    public static double EstimateSeconds(PathingTask? task)
    {
        return task == null ? 0 : EstimateSeconds(task.Positions);
    }

    public static double EstimateSeconds(IReadOnlyList<Waypoint>? positions)
    {
        if (positions == null || positions.Count == 0)
        {
            return 0;
        }

        var total = 0d;
        for (var i = 0; i < positions.Count; i++)
        {
            var waypoint = positions[i];
            total += GetWaypointOverhead(waypoint);
            total += GetActionOverhead(waypoint);

            // 距离按目标点位的移动方式折算速度
            if (i > 0)
            {
                total += Distance(positions[i - 1], waypoint) / GetMoveSpeed(waypoint);
            }
        }

        return total;
    }

    private static double Distance(Waypoint from, Waypoint to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double GetMoveSpeed(Waypoint waypoint)
    {
        return MoveSpeeds.GetValueOrDefault(waypoint.MoveMode, DefaultMoveSpeed);
    }

    private static double GetWaypointOverhead(Waypoint waypoint)
    {
        return WaypointOverheads.GetValueOrDefault(waypoint.Type, DefaultWaypointOverhead);
    }

    private static double GetActionOverhead(Waypoint waypoint)
    {
        var total = 0d;

        var action = waypoint.Action?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (!string.IsNullOrEmpty(action))
        {
            total += ActionOverheads.GetValueOrDefault(action, 0);
        }

        if (!string.IsNullOrWhiteSpace(waypoint.ActionParams))
        {
            foreach (Match match in WaitParamRegex().Matches(waypoint.ActionParams))
            {
                if (double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                {
                    total += seconds;
                }
            }
        }

        return total;
    }
}
