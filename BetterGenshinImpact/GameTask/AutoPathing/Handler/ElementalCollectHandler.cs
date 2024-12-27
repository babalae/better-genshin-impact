using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
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
        foreach (var combatScenesAvatar in combatScenes.Avatars)
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
                    // 获取CD时间
                    var cdTime = combatScenesAvatar.SkillCd * 1000;
                    var cdRemain = (DateTime.UtcNow - elementalCollectAvatar.LastUseSkillTime).TotalMilliseconds;
                    if (cdRemain < cdTime)
                    {
                        var ms = (int)Math.Ceiling(cdTime - cdRemain) + 100;
                        Logger.LogInformation("{}的E技能CD未结束，等待{Milliseconds}ms", combatScenesAvatar.Name, ms);
                        await Delay(ms, ct);
                    }

                    combatScenesAvatar.UseSkill();
                    elementalCollectAvatar.LastUseSkillTime = DateTime.UtcNow;
                }
            }
            else
            {
                Logger.LogError("切人失败,无法进行{Element}元素采集", elementalType.ToString());
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
    public static List<ElementalCollectAvatar> Lists { get; set; } =
    [
        // 水
        new ElementalCollectAvatar("芭芭拉", ElementalType.Hydro, true, true),
        new ElementalCollectAvatar("莫娜", ElementalType.Hydro, true, false),
        new ElementalCollectAvatar("珊瑚宫心海", ElementalType.Hydro, true, true),
        new ElementalCollectAvatar("玛拉妮", ElementalType.Hydro, true, false),
        new ElementalCollectAvatar("那维莱特", ElementalType.Hydro, true, true),
        new ElementalCollectAvatar("芙宁娜", ElementalType.Hydro, true, false),
        new ElementalCollectAvatar("妮露", ElementalType.Hydro, false, true),
        new ElementalCollectAvatar("坎蒂斯", ElementalType.Hydro, false, true),
        new ElementalCollectAvatar("行秋", ElementalType.Hydro, false, true),
        new ElementalCollectAvatar("神里绫人", ElementalType.Hydro, false, true),
        // 雷
        new ElementalCollectAvatar("丽莎", ElementalType.Electro, true, true),
        new ElementalCollectAvatar("八重神子", ElementalType.Electro, true, false),
        new ElementalCollectAvatar("雷电将军", ElementalType.Electro, false, true),
        new ElementalCollectAvatar("久岐忍", ElementalType.Electro, false, true),
        new ElementalCollectAvatar("北斗", ElementalType.Electro, false, true),
        new ElementalCollectAvatar("菲谢尔", ElementalType.Electro, false, true),
        new ElementalCollectAvatar("雷泽", ElementalType.Electro, false, true),
        // 风
        new ElementalCollectAvatar("砂糖", ElementalType.Anemo, true, true),
        new ElementalCollectAvatar("鹿野院平藏", ElementalType.Anemo, true, true),
        new ElementalCollectAvatar("流浪者", ElementalType.Anemo, true, false),
        new ElementalCollectAvatar("闲云", ElementalType.Anemo, true, false),
        new ElementalCollectAvatar("枫原万叶", ElementalType.Anemo, false, true),
        new ElementalCollectAvatar("珐露珊", ElementalType.Anemo, false, true),
        new ElementalCollectAvatar("琳妮特", ElementalType.Anemo, false, true),
        new ElementalCollectAvatar("温迪", ElementalType.Anemo, false, true),
        new ElementalCollectAvatar("琴", ElementalType.Anemo, false, true),
    ];

    public static ElementalCollectAvatar? Get(string name, ElementalType type) => Lists.FirstOrDefault(x => x.Name == name && x.ElementalType == type);

    public static List<string> GetAvatarNameList(ElementalType type) => Lists.Where(x => x.ElementalType == type).Select(x => x.Name).ToList();
}
