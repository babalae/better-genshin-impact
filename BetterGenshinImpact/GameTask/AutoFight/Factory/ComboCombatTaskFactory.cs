using BetterGenshinImpact.GameTask.AutoCombo.ComboRun;

namespace BetterGenshinImpact.GameTask.AutoFight.Factory;

/// <summary>
/// 自动连招（LLM 行为树）工厂 — 创建 AutoComboRunTask
/// 不对应任何策略文件：策略名为 AutoFightParam.ComboStrategyName 时路由到此，
/// 消费 AutoComboRuntime 中已构建的建树会话（尚未建树时由任务自身报错终止）
/// </summary>
public class ComboCombatTaskFactory : ICombatTaskFactory
{
    public ISoloTask CreateTask(AutoFightParam param)
    {
        return new AutoComboRunTask();
    }

    public bool CanHandle(string strategyPath)
    {
        return AutoFightParam.ComboStrategyName.Equals(strategyPath);
    }
}
