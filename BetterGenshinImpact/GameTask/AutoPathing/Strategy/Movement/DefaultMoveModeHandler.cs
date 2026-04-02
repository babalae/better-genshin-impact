using System;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Strategy.Movement;

/// <summary>
/// 默认移动模式处理器（处理除指定模式外的常规地面移动，并进行战技及自动辅助跑的释放） 
/// Default movement handler for generic ground traversal, including elemental skills and auto sprinting.
/// </summary>
public class DefaultMoveModeHandler : IMoveModeHandler
{
    public DefaultMoveModeHandler()
    {
    }

    public bool CanHandle(string moveModeCode) 
        => moveModeCode != MoveModeEnum.Fly.Code && 
           moveModeCode != MoveModeEnum.Jump.Code &&
           moveModeCode != MoveModeEnum.Run.Code &&
           moveModeCode != MoveModeEnum.Dash.Code &&
           moveModeCode != MoveModeEnum.Climb.Code;

    public async Task<MoveModeResult> ExecuteAsync(WaypointForTrack waypoint, PathingMovementContext context)
    {
        var partyConfig = context.PartyConfigGetter();

        if (context.Distance > 10 && !string.IsNullOrEmpty(partyConfig.GuardianAvatarIndex) &&
            double.TryParse(partyConfig.GuardianElementalSkillSecondInterval, out var s))
        {
            if (s < 1)
            {
                Logger.LogWarning("元素战技冷却时间设置太短，不执行！");
                return MoveModeResult.ReturnFalse;
            }

            var ms = s * 1000;
            if ((DateTime.UtcNow - context.GetElementalSkillLastUseTime()).TotalMilliseconds > ms)
            {
                if (context.Num <= 5 && (!string.IsNullOrEmpty(partyConfig.MainAvatarIndex) &&
                                 partyConfig.GuardianAvatarIndex != partyConfig.MainAvatarIndex))
                {
                    await Delay(800, context.CancellationToken);
                }

                await context.UseElementalSkillAction();
                context.SetElementalSkillLastUseTime(DateTime.UtcNow);
            }
        }

        if (context.Distance > 20 && partyConfig.AutoRunEnabled && (DateTime.UtcNow - context.FastModeColdTime).TotalMilliseconds > 2500)
        {
            context.FastModeColdTime = DateTime.UtcNow;
            Simulation.SendInput.SimulateAction(GIActions.SprintMouse);
        }

        return MoveModeResult.Pass;
    }
}
