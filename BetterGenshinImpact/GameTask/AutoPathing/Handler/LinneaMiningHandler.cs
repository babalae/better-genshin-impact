using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common.Job;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 莉奈娅Yolo挖矿
/// 格式: "射箭次数,大循环次数" 或 "射箭次数"
/// 例: "3" 或 "3,10" 或 "mines=3,rounds=10"
/// </summary>
public class LinneaMiningHandler : IActionHandler
{
    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        var (mineCount, scanRounds) = ParseParams(waypointForTrack?.ActionParams);

        var combatScenes = await RunnerContext.Instance.GetCombatScenes(ct);
        if (combatScenes == null)
        {
            Logger.LogError("队伍识别未初始化成功！");
            return;
        }

        // 切人
        var linnea = combatScenes.SelectAvatar("莉奈娅");
        if (linnea is not null)
        {
            linnea.TrySwitch();
            await Delay(500, ct);
        }
        else
        {
            Logger.LogError("队伍中未找到莉奈娅！");
            return;
        }

        await new LinneaMiningTask(scanRounds, mineCount).Start(ct);
    }

    private static (int mineCount, int scanRounds) ParseParams(string? actionParams)
    {
        if (string.IsNullOrEmpty(actionParams)) return (LinneaMiningTask.DefaultMineCount, LinneaMiningTask.DefaultScanRounds);

        var parts = actionParams.Split(',');
        var mineCount = -1;
        var scanRounds = -1;

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("mines=", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(trimmed.AsSpan("mines=".Length), out var m))
            {
                mineCount = Clamp(m, 1, 999);
            }
            else if (trimmed.StartsWith("rounds=", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(trimmed.AsSpan("rounds=".Length), out var r))
            {
                scanRounds = Clamp(r, 1, 999);
            }
            else if (int.TryParse(trimmed, out var num))
            {
                if (mineCount == -1)
                    mineCount = Clamp(num, 1, 999);
                else if (scanRounds == -1)
                    scanRounds = Clamp(num, 1, 999);
            }
        }

        if (mineCount == -1) mineCount = LinneaMiningTask.DefaultMineCount;
        if (scanRounds == -1) scanRounds = LinneaMiningTask.DefaultScanRounds;
        if (scanRounds < mineCount) scanRounds = mineCount;

        return (mineCount, scanRounds);
    }

    private static int Clamp(int value, int min, int max) => value <= 0 ? min : value > max ? max : value;
}
