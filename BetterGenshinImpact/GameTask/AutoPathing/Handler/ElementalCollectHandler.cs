using System;
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
/// 处理特定元素采集（Elemental Collect）动作的执行逻辑。
/// </summary>
/// <param name="elementalType">需要进行采集的目标元素类型（如水、雷、风、火等）。</param>
/// <remarks>
/// 用于在自动寻路或采集过程中，自动从队伍中筛选符合指定元素类型的角色，
/// 切人后根据预设配置，通过普通攻击或元素战技对采集物（如烈焰花、冰雾花、蒲公英籽等）进行元素附着以实现采集交互。
/// </remarks>
public class ElementalCollectHandler(ElementalType elementalType) : IActionHandler
{
    /// <summary>
    /// 异步执行元素采集逻辑。
    /// </summary>
    /// <param name="ct">异步操作取消令牌（CancellationToken）。</param>
    /// <param name="waypointForTrack">触发元素采集的主动航点（Waypoint）上下文。</param>
    /// <param name="config">透传的采集配置对象（当前实现中暂未使用）。</param>
    /// <returns>代表动作流转过程的任务实例。</returns>
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

/// <summary>
/// 封装针对此元素采集场景所适配的角色参数字典实体。
/// </summary>
/// <param name="name">角色在系统内部校验使用的基础名称。</param>
/// <param name="elementalType">该角色所属的神之眼元素界定属性。</param>
/// <param name="normalAttack">是否推荐通过普通攻击（NormalAttack）来进行即时元素附着。</param>
/// <param name="elementalSkill">是否推荐通过元素战技（ElementalSkill）来进行即时元素附着。</param>
/// <remarks>
/// 主要用于明确指定角色上元素的最高效方式。例如：法器角色大多推荐基于普攻实现零冷却上元素，而近战角色推荐使用前摇较短的战技。
/// </remarks>
public class ElementalCollectAvatar(string name, ElementalType elementalType, bool normalAttack, bool elementalSkill)
{
    /// <summary>获取或设置当前角色名称标识。</summary>
    public string Name { get; set; } = name;
    
    /// <summary>获取或设置该角色所属的神之眼属性范围（ElementalType）。</summary>
    public ElementalType ElementalType { get; set; } = elementalType;
    
    /// <summary>获取或设置触发标识：使用普通攻击为采集对象附加元素。</summary>
    public bool NormalAttack { get; set; } = normalAttack;

    /// <summary>获取或设置触发标识：使用元素战技为采集对象附加元素。</summary>
    public bool ElementalSkill { get; set; } = elementalSkill;

    // public CombatAvatar Info => DefaultAutoFightConfig.CombatAvatarMap[Name];

    /// <summary>获取或设置该角色的上一次战技释放时间戳，用于防抖或内部计算 CD。</summary>
    public DateTime LastUseSkillTime { get; set; } = DateTime.MinValue;
}

/// <summary>
/// 管理和提供内置的元素采集角色环境配置（ElementalCollectAvatar）预设项静态池。
/// </summary>
public class ElementalCollectAvatarConfigs
{
    /// <summary>获取或设置游戏中可有效支撑野外采集互动交互的已知角色序列全集。</summary>
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
        new ElementalCollectAvatar("瓦雷莎", ElementalType.Electro, true, false),
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
        new ElementalCollectAvatar("蓝砚", ElementalType.Anemo, true, false),
        new ElementalCollectAvatar("枫原万叶", ElementalType.Anemo, false, true),
        new ElementalCollectAvatar("珐露珊", ElementalType.Anemo, false, true),
        new ElementalCollectAvatar("琳妮特", ElementalType.Anemo, false, true),
        new ElementalCollectAvatar("温迪", ElementalType.Anemo, false, true),
        new ElementalCollectAvatar("琴", ElementalType.Anemo, false, true),
        new ElementalCollectAvatar("早柚", ElementalType.Anemo, false, true),
        // 火
        new ElementalCollectAvatar("烟绯", ElementalType.Pyro, true, true),
        new ElementalCollectAvatar("迪卢克", ElementalType.Pyro, false,true),
        new ElementalCollectAvatar("可莉", ElementalType.Pyro, true, true),
        new ElementalCollectAvatar("班尼特", ElementalType.Pyro, false, true),
        new ElementalCollectAvatar("香菱", ElementalType.Pyro, false, true),
        new ElementalCollectAvatar("托马", ElementalType.Pyro,false, true),
        new ElementalCollectAvatar("胡桃", ElementalType.Pyro, false, true),
        new ElementalCollectAvatar("迪希雅", ElementalType.Pyro, false, true),
        new ElementalCollectAvatar("夏沃蕾", ElementalType.Pyro, false, true),
        new ElementalCollectAvatar("辛焱", ElementalType.Pyro, false, true),
        new ElementalCollectAvatar("林尼", ElementalType.Pyro, false, true),
        new ElementalCollectAvatar("宵宫", ElementalType.Pyro, false, true),
    ];

    /// <summary>
    /// 基于全局角色库及限定的元素类型尝试配对对应的有效采集配置。
    /// </summary>
    /// <param name="name">需检索判定的角色本地化名称。</param>
    /// <param name="type">筛选范围依据的 <see cref="ElementalType"/> 元素类型约束。</param>
    /// <returns>若匹配成功则返回有效的 <see cref="ElementalCollectAvatar"/> 实例，否则返回空（<c>null</c>）。</returns>
    public static ElementalCollectAvatar? Get(string name, ElementalType type) => Lists.FirstOrDefault(x => x.Name == name && x.ElementalType == type);

    /// <summary>
    /// 单独基于目标元素类别抽取完全适配此条件的注册交互角色名单集合。
    /// </summary>
    /// <param name="type">限制查询对象的元素种类。</param>
    /// <returns>从指定 <see cref="ElementalType"/> 中所筛选出来的等效角色注册名称列表。</returns>
    public static List<string> GetAvatarNameList(ElementalType type) => Lists.Where(x => x.ElementalType == type).Select(x => x.Name).ToList();
}
