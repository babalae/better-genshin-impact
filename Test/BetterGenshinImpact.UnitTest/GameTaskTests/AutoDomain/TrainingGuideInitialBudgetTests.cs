using BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoDomain;

public class TrainingGuideInitialBudgetTests
{
    [Fact]
    public void FixedBudget_ConsumesActualResinWithoutChangingInventory()
    {
        var material = TrainingGuideMaterialCatalog.Materials.First(m => m.Tier == 0);
        var reading = new TrainingGuideMaterialReading(material, 0, 22, true);
        var plan = new TrainingGuideFamilyPlan(new[] { reading });
        var initial = plan.RemainingInitialResin(0)!.Value;
        Assert.True(initial >= 120);

        foreach (var resin in new[] { 20, 40, 60 })
            plan.ApplyRewards(null, resin);

        Assert.Equal(initial - 120, plan.RemainingInitialResin(0));
        Assert.Equal(reading, Assert.Single(plan.Materials));
        plan.ApplyRewards(null, initial);
        Assert.Equal(0, plan.RemainingInitialResin(0));
    }

    [Fact]
    public void FixedBudget_RemainsTheInitialEstimateAfterRefresh()
    {
        var material = TrainingGuideMaterialCatalog.Materials.First(m => m.Tier == 0);
        var reading = new TrainingGuideMaterialReading(material, 0, 22, true);
        var plan = new TrainingGuideFamilyPlan(new[] { reading });
        var initial = plan.RemainingInitialResin(0)!.Value;
        plan.ApplyRewards(null, 40);
        plan.Refresh(new[] { reading with { Stock = 22 } });

        Assert.Equal(initial - 40, plan.RemainingInitialResin(0));
        Assert.Equal(0, plan.RemainingResin(0));
    }
}
