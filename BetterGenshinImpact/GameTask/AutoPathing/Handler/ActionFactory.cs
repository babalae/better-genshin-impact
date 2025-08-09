using System;
using System.Collections.Concurrent;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Model;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

public class ActionFactory
{
    private static readonly ConcurrentDictionary<string, IActionHandler> _handlers = new();

    public static IActionHandler GetAfterHandler(string handlerType)
    {
        return _handlers.GetOrAdd(handlerType, (key) =>
        {
            return key switch
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
                "set_time" => new SetTimeHandler(),
                "use_gadget" => new UseGadgetHandler(),
                _ => throw new ArgumentException("未知的后置 action 类型")
            };
        });
    }

    public static IActionHandler GetBeforeHandler(string handlerType)
    {
        return _handlers.GetOrAdd(handlerType, (key) =>
        {
            return key switch
            {
                "up_down_grab_leaf" => new UpDownGrabLeafHandler(),
                "stop_flying" => new StopFlyingHandler(),
                _ => throw new ArgumentException("未知的前置 action 类型")
            };
        });
    }
}
