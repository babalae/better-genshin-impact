using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 负责处理自动战斗动作的执行逻辑 / Handles the execution logic for automatic combat actions.
/// </summary>
public class AutoFightHandler : IActionHandler
{
    private readonly ILogger<AutoFightHandler> _logger = App.GetLogger<AutoFightHandler>();

    /// <inheritdoc/>
    public async Task RunAsyncByScript(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null) => await StartFightAsync(ct, config, waypointForTrack);

    /// <inheritdoc/>
    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        await StartFightAsync(ct, config, waypointForTrack);
    }

    private async Task StartFightAsync(CancellationToken ct, object? config = null, WaypointForTrack? waypointForTrack = null)
    {
        TaskControl.Logger.LogInformation("执行自动战斗任务");
        
        AutoFightParam taskParams;
        if (config is PathingPartyConfig partyConfig && partyConfig.AutoFightEnabled)
        {
            taskParams = CreateFightParam(partyConfig.AutoFightConfig);
        }
        else
        {
            var fallbackConfig = TaskContext.Instance().Config.AutoFightConfig;
            taskParams = new AutoFightParam(GetStrategyPath(fallbackConfig), fallbackConfig);
        }

        ProcessMonsterLootConfiguration(waypointForTrack, taskParams);

        if (waypointForTrack != null && waypointForTrack.EnableMonsterLootSplit && 
           !(waypointForTrack.MonsterTag == "elite" || waypointForTrack.MonsterTag == "legendary") &&
            taskParams.OnlyPickEliteDropsMode == "DisableAutoPickupForNonElite")
        {
            await RunnerContext.Instance.StopAutoPickRunTask(
                async () => await new AutoFightTask(taskParams).Start(ct),
                5);
            return;
        }

        var fightSoloTask = new AutoFightTask(taskParams);
        await fightSoloTask.Start(ct);
    }

    private void ProcessMonsterLootConfiguration(WaypointForTrack? waypointForTrack, AutoFightParam taskParams)
    {
        if (waypointForTrack?.EnableMonsterLootSplit != true)
        {
            return;
        }

        // normal, elite, legendary
        if (waypointForTrack.MonsterTag == "elite" || waypointForTrack.MonsterTag == "legendary")
        {
            return;
        }

        if (taskParams.OnlyPickEliteDropsMode is "AllowAutoPickupForNonElite" or "DisableAutoPickupForNonElite")
        {
            taskParams.KazuhaPickupEnabled = false;
            taskParams.PickDropsAfterFightEnabled = false;
            _logger.LogInformation("当前点位非精英/传奇，已关闭战斗关联的拾取配置");
        }
    }

    private AutoFightParam CreateFightParam(AutoFightConfig? config)
    {
        return new AutoFightParam(GetStrategyPath(config), config);
    }

    private string GetStrategyPath(AutoFightConfig? config)
    {
        ArgumentNullException.ThrowIfNull(config);
        
        var strategyName = config.StrategyName;
        var path = "根据队伍自动选择".Equals(strategyName, StringComparison.OrdinalIgnoreCase) 
            ? Global.Absolute(@"User\AutoFight\")
            : Global.Absolute($@"User\AutoFight\{strategyName}.txt");

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new FileNotFoundException($"战斗策略文件不存在 {path}");
        }

        return path;
    }
}

