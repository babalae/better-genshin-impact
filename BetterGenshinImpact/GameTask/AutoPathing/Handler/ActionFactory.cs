using System;
using System.Collections.Concurrent;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Model;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Runtime;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 路径点动作处理器工厂。处理器持有运行环境，缓存只在本工厂实例内有效（由 PathExecutor 每次运行创建）
/// </summary>
/// <param name="runtime">本次运行使用的运行环境</param>
/// <param name="config">配置根</param>
public class ActionFactory(GameRuntime runtime, AllConfig config)
{
    /// <summary>
    /// 本次运行使用的运行环境
    /// </summary>
    private readonly GameRuntime _runtime = runtime;

    /// <summary>
    /// 配置根
    /// </summary>
    private readonly AllConfig _config = config;

    /// <summary>
    /// 已创建的处理器缓存，键为动作类型。处理器绑定了 <see cref="_runtime"/>，不能跨运行环境共享，所以不再是静态缓存
    /// </summary>
    private readonly ConcurrentDictionary<string, IActionHandler> _handlers = new();

    public IActionHandler GetAfterHandler(string handlerType)
    {
        return _handlers.GetOrAdd(handlerType, (key) =>
        {
            return key switch
            {
                "nahida_collect" => new NahidaCollectHandler(_runtime, _config),
                "pick_around" => new PickAroundHandler(_runtime),
                "fight" => new AutoFightHandler(_runtime, _config),
                "normal_attack" => new NormalAttackHandler(_runtime),
                "elemental_skill" => new ElementalSkillHandler(_runtime),
                "hydro_collect" => new ElementalCollectHandler(_runtime, _config, ElementalType.Hydro),
                "electro_collect" => new ElementalCollectHandler(_runtime, _config, ElementalType.Electro),
                "anemo_collect" => new ElementalCollectHandler(_runtime, _config, ElementalType.Anemo),
                "pyro_collect" => new ElementalCollectHandler(_runtime, _config, ElementalType.Pyro),
                "combat_script" => new CombatScriptHandler(_runtime, _config),
                "mining" => new MiningHandler(_runtime, _config),
                "linnea_mining" => new LinneaMiningHandler(_runtime, _config),
                "fishing" => new FishingHandler(_runtime, _config),
                "exit_and_relogin" => new ExitAndReloginHandler(_runtime),
                "wonderland_cycle" => new EnterAndExitWonderlandHandler(_runtime),
                "set_time" => new SetTimeHandler(_runtime),
                "use_gadget" => new UseGadgetHandler(_runtime),
                "pick_up_collect" => new PickUpCollectHandler(_runtime, _config),
                _ => throw new ArgumentException("未知的后置 action 类型")
            };
        });
    }

    public IActionHandler GetBeforeHandler(string handlerType)
    {
        return _handlers.GetOrAdd(handlerType, (key) =>
        {
            return key switch
            {
                "up_down_grab_leaf" => new UpDownGrabLeafHandler(_runtime),
                "stop_flying" => new StopFlyingHandler(_runtime),
                _ => throw new ArgumentException("未知的前置 action 类型")
            };
        });
    }
}
