using System;
using System.Collections.Concurrent;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Model;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 负责管理与解析路径执行中各类动作处理器的类工厂。
/// Action handler factory for resolving specific navigation tasks.
/// </summary>
public static class ActionFactory
{
    private static readonly ConcurrentDictionary<string, IActionHandler> AfterHandlers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, IActionHandler> BeforeHandlers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 获取在目标点后执行的动作处理器。 / Gets the execution handler triggered post-waypoint.
    /// </summary>
    /// <param name="handlerType">处理器类型标识。 / Handler type identifier.</param>
    /// <returns>处理器实例，未匹配则返回 null。 / Returns the matched handler, or null if no match is found.</returns>
    public static IActionHandler? GetAfterHandler(string handlerType)
    {
        if (string.IsNullOrWhiteSpace(handlerType))
            return null;

        return AfterHandlers.GetOrAdd(handlerType, CreateAfterHandler);
    }

    /// <summary>
    /// 获取在前往目标点前执行的预置动作处理器。 / Gets the execution handler triggered pre-waypoint.
    /// </summary>
    /// <param name="handlerType">处理器类型标识。 / Handler type identifier.</param>
    /// <returns>处理器实例，未匹配则返回 null。 / Returns the matched handler, or null if no match is found.</returns>
    public static IActionHandler? GetBeforeHandler(string handlerType)
    {
        if (string.IsNullOrWhiteSpace(handlerType))
            return null;

        return BeforeHandlers.GetOrAdd(handlerType, CreateBeforeHandler);
    }

    private static IActionHandler? CreateAfterHandler(string key)
    {
        return key.ToLowerInvariant() switch
        {
            "nahida_collect" => new NahidaCollectHandler(),
            "pick_around" => new PickAroundHandler(),
            "fight" => new AutoFightHandler(),
            "normal_attack" => new NormalAttackHandler(),
            "elemental_skill" => new ElementalSkillHandler(),
            "hydro_collect" => new ElementalCollectHandler(ElementalType.Hydro),
            "electro_collect" => new ElementalCollectHandler(ElementalType.Electro),
            "anemo_collect" => new ElementalCollectHandler(ElementalType.Anemo),
            "pyro_collect" => new ElementalCollectHandler(ElementalType.Pyro),
            "combat_script" => new CombatScriptHandler(),
            "mining" => new MiningHandler(),
            "fishing" => new FishingHandler(),
            "exit_and_relogin" => new ExitAndReloginHandler(),
            "wonderland_cycle" => new EnterAndExitWonderlandHandler(),
            "set_time" => new SetTimeHandler(),
            "use_gadget" => new UseGadgetHandler(),
            "pick_up_collect" => new PickUpCollectHandler(),
            _ => null
        };
    }

    private static IActionHandler? CreateBeforeHandler(string key)
    {
        return key.ToLowerInvariant() switch
        {
            "up_down_grab_leaf" => new UpDownGrabLeafHandler(),
            "stop_flying" => new StopFlyingHandler(),
            _ => null
        };
    }
}
