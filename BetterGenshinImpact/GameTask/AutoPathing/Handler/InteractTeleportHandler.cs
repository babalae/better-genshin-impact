using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// Handles an in-world teleport device activated by the interaction key.
/// </summary>
public class InteractTeleportHandler : IActionHandler
{
    private const double DefaultWaitSeconds = 0;
    private const int MaxInteractionAttempts = 8;
    private const int InteractionIntervalMs = 250;
    private const int TeleportSettleTimeoutMs = 12000;
    private const int TeleportSettleIntervalMs = 250;
    private const double TeleportMovementThreshold = 120.0;

    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        var waitSeconds = ParseWaitSeconds(waypointForTrack?.ActionParams);
        Logger.LogInformation("执行动作: 【交互传送】按下交互键，等待传送状态出现");

        if (waypointForTrack == null || config is not PathExecutor executor)
        {
            Simulation.SendInput.SimulateAction(GIActions.PickUpOrInteract);
            await DelayIfNeeded(waitSeconds, ct);
            return;
        }

        var beforePosition = GetCurrentPosition(waypointForTrack);
        for (var attempt = 1; attempt <= MaxInteractionAttempts; attempt++)
        {
            Simulation.SendInput.SimulateAction(GIActions.PickUpOrInteract);
            await Delay(InteractionIntervalMs, ct);

            var state = DetectTeleportState(waypointForTrack, beforePosition);
            if (state.TeleportStarted)
            {
                Logger.LogInformation("交互传送已触发：{Reason}，交互次数 {Attempt}", state.Reason, attempt);
                await WaitForTeleportSettled(waypointForTrack, ct);
                await DelayIfNeeded(waitSeconds, ct);
                return;
            }
        }

        Logger.LogWarning("交互传送在 {Attempts} 次交互后仍未检测到传送状态，后续将重新定位确认。", MaxInteractionAttempts);
        await WaitForTeleportSettled(waypointForTrack, ct);
        await DelayIfNeeded(waitSeconds, ct);
    }

    private static async Task WaitForTeleportSettled(WaypointForTrack waypoint, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(TeleportSettleTimeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            await Delay(TeleportSettleIntervalMs, ct);
            using var screen = CaptureToRectArea();
            if (!Bv.IsInMainUi(screen))
            {
                continue;
            }

            var position = Navigation.GetPosition(screen, waypoint.MapName, waypoint.MapMatchMethod);
            if (!PathingPositionValidator.IsKnownPosition(position))
            {
                continue;
            }

            Navigation.SetPrevPosition(position.X, position.Y);
            Logger.LogInformation("交互传送后小地图已恢复，当前位置：({X:F1}, {Y:F1})", position.X, position.Y);
            return;
        }

        Logger.LogWarning("交互传送后等待小地图恢复超时，后续路径点将自行重新建立定位基准。");
    }

    private static async Task DelayIfNeeded(double waitSeconds, CancellationToken ct)
    {
        if (waitSeconds > 0)
        {
            await Delay(ToMilliseconds(waitSeconds), ct);
        }
    }

    private static Point2f GetCurrentPosition(WaypointForTrack waypoint)
    {
        using var screen = CaptureToRectArea();
        return Navigation.GetPosition(screen, waypoint.MapName, waypoint.MapMatchMethod);
    }

    private static (bool TeleportStarted, string Reason) DetectTeleportState(WaypointForTrack waypoint, Point2f beforePosition)
    {
        using var screen = CaptureToRectArea();
        if (!Bv.IsInMainUi(screen))
        {
            return (true, "当前不在主界面");
        }

        var currentPosition = Navigation.GetPosition(screen, waypoint.MapName, waypoint.MapMatchMethod);
        if (!PathingPositionValidator.IsKnownPosition(currentPosition))
        {
            return (true, "小地图位置暂不可识别");
        }

        if (PathingPositionValidator.IsKnownPosition(beforePosition)
            && GetDistance(beforePosition, currentPosition) > TeleportMovementThreshold)
        {
            return (true, $"识别坐标大幅移动 {GetDistance(beforePosition, currentPosition):F1}");
        }

        return (false, string.Empty);
    }

    private static double GetDistance(Point2f a, Point2f b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static double ParseWaitSeconds(string? actionParams)
    {
        if (string.IsNullOrWhiteSpace(actionParams))
        {
            return DefaultWaitSeconds;
        }

        if (double.TryParse(actionParams, NumberStyles.Float, CultureInfo.InvariantCulture, out var directValue))
        {
            return Math.Max(0, directValue);
        }

        var values = ParseKeyValuePairs(actionParams);
        return values.TryGetValue("wait", out var waitText)
               && double.TryParse(waitText, NumberStyles.Float, CultureInfo.InvariantCulture, out var waitSeconds)
            ? Math.Max(0, waitSeconds)
            : DefaultWaitSeconds;
    }

    private static Dictionary<string, string> ParseKeyValuePairs(string actionParams)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var parts = actionParams.Split(new[] { ',', ';' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var index = part.IndexOf('=');
            if (index <= 0 || index >= part.Length - 1)
            {
                continue;
            }

            result[part[..index].Trim()] = part[(index + 1)..].Trim();
        }

        return result;
    }

    private static int ToMilliseconds(double seconds)
    {
        return (int)Math.Round(Math.Max(0, seconds) * 1000, MidpointRounding.AwayFromZero);
    }
}
