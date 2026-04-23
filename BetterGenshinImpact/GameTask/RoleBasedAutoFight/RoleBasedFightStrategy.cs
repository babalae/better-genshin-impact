using BetterGenshinImpact.Core.Simulator;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.RoleBasedAutoFight;

public class RoleBasedFightStrategy
{
    private readonly RoleBasedAutoFightParam _param;
    private DateTime _lastZHealTime = DateTime.MinValue;

    private readonly Dictionary<string, IRoleStrategy> _strategyMap = new()
    {
        { "shield", new ShieldStrategy() },
        { "healer", new HealerStrategy() },
        { "buffer", new BufferStrategy() },
        { "sub_dps", new SubDpsStrategy() },
        { "main_dps", new MainDpsStrategy() }
    };

    public RoleBasedFightStrategy(RoleBasedAutoFightParam param)
    {
        _param = param;
    }

    public async Task OnTickAsync(CombatScenes combatScenes, CancellationToken ct)
    {
        var activeAvatarIdx = combatScenes.GetActiveAvatarIndex(TaskControl.CaptureToRectArea(), new AvatarActiveCheckContext());
        var activeAvatar = combatScenes.GetAvatars().FirstOrDefault(a => a.Index == activeAvatarIdx);

        if (activeAvatar == null)
            return;

        // Layer 0: 异常状态脱离
        if (_param.AutoEscapeAbnormal)
        {
            var screen = TaskControl.CaptureToRectArea();
            var motion = Bv.GetMotionStatus(screen);
            
            if (motion == MotionStatus.Climb)
            {
                TaskControl.Logger.LogInformation("[脱离] 爬墙中，按X脱离");
                Simulation.SendInput.Keyboard.KeyPress(Vanara.PInvoke.User32.VK.VK_X);
                await Task.Delay(1000, ct);
                return;
            }
            
            // TODO: SwimmingConfirm
        }

        // Layer 1: Z键紧急吃药
        if (_param.UseNreGadget)
        {
            if ((DateTime.Now - _lastZHealTime).TotalSeconds > 2.5)
            {
                if (BvStatus.CurrentAvatarIsLowHp(TaskControl.CaptureToRectArea()))
                {
                    TaskControl.Logger.LogInformation("[紧急生存] 当前角色红血，使用便携营养袋(Z)");
                    Simulation.SendInput.Keyboard.KeyPress(Vanara.PInvoke.User32.VK.VK_Z);
                    _lastZHealTime = DateTime.Now;
                    await Task.Delay(500, ct);
                    return;
                }
            }
        }

        // Layer 2 & 3: 自动追怪
        if (_param.AutoChaseEnemy)
        {
            await RoleBasedEnemySeeker.CheckAndChaseEnemyAsync(ct);
        }

        // Layer 4: 角色评估与切换
        var wrappers = combatScenes.GetAvatars().Select(a => new RoleBasedAvatarWrapper(a)).ToList();
        var activeWrapper = wrappers.FirstOrDefault(w => w.BaseAvatar.Index == activeAvatarIdx) ?? wrappers[0];

        RoleBasedAvatarWrapper bestWrapper = activeWrapper;
        int highestScore = -1;

        foreach (var wrapper in wrappers)
        {
            if (!_strategyMap.TryGetValue(wrapper.CombatRole, out var strategy))
            {
                strategy = _strategyMap["sub_dps"]; // fallback
            }

            int score = strategy.EvaluatePriority(wrapper, combatScenes);
            
            // 防抖：给当前在场角色加分
            if (wrapper.BaseAvatar.Index == activeAvatarIdx)
            {
                score += 10;
            }

            if (score > highestScore)
            {
                highestScore = score;
                bestWrapper = wrapper;
            }
        }

        // 执行切换和动作
        if (bestWrapper.BaseAvatar.Index != activeAvatarIdx)
        {
            if (bestWrapper.TrySwitch(_param.AntiPreemptionSeconds))
            {
                // 切人成功，稍作延迟后执行技能
                await Task.Delay(200, ct);
                await _strategyMap[bestWrapper.CombatRole].ExecuteActionAsync(bestWrapper, combatScenes, ct);
            }
            else
            {
                // 切人被锁定，则继续执行当前角色
                await _strategyMap[activeWrapper.CombatRole].ExecuteActionAsync(activeWrapper, combatScenes, ct);
            }
        }
        else
        {
            // 继续执行当前角色
            await _strategyMap[activeWrapper.CombatRole].ExecuteActionAsync(activeWrapper, combatScenes, ct);
        }
    }
}
