using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoFight.Factory;
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
    private const int DisabledFightTimeout = 0;
    private const string Elite400PathingFolder = "精英400@汐";
    private const string Elite400TaskName = "400精英";

    private readonly ILogger<AutoFightHandler> _logger = App.GetLogger<AutoFightHandler>();
    public async Task RunAsyncByScript(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        await StartFight(ct, config,waypointForTrack);
    }

    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        await StartFight(ct, config,waypointForTrack);
    }

    private async Task StartFight(CancellationToken ct, object? config = null , WaypointForTrack? waypointForTrack = null)
    {
        TaskControl.Logger.LogInformation("执行 {Text}", "自动战斗");
        // 爷们要战斗
        AutoFightParam taskParams = null;
        if (config is PathingPartyConfig { Enabled: true, AutoFightEnabled: true } partyConfig)
        {
            //替换配置为地图追踪

            taskParams = GetFightAutoFightParam(partyConfig.AutoFightConfig);
        }
        else
        {
            taskParams = new AutoFightParam(GetFightStrategy(), TaskContext.Instance().Config.AutoFightConfig);
        }

        if (ShouldDisableTimeTimeoutForPathing(waypointForTrack))
        {
            ApplyElite400NoTimeoutSafety(taskParams);
            _logger.LogInformation("当前为 400 精英路线，禁用时间型战斗超时并启用找敌失败保护");
        }

        //根据怪物标签，调整拾取配置
        if (waypointForTrack!=null && waypointForTrack.EnableMonsterLootSplit)
        {
           // normal 小怪,elite 精英,legendary 传奇
           //不为精英或者小怪
           if (!(waypointForTrack.MonsterTag == "elite" || waypointForTrack.MonsterTag == "legendary"))
           {
               
               if (taskParams.OnlyPickEliteDropsMode == "AllowAutoPickupForNonElite" || taskParams.OnlyPickEliteDropsMode == "DisableAutoPickupForNonElite")
               {
                   //允许自动拾取，即只关闭配置上的拾取即刻
                   taskParams.KazuhaPickupEnabled = false;
                   taskParams.PickDropsAfterFightEnabled = false;
                   _logger.LogInformation("当前非精英或传奇点位，关闭战斗拾取配置！");
                   //禁止自动拾取，除了关闭配置拾取外，连自动拾取都关掉
                   if (taskParams.OnlyPickEliteDropsMode == "DisableAutoPickupForNonElite")
                   {
                       var factory = CombatTaskFactoryProvider.GetFactory(taskParams.CombatStrategyPath);
                       await RunnerContext.Instance.StopAutoPickRunTask(
                           async () => await factory.CreateTask(taskParams).Start(ct),
                           5);
                       return;
                   }
               }

           }
            
        }
        
        var factory2 = CombatTaskFactoryProvider.GetFactory(taskParams.CombatStrategyPath);
        var fightSoloTask = factory2.CreateTask(taskParams);
        await fightSoloTask.Start(ct);
    }

    internal static bool ShouldDisableTimeTimeoutForPathing(WaypointForTrack? waypointForTrack)
    {
        return IsElite400PathingSource(waypointForTrack?.PathingTaskFileName, waypointForTrack?.PathingTaskFullPath);
    }

    internal static bool IsElite400PathingSource(string? fileName, string? fullPath)
    {
        return ContainsElite400Marker(fileName) || ContainsElite400Marker(fullPath);
    }

    internal static void ApplyElite400NoTimeoutSafety(AutoFightParam taskParams)
    {
        taskParams.Timeout = DisabledFightTimeout;
        taskParams.FightFinishDetectEnabled = true;
        taskParams.FinishDetectConfig.RotateFindEnemyEnabled = true;
    }

    private static bool ContainsElite400Marker(string? value)
    {
        return !string.IsNullOrEmpty(value) &&
               (value.Contains(Elite400PathingFolder, StringComparison.OrdinalIgnoreCase) ||
                value.Contains(Elite400TaskName, StringComparison.OrdinalIgnoreCase));
    }

    private AutoFightParam GetFightAutoFightParam(AutoFightConfig? config)
    {
        AutoFightParam autoFightParam = new AutoFightParam(GetFightStrategy(config), config);
        return autoFightParam;
    }

    private string GetFightStrategy(AutoFightConfig config)
    {
        if ("根据队伍自动选择".Equals(config.StrategyName) || string.IsNullOrEmpty(config.StrategyName))
        {
            return Global.Absolute(@"User\AutoFight\");
        }

        var (path, _) = AutoFightParam.ResolveStrategyPath(config.StrategyName);
        if (!File.Exists(path))
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
