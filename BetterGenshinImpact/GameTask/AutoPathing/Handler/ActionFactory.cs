using System;
using System.Collections.Concurrent;

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
                "up_down_grab_leaf" => new UpDownGrabLeaf(),
                _ => throw new ArgumentException("未知的前置 action 类型")
            };
        });
    }
}
