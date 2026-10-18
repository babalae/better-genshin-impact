using BetterGenshinImpact.Core.Input;
using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Runtime;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 使用纳西妲长按E技能进行收集，360°球形无死角扫码，`type = target`的情况才有效
/// </summary>
/// <param name="runtime">本次运行使用的运行环境</param>
/// <param name="config">配置根</param>
public class NahidaCollectHandler(GameRuntime runtime, AllConfig config) : IActionHandler
{
    /// <summary>
    /// 本次运行使用的运行环境
    /// </summary>
    private readonly GameRuntime _runtime = runtime;

    /// <summary>
    /// 配置根
    /// </summary>
    private readonly AllConfig _config = config;

    private DateTime lastETime = DateTime.MinValue;

    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        Logger.LogInformation("执行 {Nhd} 长按E转圈拾取", "纳西妲");

        var combatScenes = await RunnerContext.Instance.GetCombatScenes(_runtime, _config, ct);
        if (combatScenes == null)
        {
            Logger.LogError("队伍识别未初始化成功！");
            return;
        }

        // 切人
        var nahida = combatScenes.SelectAvatar("纳西妲");
        if (nahida is not null)
        {
            nahida.TrySwitch();
        }
        else
        {
            Logger.LogError("队伍中未找到纳西妲角色！");
            return;
        }

        await nahida.WaitSkillCd(ct);

        var dpi = _runtime.SystemInfo.DpiScale;

        int x = (int)(400 * dpi), y = (int)(-30 * dpi);
        int i = 60;
        // 视角拉到最下面
        InputHub.Foreground.Mouse.MoveMouseBy(0, 10000);
        await _runtime.Delay(200, ct);

        // 按住E技能 无死角扫码
        InputHub.Foreground.SimulateAction(GIActions.ElementalSkill, KeyType.KeyDown);
        try
        {
            await _runtime.Delay(200, ct);

            // 先地面来一圈
            for (int j = 0; j < 15; j++)
            {
                InputHub.Foreground.Mouse.MoveMouseBy(x, 500);
                await _runtime.Delay(30, ct);
            }

            // 然后抬斜向转圈
            while (!ct.IsCancellationRequested && i > 0)
            {
                i--;
                if (i == 40)
                {
                    y -= (int)(20 * dpi);
                }

                InputHub.Foreground.Mouse.MoveMouseBy(x, y);
                await _runtime.Delay(30, ct);
            }
        }
        finally
        {
            // 就算被终止也要让按键弹回
            InputHub.Foreground.SimulateAction(GIActions.ElementalSkill, KeyType.KeyUp);
            // 更新纳西妲CD
            if (!ct.IsCancellationRequested)
            {
                await _runtime.Delay(200, ct);
                var cd = nahida.AfterUseSkill();
                Logger.LogInformation("{Nhd} 长按E转圈,cd:{Cd}", "纳西妲", Math.Round(cd, 2));
            }
        }

        await _runtime.Delay(800, ct);
        // 恢复视角
        InputHub.Foreground.Mouse.MiddleButtonClick();
        await _runtime.Delay(1000, ct);
    }
}