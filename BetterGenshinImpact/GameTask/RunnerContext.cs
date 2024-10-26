using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.OneDragon;
using BetterGenshinImpact.Model;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask;

/// <summary>
/// 使用 TaskRunner 运行任务时的上下文
/// </summary>
public class RunnerContext : Singleton<RunnerContext>
{
    private CombatScenes? _combatScenes;

    public async Task<CombatScenes?> GetCombatScenes(CancellationToken ct)
    {
        if (_combatScenes == null)
        {
            // 返回主界面再识别
            var returnMainUiTask = new ReturnMainUiTask();
            await returnMainUiTask.Start(ct);

            await Delay(200, ct);

            _combatScenes = new CombatScenes().InitializeTeam(CaptureToRectArea());
            if (!_combatScenes.CheckTeamInitialized())
            {
                Logger.LogError("队伍角色识别失败");
                _combatScenes = null;
            }
        }
        return _combatScenes;
    }

    public void Clear()
    {
        _combatScenes = null;
    }
}
