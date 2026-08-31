using CsTrees.FluentBuilder;

namespace BetterGenshinImpact.GameTask.AutoBuildCombo;

public partial class AutoBuildComboBuilder : TreeBuilder<AutoBuildComboBuilder>
{
    private static readonly ClassicCompositesCatalog classicCompositesCatalog = new();
    private static readonly AutoBuildComboCatalog autoBuildComboCatalog = new();
}
