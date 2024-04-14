using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.Model;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoFight;

/// <summary>
/// 一键战斗宏
/// </summary>
public class OneKeyFightTask : Singleton<OneKeyFightTask>
{
    public void Run()
    {
        var combatScenes = new CombatScenes().InitializeTeam(GetContentFromDispatcher());
    }
}
