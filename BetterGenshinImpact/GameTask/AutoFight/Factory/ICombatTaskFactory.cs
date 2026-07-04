namespace BetterGenshinImpact.GameTask.AutoFight.Factory;

/// <summary>
/// 战斗策略工厂接口
/// </summary>
public interface ICombatTaskFactory
{
    /// <summary>
    /// 根据策略文件路径创建对应的战斗 Task
    /// </summary>
    ISoloTask CreateTask(AutoFightParam param);

    /// <summary>
    /// 判断本工厂是否可以处理指定路径的策略文件
    /// </summary>
    bool CanHandle(string strategyPath);
}
