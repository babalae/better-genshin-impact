using CsTrees.FluentBuilder;

namespace BetterGenshinImpact.GameTask.AutoCombo.ComboBuild;

public partial class AutoComboBuildBuilder : TreeBuilder<AutoComboBuildBuilder>
{
    private static readonly ClassicCompositesCatalog classicCompositesCatalog = new();
    private static readonly AutoComboBuildCatalog autoComboCatalog = new();
}
