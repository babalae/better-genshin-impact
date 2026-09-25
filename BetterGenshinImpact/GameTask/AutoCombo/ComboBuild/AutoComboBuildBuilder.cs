using BetterGenshinImpact.GameTask.AutoFight.Model;
using CsTrees.FluentBuilder;

namespace BetterGenshinImpact.GameTask.AutoCombo.ComboBuild;

public partial class AutoComboBuildBuilder : TreeBuilder<AutoComboBuildBuilder>
{
    private static readonly ClassicCompositesCatalog classicCompositesCatalog = new();

    private readonly AutoComboBuildCatalog autoComboCatalog;

    public AutoComboBuildBuilder(Avatar[] avatars)
    {
        autoComboCatalog = new AutoComboBuildCatalog(avatars);
    }
}
