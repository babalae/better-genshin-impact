using CsTrees.Blackboard;
using CsTrees.FluentBuilder;

namespace BetterGenshinImpact.GameTask.AutoCombo.ComboRun;

/// <summary>
/// 连招行为树测试专用目录
/// 仅挂载在 AutoComboRunBuilder 上，不进入 AutoComboBuildBuilder 的 LLM 建树词汇表
/// </summary>
public class AutoComboRunCatalog : IBehaviourCatalog
{
    public CheckFightFinish CheckFightFinish(string name, Blackboard blackboard) => new(name, blackboard);
}
