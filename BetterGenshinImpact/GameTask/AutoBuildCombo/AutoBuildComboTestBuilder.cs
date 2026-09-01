using CsTrees.FluentBuilder;

namespace BetterGenshinImpact.GameTask.AutoBuildCombo;

/// <summary>
/// 连招行为树测试用流式构建器
/// 仅用于手工扩展树，不参与 LLM 建树工具生成
/// </summary>
public partial class AutoBuildComboTestBuilder : TreeBuilder<AutoBuildComboTestBuilder>
{
    private static readonly CompositesCatalog compositesCatalog = new();
    private static readonly AutoBuildComboTestCatalog autoBuildComboTestCatalog = new();
}
