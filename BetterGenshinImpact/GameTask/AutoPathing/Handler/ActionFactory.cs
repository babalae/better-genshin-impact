using System;
using System.Collections.Concurrent;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

public class ActionFactory
{
    private static readonly ConcurrentDictionary<string, IActionHandler> _handlers = new();

    public static IActionHandler GetHandler(string handlerType)
    {
        return _handlers.GetOrAdd(handlerType, (key) =>
        {
            return key switch
            {
                "nahida_collect" => new NahidaCollectHandler(),
                "pick_around" => new PickAroundHandler(),
                _ => throw new ArgumentException("未知的 action 类型")
            };
        });
    }
}
