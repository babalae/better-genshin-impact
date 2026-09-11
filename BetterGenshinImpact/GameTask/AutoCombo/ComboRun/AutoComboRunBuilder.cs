using CsTrees.FluentBuilder;

namespace BetterGenshinImpact.GameTask.AutoCombo.ComboRun;

/// <summary>
/// 连招行为树测试用流式构建器
/// 仅用于手工扩展树，不参与 LLM 建树工具生成
/// </summary>
public partial class AutoComboRunBuilder : TreeBuilder<AutoComboRunBuilder>
{
    private static readonly CompositesCatalog compositesCatalog = new();
    private static readonly AutoComboRunCatalog autoComboRunCatalog = new();
}
