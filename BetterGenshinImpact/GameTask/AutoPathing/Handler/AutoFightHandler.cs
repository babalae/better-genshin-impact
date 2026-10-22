using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoFight.Factory;
using BetterGenshinImpact.GameTask.Common;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Runtime;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 自动战斗
/// </summary>
/// <param name="runtime">本次运行使用的运行环境（创建战斗任务需要）</param>
/// <param name="config">配置根</param>
internal class AutoFightHandler(GameRuntime runtime, AllConfig config) : IActionHandler
{
    /// <summary>
    /// 本次运行使用的运行环境
    /// </summary>
    private readonly GameRuntime _runtime = runtime;

    /// <summary>
    /// 配置根
    /// </summary>
    private readonly AllConfig _config = config;

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
            taskParams = new AutoFightParam(GetFightStrategy(), _config.AutoFightConfig);
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
                       // 直接调用 RunAsync（而非 ExecuteAsync），保持战斗任务提前结束时异常向外传播的原有语义
                       await RunnerContext.Instance.StopAutoPickRunTask(
                           async () => await factory.CreateTask(_runtime, _config).RunAsync(taskParams, ct),
                           5);
                       return;
                   }
               }

           }
            
        }
        
        var factory2 = CombatTaskFactoryProvider.GetFactory(taskParams.CombatStrategyPath);
        var fightSoloTask = factory2.CreateTask(_runtime, _config);
        // 直接调用 RunAsync（而非 ExecuteAsync），保持战斗任务提前结束时异常向外传播的原有语义
        await fightSoloTask.RunAsync(taskParams, ct);
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

        // 固定策略：不对应策略文件，跳过存在性检查，由 ComboCombatTaskFactory 路由
        if (AutoFightParam.ComboStrategyName.Equals(config.StrategyName))
        {
            return config.StrategyName;
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
        return GetFightStrategy(_config.AutoFightConfig);
    }
}
