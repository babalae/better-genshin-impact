using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.Common;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

internal class AutoFightHandler : IActionHandler
{
    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        await StartFight(ct, config);
    }

    private async Task StartFight(CancellationToken ct, object? config = null)
    {
        TaskControl.Logger.LogInformation("执行 {Text}", "自动战斗");
        // 爷们要战斗
        AutoFightParam taskParams = null;
        if (config != null && config is PathingPartyConfig patyConfig && patyConfig.AutoFightEabled)
        {
            //替换配置为路径追踪

            taskParams = GetFightAutoFightParam(patyConfig.AutoFightConfig);
        }
        else
        {
            taskParams = new AutoFightParam(GetFightStrategy(), TaskContext.Instance().Config.AutoFightConfig);
        }

        var fightSoloTask = new AutoFightTask(taskParams);
        await fightSoloTask.Start(ct);
    }

    private AutoFightParam GetFightAutoFightParam(AutoFightConfig? config)
    {
        AutoFightParam autoFightParam = new AutoFightParam(GetFightStrategy(config), config);
        return autoFightParam;
    }

    private string GetFightStrategy(AutoFightConfig config)
    {
        var path = Global.Absolute(@"User\AutoFight\" + config.StrategyName + ".txt");
        if ("根据队伍自动选择".Equals(config.StrategyName))
        {
            path = Global.Absolute(@"User\AutoFight\");
        }

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new Exception("战斗策略文件不存在");
        }

        return path;
    }

    private string GetFightStrategy()
    {
        return GetFightStrategy(TaskContext.Instance().Config.AutoFightConfig);
    }
}