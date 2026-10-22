using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.GameTask.SoloTask;

namespace BetterGenshinImpact.GameTask.AutoFight.Factory;

/// <summary>
/// 战斗策略工厂接口
/// </summary>
public interface ICombatTaskFactory
{
    /// <summary>
    /// 创建对应策略类型的战斗任务；战斗参数在调用任务的 RunAsync 时传入
    /// </summary>
    /// <param name="runtime">战斗任务所在的运行环境</param>
    /// <param name="config">配置根</param>
    /// <returns>以 <see cref="AutoFightParam"/> 为参数的战斗任务</returns>
    ISoloTask<AutoFightParam, NoData> CreateTask(GameRuntime runtime, AllConfig config);

    /// <summary>
    /// 判断本工厂是否可以处理指定路径的策略文件
    /// </summary>
    bool CanHandle(string strategyPath);
}
