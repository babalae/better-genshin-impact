using CsTrees.FluentBuilder;

namespace BetterGenshinImpact.GameTask.AutoFishing
{
    public partial class AutoFishingBuilder : TreeBuilder<AutoFishingBuilder>
    {
        private static readonly CompositesCatalog compositesCatalog = new();
        private static readonly DecoratorsCatalog decoratorsCatalog = new();
        private static readonly AutoFishingTaskCatalog autoFishingTaskCatalog = new();
    }
}
