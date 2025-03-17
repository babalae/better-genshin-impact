using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPick;

/// <summary>
/// 万叶吸引掉落物任务，从AutoFightTask分离的
/// </summary>
public class KazuhaPickUpTask : ISoloTask
{
    public string Name => "万叶吸引掉落物";


    public async Task Start(CancellationToken ct = default)
    {
        var combatScenes = new CombatScenes().InitializeTeam(CaptureToRectArea());
        combatScenes.BeforeTask(ct);
        await Execute(combatScenes, ct);
        combatScenes.AfterTask();

    }

    public static async Task Execute(CombatScenes combatScenes,CancellationToken ct = default)
    {
        var kazuha = combatScenes.SelectAvatar("枫原万叶");
        if (kazuha is null)
        {
            Logger.LogInformation("队伍中没有找到枫原万叶,跳过");
            return;
        }

        kazuha.TrySwitch();
        Logger.LogInformation("使用枫原万叶长E吸引周围掉落物");
        await Delay(300, ct);
        if (kazuha.TrySwitch())
        {
            await kazuha.WaitSkillCd(ct);
            kazuha.UseSkill(true);
            await Task.Delay(100); // 这里不可中断
            Simulation.SendInput.SimulateAction(GIActions.NormalAttack);
            await Delay(1500, ct);
        }
        else
        {
            Logger.LogWarning("切换到枫原万叶失败");
        }
    }
}