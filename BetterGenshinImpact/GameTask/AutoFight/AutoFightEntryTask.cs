using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoFight.Factory;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.GameTask.SoloTask;

namespace BetterGenshinImpact.GameTask.AutoFight;

/// <summary>
/// 自动战斗的统一入口：按参数中的策略类型选择 txt、JSON 或连招实现，并把参数原样交给具体实现运行
/// </summary>
[SoloTaskInfo(TaskId, "自动战斗", Group = "树脂与战斗", Order = 250)]
public class AutoFightEntryTask : ISoloTask<AutoFightParam, NoData>
{
    /// <summary>
    /// 任务稳定标识
    /// </summary>
    public const string TaskId = "auto_fight";

    /// <summary>
    /// 本次战斗所在的运行环境
    /// </summary>
    private readonly GameRuntime _runtime;

    /// <summary>
    /// 配置根
    /// </summary>
    private readonly AllConfig _config;

    /// <summary>
    /// 创建自动战斗入口任务
    /// </summary>
    /// <param name="runtime">本次战斗所在的运行环境</param>
    /// <param name="config">配置根</param>
    public AutoFightEntryTask(GameRuntime runtime, AllConfig config)
    {
        _runtime = runtime;
        _config = config;
    }

    /// <summary>
    /// 解析策略后选择对应的战斗实现并运行。策略不存在时返回 Failed(strategy_not_found)；
    /// 具体实现的结果和异常（包括取消）原样向上传递
    /// </summary>
    /// <param name="param">本次战斗参数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>具体战斗实现的结果</returns>
    public async Task<SoloTaskResult<NoData>> RunAsync(AutoFightParam param, CancellationToken ct)
    {
        // 参数里可能保存策略名（任务计划）或路径（旧入口、JS），统一解析为路径并校验存在性
        if (!CombatStrategyResolver.TryResolve(param.CombatStrategyPath, out var strategyPath))
        {
            return SoloTaskResult.Failed(SoloTaskReasons.StrategyNotFound,
                $"战斗策略不存在：{param.CombatStrategyPath}");
        }

        // 具体实现按 CombatStrategyPath 读取策略，这里写回解析后的路径
        param.CombatStrategyPath = strategyPath;

        // 按策略类型（连招固定名 / .json / .txt 或目录）选择工厂，创建实现并运行
        var factory = CombatTaskFactoryProvider.GetFactory(param.CombatStrategyPath);
        return await factory.CreateTask(_runtime, _config).RunAsync(param, ct);
    }
}
