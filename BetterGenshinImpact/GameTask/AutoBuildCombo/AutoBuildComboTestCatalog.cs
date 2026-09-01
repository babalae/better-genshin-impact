using CsTrees.Blackboard;
using CsTrees.FluentBuilder;

namespace BetterGenshinImpact.GameTask.AutoBuildCombo;

/// <summary>
/// 连招行为树测试专用目录
/// 仅挂载在 AutoBuildComboTestBuilder 上，不进入 AutoBuildComboBuilder 的 LLM 建树词汇表
/// </summary>
public class AutoBuildComboTestCatalog : IBehaviourCatalog
{
    public CheckFightFinish CheckFightFinish(string name, Blackboard blackboard) => new(name, blackboard);
}
