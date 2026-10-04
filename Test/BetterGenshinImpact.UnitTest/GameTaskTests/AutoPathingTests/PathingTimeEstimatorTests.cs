using System.Collections.Generic;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Model;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoPathingTests;

/// <summary>
/// 地图追踪耗时的估算规则。参数是按实测任务标定的，这里用可手算的样例把规则锁死，
/// 避免后续调整参数时无意改变估算口径。
/// </summary>
public class PathingTimeEstimatorTests
{
    [Fact]
    public void EstimateSeconds_ShouldReturnZero_ForNoPositions()
    {
        Assert.Equal(0d, PathingTimeEstimator.EstimateSeconds((IReadOnlyList<Waypoint>?)null));
        Assert.Equal(0d, PathingTimeEstimator.EstimateSeconds(new List<Waypoint>()));
        Assert.Equal(0d, PathingTimeEstimator.EstimateSeconds((PathingTask?)null));
    }

    [Fact]
    public void EstimateSeconds_ShouldSumWaypointOverheadAndDistanceOverMoveSpeed()
    {
        var positions = new List<Waypoint>
        {
            new() { Type = "path", MoveMode = "walk", X = 0, Y = 0 },
            // 距离 4，速度 4 -> 1 秒
            new() { Type = "path", MoveMode = "walk", X = 4, Y = 0 },
        };

        // 两个途径点各 1 秒开销 + 1 秒移动
        Assert.Equal(3.0, PathingTimeEstimator.EstimateSeconds(positions), 3);
    }

    [Fact]
    public void EstimateSeconds_ShouldUseWaypointTypeOverhead()
    {
        var positions = new List<Waypoint>
        {
            new() { Type = "teleport", MoveMode = "walk", X = 0, Y = 0 },
            new() { Type = "path", MoveMode = "walk", X = 0, Y = 0 },
            new() { Type = "target", MoveMode = "walk", X = 0, Y = 0 },
            new() { Type = "orientation", MoveMode = "walk", X = 0, Y = 0 },
        };

        // 传送 8 + 途径点 1 + 目标点 2.5 + 方位点 2
        Assert.Equal(13.5, PathingTimeEstimator.EstimateSeconds(positions), 3);
    }

    [Theory]
    [InlineData("walk", 4.0)]
    [InlineData("run", 5.0)]
    [InlineData("dash", 5.5)]
    [InlineData("fly", 4.0)]
    [InlineData("jump", 2.5)]
    [InlineData("climb", 2.0)]
    [InlineData("swim", 3.0)]
    [InlineData("unknown_mode", 4.0)]
    public void EstimateSeconds_ShouldUseMoveModeSpeed(string moveMode, double speed)
    {
        var positions = new List<Waypoint>
        {
            new() { Type = "path", MoveMode = moveMode, X = 0, Y = 0 },
            new() { Type = "path", MoveMode = moveMode, X = speed * 2, Y = 0 },
        };

        // 两个途径点各 1 秒开销，距离 speed * 2 恰好耗时 2 秒
        Assert.Equal(4.0, PathingTimeEstimator.EstimateSeconds(positions), 3);
    }

    [Fact]
    public void EstimateSeconds_ShouldCountActionOverheadAndExplicitWait()
    {
        var positions = new List<Waypoint>
        {
            new() { Type = "path", MoveMode = "walk", X = 0, Y = 0, Action = "stop_flying" },
            // action 带参数时只取动作名；wait 累加
            new() { Type = "path", MoveMode = "walk", X = 0, Y = 0, Action = "combat_script 战斗", ActionParams = "wait(0.4),j,wait(0.1),j" },
            // 未知动作没有额外开销
            new() { Type = "path", MoveMode = "walk", X = 0, Y = 0, Action = "log_output" },
        };

        // 三个途径点各 1 秒开销 + 下落攻击 1.5 + 战斗策略 10 + 等待 0.5
        Assert.Equal(15.0, PathingTimeEstimator.EstimateSeconds(positions), 3);
    }

    [Fact]
    public void EstimateSeconds_ShouldMatchCalibratedSample()
    {
        var positions = new List<Waypoint>
        {
            new() { Type = "teleport", MoveMode = "walk", X = 0, Y = 0, Action = "stop_flying" },
            new() { Type = "path", MoveMode = "dash", X = 0, Y = 11 },
            new() { Type = "target", MoveMode = "fly", X = 8, Y = 11, Action = "combat_script", ActionParams = "wait(0.4),j" },
        };

        // 传送 8 + 下落攻击 1.5 + 途径点 1 + 冲刺 11/5.5 + 目标点 2.5 + 飞行 8/4 + 战斗策略 10 + 等待 0.4
        Assert.Equal(27.4, PathingTimeEstimator.EstimateSeconds(positions), 3);
    }
}
