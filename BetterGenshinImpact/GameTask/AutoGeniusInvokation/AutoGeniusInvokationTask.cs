using BetterGenshinImpact.Helpers;
﻿using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation;

public class AutoGeniusInvokationTask(GeniusInvokationTaskParam taskParam) : ISoloTask
{
    public string Name => Lang.S["Task_002_16fb22"];

    public Task Start(CancellationToken ct)
    {
        try
        {
            // 读取策略信息
            var duel = ScriptParser.Parse(taskParam.StrategyContent);
            duel.Run(ct);
        }
        catch (System.Exception e)
        {
            TaskControl.Logger.LogDebug(e, Lang.S["GameTask_10850_f0b873"]);
            TaskControl.Logger.LogError(Lang.S["GameTask_10849_97787d"], e.Message);
        }
        return Task.CompletedTask;
    }
}