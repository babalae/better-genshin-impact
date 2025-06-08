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
    private readonly ILogger<AutoFightHandler> _logger = App.GetLogger<AutoFightHandler>();
    public async Task RunAsyncByScript(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        if (!(config != null && config is PathingPartyConfig patyConfig && patyConfig is {AutoFightEnabled:true,JsScriptUseEnabled:true,SoloTaskUseFightEnabled:true}  ))
        {
            config = null;
        }
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
        if (config != null && config is PathingPartyConfig patyConfig && patyConfig.AutoFightEnabled)
        {
            //替换配置为地图追踪

            taskParams = GetFightAutoFightParam(patyConfig.AutoFightConfig);
        }
        else
        {
            taskParams = new AutoFightParam(GetFightStrategy(), TaskContext.Instance().Config.AutoFightConfig);
        }

        //根据怪物标签，调整拾取配置
        if (waypointForTrack!=null)
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
                       await RunnerContext.Instance.StopAutoPickRunTask(
                           async () => await new AutoFightTask(taskParams).Start(ct),
                           5);
                       return;
                   }
               }

           }
            
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