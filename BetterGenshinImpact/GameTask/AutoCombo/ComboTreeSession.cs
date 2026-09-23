using BetterGenshinImpact.GameTask.AutoCombo.ComboBuild;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using CsTrees;
using CsTrees.Blackboard;
using System;

namespace BetterGenshinImpact.GameTask.AutoCombo;

/// <summary>
/// 一次建树会话的产物：持有连招行为树构建器、兜底攻击行为树构建器、黑板、建树时的队伍成员与建树元数据
/// 队伍成员名单随构建器注入 Catalog（构建时按名解析节点目标角色），
/// 消费方通过 CombatScenes.FromAvatars 接管队伍成员（校验当前游戏内队伍一致并复位成员状态），
/// 再通过 BindAndBuild 构建全新节点实例的树
/// </summary>
public sealed record ComboTreeSession
{
    /// <summary>连招行为树构建器，重复 Build 每次产出全新节点实例</summary>
    public required AutoComboBuildBuilder Builder { get; init; }

    /// <summary>建树时绑定的黑板，当前无运行期状态，保留作用域供未来节点间共享状态使用</summary>
    public required Blackboard Blackboard { get; init; }

    /// <summary>
    /// 兜底攻击行为树构建器
    /// </summary>
    public required AutoComboBuildFallbackBuilder FallbackBuilder { get; init; }

    /// <summary>建树时识别的队伍成员实例，建树期间的 Avatar 状态修改（如 ManualSkillCd）随实例携带到运行时</summary>
    public required Avatar[] Avatars { get; init; }

    /// <summary>建树完成时间</summary>
    public DateTimeOffset BuiltAt { get; init; }

    /// <summary>
    /// 构建全新行为树：清空黑板（复位上次运行的键值状态）→ 复位队伍成员的上次战斗状态 → Build（每次产出全新节点实例，节点内状态随实例自然复位）
    /// 队伍一致性校验与成员状态复位已由 CombatScenes.FromAvatars 在接管时完成
    /// </summary>
    public (Behaviour ComboTree, Behaviour FallbackTree) BindAndBuild()
    {
        Blackboard.Clear();
        foreach (var avatar in Avatars)
        {
            avatar.ResetSkillCdRecord();
        }

        var comboTree = Builder.Build();
        var fallbackTree = FallbackBuilder.Build();
        return (comboTree, fallbackTree);
    }
}
