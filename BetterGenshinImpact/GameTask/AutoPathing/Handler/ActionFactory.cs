using BetterGenshinImpact.Helpers;
﻿using System;
using System.Collections.Concurrent;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Model;
using BetterGenshinImpact.GameTask.Common.Job;

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
                "wonderland_cycle" => new EnterAndExitWonderlandHandler(),
                "set_time" => new SetTimeHandler(),
                "use_gadget" => new UseGadgetHandler(),
                "pick_up_collect" => new PickUpCollectHandler(),
                _ => throw new ArgumentException(Lang.S["GameTask_11066_d7a553"])
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
                _ => throw new ArgumentException(Lang.S["GameTask_11065_e8ee59"])
            };
        });
    }
}
