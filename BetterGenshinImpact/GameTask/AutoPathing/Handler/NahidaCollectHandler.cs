using BetterGenshinImpact.Helpers;
﻿using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 使用纳西妲长按E技能进行收集，360°球形无死角扫码，`type = target`的情况才有效
/// </summary>
public class NahidaCollectHandler : IActionHandler
{
    private DateTime lastETime = DateTime.MinValue;

    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        Logger.LogInformation(Lang.S["GameTask_11134_cfecbd"], "纳西妲");

        var combatScenes = await RunnerContext.Instance.GetCombatScenes(ct);
        if (combatScenes == null)
        {
            Logger.LogError(Lang.S["GameTask_11074_f6bb4a"]);
            return;
        }

        // 切人
        var nahida = combatScenes.SelectAvatar(Lang.S["GameTask_10596_8279ac"]);
        if (nahida is not null)
        {
            nahida.TrySwitch();
        }
        else
        {
            Logger.LogError(Lang.S["GameTask_11133_ef90b5"]);
            return;
        }

        await nahida.WaitSkillCd(ct);

        var dpi = TaskContext.Instance().DpiScale;

        int x = (int)(400 * dpi), y = (int)(-30 * dpi);
        int i = 60;
        // 视角拉到最下面
        Simulation.SendInput.Mouse.MoveMouseBy(0, 10000);
        await Delay(200, ct);

        // 按住E技能 无死角扫码
        Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.KeyDown);
        try
        {
            await Delay(200, ct);

            // 先地面来一圈
            for (int j = 0; j < 15; j++)
            {
                Simulation.SendInput.Mouse.MoveMouseBy(x, 500);
                await Delay(30, ct);
            }

            // 然后抬斜向转圈
            while (!ct.IsCancellationRequested && i > 0)
            {
                i--;
                if (i == 40)
                {
                    y -= (int)(20 * dpi);
                }

                Simulation.SendInput.Mouse.MoveMouseBy(x, y);
                await Delay(30, ct);
            }
        }
        finally
        {
            // 就算被终止也要让按键弹回
            Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.KeyUp);
            // 更新纳西妲CD
            if (!ct.IsCancellationRequested)
            {
                await Delay(200, ct);
                var cd = nahida.AfterUseSkill();
                Logger.LogInformation(Lang.S["GameTask_11132_3e87c2"], "纳西妲", Math.Round(cd, 2));
            }
        }

        await Delay(800, ct);
        // 恢复视角
        Simulation.SendInput.Mouse.MiddleButtonClick();
        await Delay(1000, ct);
    }
}