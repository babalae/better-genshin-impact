using System;
using System.Collections.Generic;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Model;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 这个类是一个动作处理器工厂
/// 用于根据字符串标识动态创建和缓存特定类型的动作处理器（IActionHandler）      
/// 它在自动寻路或任务执行流程中，负责管理路径点前后需要执行的附加动作
/// Action handler factory for resolving specific navigation tasks.
/// </summary>
public static class ActionFactory
{
    private static readonly Dictionary<string, Func<IActionHandler>> AfterHandlerFactories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["nahida_collect"] = () => new NahidaCollectHandler(),
        ["pick_around"] = () => new PickAroundHandler(),
        ["fight"] = () => new AutoFightHandler(),
#pragma warning disable CS0618 // 保留旧路线动作的兼容性映射，执行时已由更高层配置替代。
        ["normal_attack"] = () => new NormalAttackHandler(),
        ["elemental_skill"] = () => new ElementalSkillHandler(),
#pragma warning restore CS0618
        ["hydro_collect"] = () => new ElementalCollectHandler(ElementalType.Hydro),
        ["electro_collect"] = () => new ElementalCollectHandler(ElementalType.Electro),
        ["anemo_collect"] = () => new ElementalCollectHandler(ElementalType.Anemo),
        ["pyro_collect"] = () => new ElementalCollectHandler(ElementalType.Pyro),
        ["combat_script"] = () => new CombatScriptHandler(),
        ["mining"] = () => new MiningHandler(),
        ["linnea_mining"] = () => new LinneaMiningHandler(),
        ["fishing"] = () => new FishingHandler(),
        ["exit_and_relogin"] = () => new ExitAndReloginHandler(),
        ["wonderland_cycle"] = () => new EnterAndExitWonderlandHandler(),
        ["set_time"] = () => new SetTimeHandler(),
        ["use_gadget"] = () => new UseGadgetHandler(),
        ["pick_up_collect"] = () => new PickUpCollectHandler(),
        ["stop_flying"] = () => new StopFlyingHandler()
    };

    private static readonly Dictionary<string, Func<IActionHandler>> BeforeHandlerFactories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["up_down_grab_leaf"] = () => new UpDownGrabLeafHandler(),
        ["log_output"] = () => new LogOutputHandler()
    };

    /// <summary>
    /// 获取在目标点后执行的动作处理器。 / Gets the execution handler triggered post-waypoint.
    /// </summary>
    /// <param name="handlerType">处理器类型标识。 / Handler type identifier.</param>
    /// <returns>处理器实例，未匹配则返回 null。 / Returns the matched handler, or null if no match is found.</returns>
    public static IActionHandler? GetAfterHandler(string handlerType)
    {
        if (string.IsNullOrWhiteSpace(handlerType))
            return null;

        return AfterHandlerFactories.TryGetValue(handlerType, out var factory) ? factory() : null;
    }

    /// <summary>
    /// 获取在前往目标点前执行的预置动作处理器。 
    ///  Gets the execution handler triggered pre-waypoint.
    /// </summary>
    /// <param name="handlerType">处理器类型标识。 
    ///  Handler type identifier.</param>
    /// <returns>处理器实例，未匹配则返回 null。 
    ///  Returns the matched handler, or null if no match is found.</returns>
    public static IActionHandler? GetBeforeHandler(string handlerType)
    {
        if (string.IsNullOrWhiteSpace(handlerType))
            return null;

        return BeforeHandlerFactories.TryGetValue(handlerType, out var factory) ? factory() : null;
    }
}
