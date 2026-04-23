using static BetterGenshinImpact.GameTask.Common.TaskControl;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoFight.Model;

namespace BetterGenshinImpact.GameTask.RoleBasedAutoFight;

public interface IRoleStrategy
{
    /// <summary>
    /// 评估上场优先级
    /// </summary>
    int EvaluatePriority(RoleBasedAvatarWrapper avatar, CombatScenes scenes);

    /// <summary>
    /// 执行角色的具体战斗行为
    /// </summary>
    Task ExecuteActionAsync(RoleBasedAvatarWrapper avatar, CombatScenes scenes, CancellationToken ct);
}
