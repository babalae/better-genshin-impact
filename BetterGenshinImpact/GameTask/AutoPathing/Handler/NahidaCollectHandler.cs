using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
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
        Logger.LogInformation("执行 {Nhd} 长按E转圈拾取", "纳西妲");

        var combatScenes = await RunnerContext.Instance.GetCombatScenes(ct);
        if (combatScenes == null)
        {
            Logger.LogError("队伍识别未初始化成功！");
            return;
        }

        // 切人
        if (combatScenes.AvatarMap.TryGetValue("纳西妲", out var nahida))
        {
            nahida.TrySwitch();
        }
        else
        {
            Logger.LogError("队伍中未找到纳西妲角色！");
            return;
        }

        var cd = DateTime.Now - lastETime;
        if (cd < TimeSpan.FromSeconds(10))
        {
            await Delay((int)((6 - cd.TotalSeconds + 0.5) * 1000), ct);
        }

        var dpi = TaskContext.Instance().DpiScale;

        int x = (int)(400 * dpi), y = (int)(-30 * dpi);
        int i = 60;
        // 视角拉到最下面
        Simulation.SendInput.Mouse.MoveMouseBy(0, 10000);
        await Delay(200, ct);

        // 按住E技能 无死角扫码
        Simulation.SendInput.Keyboard.KeyDown(TaskContext.Instance().Config.KeyBindingsConfig.ElementalSkill.ToVK());
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
        Simulation.SendInput.Keyboard.KeyUp(TaskContext.Instance().Config.KeyBindingsConfig.ElementalSkill.ToVK());

        lastETime = DateTime.Now;

        await Delay(1000, ct);

        // 恢复视角
        Simulation.SendInput.Mouse.MiddleButtonClick();

        await Delay(1000, ct);
    }
}
