using BetterGenshinImpact.Helpers;
﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
            Logger.LogError(Lang.S["GameTask_11074_f6bb4a"]);
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
                Logger.LogError(Lang.S["GameTask_11109_492012"], elementalType.ToChinese());
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
        new ElementalCollectAvatar(Lang.S["Gen_10031_8325c6"], ElementalType.Hydro, true, true),
        new ElementalCollectAvatar(Lang.S["GameTask_10944_06bb01"], ElementalType.Hydro, true, false),
        new ElementalCollectAvatar(Lang.S["GameTask_11037_10ecec"], ElementalType.Hydro, true, true),
        new ElementalCollectAvatar(Lang.S["GameTask_11108_c3a06c"], ElementalType.Hydro, true, false),
        new ElementalCollectAvatar(Lang.S["GameTask_10591_86ddf3"], ElementalType.Hydro, true, true),
        new ElementalCollectAvatar(Lang.S["GameTask_11107_9a060b"], ElementalType.Hydro, true, false),
        new ElementalCollectAvatar(Lang.S["GameTask_11106_6c0548"], ElementalType.Hydro, false, true),
        new ElementalCollectAvatar(Lang.S["GameTask_11105_3ecf1e"], ElementalType.Hydro, false, true),
        new ElementalCollectAvatar(Lang.S["GameTask_11104_a02a8a"], ElementalType.Hydro, false, true),
        new ElementalCollectAvatar(Lang.S["GameTask_11103_b8b9bf"], ElementalType.Hydro, false, true),
        // 雷
        new ElementalCollectAvatar(Lang.S["GameTask_11102_3ca672"], ElementalType.Electro, true, true),
        new ElementalCollectAvatar(Lang.S["GameTask_11101_c91110"], ElementalType.Electro, true, false),
        new ElementalCollectAvatar(Lang.S["GameTask_11100_c38bd9"], ElementalType.Electro, true, false),
        new ElementalCollectAvatar(Lang.S["GameTask_11099_6df33c"], ElementalType.Electro, false, true),
        new ElementalCollectAvatar(Lang.S["GameTask_11098_6692b6"], ElementalType.Electro, false, true),
        new ElementalCollectAvatar(Lang.S["GameTask_11097_e1ff99"], ElementalType.Electro, false, true),
        new ElementalCollectAvatar(Lang.S["GameTask_11096_356f7d"], ElementalType.Electro, false, true),
        new ElementalCollectAvatar(Lang.S["GameTask_11095_ce6899"], ElementalType.Electro, false, true),
        // 风
        new ElementalCollectAvatar(Lang.S["GameTask_11094_079f04"], ElementalType.Anemo, true, true),
        new ElementalCollectAvatar(Lang.S["GameTask_11093_a32f23"], ElementalType.Anemo, true, true),
        new ElementalCollectAvatar(Lang.S["GameTask_10611_82263e"], ElementalType.Anemo, true, false),
        new ElementalCollectAvatar(Lang.S["GameTask_11092_e96489"], ElementalType.Anemo, true, false),
        new ElementalCollectAvatar(Lang.S["GameTask_11091_83fff4"], ElementalType.Anemo, true, false),
        new ElementalCollectAvatar(Lang.S["GameTask_10547_4afc93"], ElementalType.Anemo, false, true),
        new ElementalCollectAvatar(Lang.S["GameTask_11090_ac9b5a"], ElementalType.Anemo, false, true),
        new ElementalCollectAvatar(Lang.S["GameTask_11089_900bf6"], ElementalType.Anemo, false, true),
        new ElementalCollectAvatar(Lang.S["GameTask_11088_502f3c"], ElementalType.Anemo, false, true),
        new ElementalCollectAvatar("琴", ElementalType.Anemo, false, true),
        new ElementalCollectAvatar(Lang.S["GameTask_11087_08072d"], ElementalType.Anemo, false, true),
        // 火
        new ElementalCollectAvatar(Lang.S["GameTask_11086_da7e31"], ElementalType.Pyro, true, true),
        new ElementalCollectAvatar(Lang.S["GameTask_11085_a8b465"], ElementalType.Pyro, false,true),
        new ElementalCollectAvatar(Lang.S["GameTask_11084_3a02e2"], ElementalType.Pyro, true, true),
        new ElementalCollectAvatar(Lang.S["GameTask_11083_6aa4bb"], ElementalType.Pyro, false, true),
        new ElementalCollectAvatar(Lang.S["GameTask_11082_0e3063"], ElementalType.Pyro, false, true),
        new ElementalCollectAvatar(Lang.S["GameTask_11081_2a1c3c"], ElementalType.Pyro,false, true),
        new ElementalCollectAvatar(Lang.S["GameTask_11080_f7df9d"], ElementalType.Pyro, false, true),
        new ElementalCollectAvatar(Lang.S["Gen_10025_df2eea"], ElementalType.Pyro, false, true),
        new ElementalCollectAvatar(Lang.S["GameTask_11079_f7fc52"], ElementalType.Pyro, false, true),
        new ElementalCollectAvatar(Lang.S["GameTask_11078_16ebbc"], ElementalType.Pyro, false, true),
        new ElementalCollectAvatar(Lang.S["GameTask_11077_da8448"], ElementalType.Pyro, false, true),
        new ElementalCollectAvatar(Lang.S["GameTask_11076_539d56"], ElementalType.Pyro, false, true),
    ];

    public static ElementalCollectAvatar? Get(string name, ElementalType type) => Lists.FirstOrDefault(x => x.Name == name && x.ElementalType == type);

    public static List<string> GetAvatarNameList(ElementalType type) => Lists.Where(x => x.ElementalType == type).Select(x => x.Name).ToList();
}
