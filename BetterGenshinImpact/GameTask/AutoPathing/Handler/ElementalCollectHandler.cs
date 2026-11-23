using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoFight.Config;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 元素采集
/// </summary>
public class ElementalCollectHandler(ElementalType elementalType) : IActionHandler
{
    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        var combatScenes = await RunnerContext.Instance.GetCombatScenes(ct);
        if (combatScenes == null)
        {
            Logger.LogError("队伍识别未初始化成功！");
            return;
        }

        // 筛选出对应元素的角色列表
        var elementalCollectAvatars = ElementalCollectAvatarConfigs.Lists.Where(x => x.ElementalType == elementalType).ToList();
        // 循环遍历角色列表
        foreach (var combatScenesAvatar in combatScenes.GetAvatars())
        {
            // 判断是否为对应元素的角色
            var elementalCollectAvatar = elementalCollectAvatars.FirstOrDefault(x => x.Name == combatScenesAvatar.Name);
            if (elementalCollectAvatar == null)
            {
                continue;
            }

            // 切人
            if (combatScenesAvatar.TrySwitch())
            {
                if (elementalCollectAvatar.NormalAttack)
                {
                    combatScenesAvatar.Attack(100);
                }
                else if (elementalCollectAvatar.ElementalSkill)
                {

                    await combatScenesAvatar.WaitSkillCd(ct);
                    combatScenesAvatar.UseSkill();
                }
            }
            else
            {
                Logger.LogError("切人失败,无法进行{Element}元素采集", elementalType.ToChinese());
            }

            break;
        }
    }
}

public class ElementalCollectAvatar(string name, ElementalType elementalType, bool normalAttack, bool elementalSkill)
{
    public string Name { get; set; } = name;
    public ElementalType ElementalType { get; set; } = elementalType;
    public bool NormalAttack { get; set; } = normalAttack;

    public bool ElementalSkill { get; set; } = elementalSkill;

    // public CombatAvatar Info => DefaultAutoFightConfig.CombatAvatarMap[Name];

    public DateTime LastUseSkillTime { get; set; } = DateTime.MinValue;
}

public class ElementalCollectAvatarConfigs
{
    public static List<ElementalCollectAvatar> Lists { get; } = DefaultAutoFightConfig.CombatAvatarMap.Values
        .Where(x => x.ElementalType is not null && (x.NormalAttack || x.ElementalSkill))
        .Select(x => new ElementalCollectAvatar(x.Name, x.ElementalType!.Value, x.NormalAttack, x.ElementalSkill))
        .ToList();

    public static ElementalCollectAvatar? Get(string name, ElementalType type) => Lists.FirstOrDefault(x => x.Name == name && x.ElementalType == type);

    public static List<string> GetAvatarNameList(ElementalType type) => Lists.Where(x => x.ElementalType == type).Select(x => x.Name).ToList();
}
