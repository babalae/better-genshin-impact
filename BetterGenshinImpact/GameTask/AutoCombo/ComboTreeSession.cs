using BetterGenshinImpact.GameTask.AutoCombo.ComboBuild;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using CsTrees;
using CsTrees.Blackboard;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoCombo;

/// <summary>
/// 一次建树会话的产物：持有行为树构建器、黑板与建树元数据
/// 消费方通过 BindAndBuild 绑定本会话的队伍识别结果并构建全新节点实例的树
/// </summary>
public sealed record ComboTreeSession
{
    /// <summary>CombatScenes 黑板键名，须与各行为节点 [BlackboardKey] 标注的属性名一致</summary>
    private const string CombatScenesKeyName = "CombatScenes";

    /// <summary>行为树构建器，重复 Build 每次产出全新节点实例</summary>
    public required AutoComboBuildBuilder Builder { get; init; }

    /// <summary>建树时使用的黑板，与构建器绑定</summary>
    public required Blackboard Blackboard { get; init; }

    /// <summary>建树时的队伍角色名，供绑定校验树与当前队伍是否匹配</summary>
    public required IReadOnlyList<string> TeamNames { get; init; }

    /// <summary>建树完成时间</summary>
    public DateTimeOffset BuiltAt { get; init; }

    /// <summary>
    /// 绑定本次运行的队伍会话并构建全新行为树：校验队伍 → 清空黑板 → 授权写入 CombatScenes → Build
    /// 清空黑板会连带清除访问授权，因此每次绑定都重新授权
    /// CombatScenes 的识别与 BeforeTask/AfterTask 生命周期由消费方负责
    /// </summary>
    public Behaviour BindAndBuild(CombatScenes combatScenes)
    {
        var currentNames = combatScenes.GetAvatars().Select(a => a.Name).ToList();
        var missing = TeamNames.Except(currentNames).ToList();
        var extra = currentNames.Except(TeamNames).ToList();
        if (missing.Count > 0 || extra.Count > 0)
        {
            throw new Exception(
                $"建树队伍与当前队伍不一致，请重新运行自动连招建树" +
                $"（建树时：{string.Join("、", TeamNames)}；当前：{string.Join("、", currentNames)}）");
        }

        Blackboard.Clear();
        Blackboard.GrantExclusiveWrite<CombatScenes>(null!, CombatScenesKeyName).Set(combatScenes);
        return Builder.Build();
    }
}
