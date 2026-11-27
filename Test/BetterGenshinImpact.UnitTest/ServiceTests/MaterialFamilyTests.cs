using BetterGenshinImpact.Core.Material;
using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.Service.AccountRecords.Model;

namespace BetterGenshinImpact.UnitTest.ServiceTests;

/// <summary>家族覆盖、共享分配、未知基准与不同达成依据的纯计算验证。</summary>
public sealed class MaterialFamilyTests
{
    /// <summary>多余的正增量不取消已有达标，但需要该增量或出现负增量时仍按实际可用数量判断。</summary>
    [Theory]
    [InlineData(12, 1, MaterialDemandState.Completed)]
    [InlineData(11, 1, MaterialDemandState.Pending)]
    [InlineData(12, -1, MaterialDemandState.Unmet)]
    public void FamilyConfirmationDependsOnRequiredDelta(long quantity, long delta, MaterialDemandState state)
    {
        var workspace = Workspace(("史莱姆凝液", quantity));
        workspace.Settings.DemandCraftingReservePercent = 0;
        workspace.Inventory.Events.Add(Delta("史莱姆凝液", delta));
        var progress = MaterialDemandPolicy.EvaluateAll(workspace, Demands(("史莱姆清", 4)))["史莱姆清"];
        Assert.Equal(state, progress.State);
        if (state == MaterialDemandState.Completed) Assert.Equal("Family", progress.ConfirmationMethod);
    }
    /// <summary>目录覆盖审核过的全部升级链，成员唯一，特殊材料不靠名称推断。</summary>
    [Fact]
    public void CatalogContainsAuditedFamiliesWithoutConversionOnlyItems()
    {
        var families = MaterialFamilyCatalog.Families;
        Assert.Equal(108, families.Count);
        var names = families.SelectMany(family => family.Members).ToArray();
        Assert.Equal(355, names.Length);
        Assert.Equal(names.Length, names.Distinct().Count());
        Assert.Equal(7, families.Count(family => !family.AllowsCraftingBonus));
        Assert.DoesNotContain("璀璨原钻", names);
        Assert.DoesNotContain("原质", names);
        Assert.DoesNotContain("智识之冕", names);
    }

    /// <summary>敌人、天赋书、武器突破与宝石均可由已知低阶材料折算，不要求目标已上报库存。</summary>
    [Theory]
    [InlineData("史莱姆凝液", "史莱姆清")]
    [InlineData("「自由」的教导", "「自由」的指引")]
    [InlineData("高塔孤王的破瓦", "高塔孤王的残垣")]
    [InlineData("燃愿玛瑙碎屑", "燃愿玛瑙断片")]
    public void KnownLowerStockCanSatisfyUnrecordedHigherTarget(string lower, string higher)
    {
        var workspace = Workspace((lower, 3));
        var progress = MaterialDemandPolicy.EvaluateAll(workspace, Demands((higher, 1)))[higher];
        Assert.Equal(MaterialDemandState.Completed, progress.State);
        Assert.Equal("Family", progress.ConfirmationMethod);
        Assert.Equal(3, workspace.Inventory.Items[lower][0].Quantity);
        Assert.False(workspace.Inventory.Items.ContainsKey(higher));
    }

    /// <summary>低阶已完成的目标仍占有库存，不可重复分配给高阶。</summary>
    [Fact]
    public void CompletedLowerDemandReservesItsStock()
    {
        var workspace = Workspace(("史莱姆凝液", 3));
        var progress = MaterialDemandPolicy.EvaluateAll(workspace, Demands(("史莱姆凝液", 1), ("史莱姆清", 1)));
        Assert.Equal(MaterialDemandState.Completed, progress["史莱姆凝液"].State);
        Assert.NotEqual(MaterialDemandState.Completed, progress["史莱姆清"].State);
    }

    /// <summary>低阶目标不足不会阻断另一段已足够的库存满足高阶目标。</summary>
    [Fact]
    public void MissingLowerTargetDoesNotBlockIndependentHigherStock()
    {
        var workspace = Workspace(("史莱姆凝液", 0), ("史莱姆清", 3));
        var progress = MaterialDemandPolicy.EvaluateAll(workspace, Demands(("史莱姆凝液", 1), ("史莱姆原浆", 1)));
        Assert.NotEqual(MaterialDemandState.Completed, progress["史莱姆凝液"].State);
        Assert.Equal("Family", progress["史莱姆原浆"].ConfirmationMethod);
    }

    /// <summary>未确认基准的正增量不会创造可供合成的数量。</summary>
    [Fact]
    public void UnknownBaselineDoesNotBecomeStockFromDelta()
    {
        var workspace = Workspace(("史莱姆清", 0));
        workspace.Settings.UseDeltaForDemands = true;
        workspace.Inventory.Events.Add(Delta("史莱姆凝液", 30));
        Assert.Equal(MaterialDemandState.Unmet,
            MaterialDemandPolicy.EvaluateAll(workspace, Demands(("史莱姆清", 1)))["史莱姆清"].State);
    }

    /// <summary>消耗增量必须从旧库存扣除，即使仍未允许增量确认也不能用旧量合成达标。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NegativeDeltaReducesAvailableStock(bool useDelta)
    {
        var workspace = Workspace(("史莱姆凝液", 3));
        workspace.Settings.UseDeltaForDemands = useDelta;
        workspace.Inventory.Events.Add(Delta("史莱姆凝液", -1));
        Assert.Equal(MaterialDemandState.Unmet,
            MaterialDemandPolicy.EvaluateAll(workspace, Demands(("史莱姆清", 1)))["史莱姆清"].State);
    }

    /// <summary>待确认增量只有开启相应判断设置后才可直接确认家族达标。</summary>
    [Fact]
    public void FamilyDeltaRequiresExplicitSetting()
    {
        var workspace = Workspace(("史莱姆凝液", 0));
        workspace.Inventory.Events.Add(Delta("史莱姆凝液", 3));
        var demands = Demands(("史莱姆清", 1));
        Assert.Equal(MaterialDemandState.Pending, MaterialDemandPolicy.EvaluateAll(workspace, demands)["史莱姆清"].State);
        workspace.Settings.UseDeltaForDemands = true;
        Assert.Equal("FamilyDelta", MaterialDemandPolicy.EvaluateAll(workspace, demands)["史莱姆清"].ConfirmationMethod);
    }

    /// <summary>目标本身没有红灯时，也不能借用有库存错误的低阶材料达标。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidLowerStockCannotCompleteHigherDemand(bool errorEvent)
    {
        var workspace = Workspace(("史莱姆凝液", 3));
        if (errorEvent)
            workspace.Inventory.Events.Add(new InventoryEvent { Name = "史莱姆凝液", State = "Error" });
        else
            workspace.Inventory.Errors["史莱姆凝液"] = new InventoryError { Message = "数量冲突" };

        Assert.Equal(MaterialDemandState.Unmet,
            MaterialDemandPolicy.EvaluateAll(workspace, Demands(("史莱姆清", 1)))["史莱姆清"].State);
    }

    /// <summary>不可靠低阶材料不贡献库存，但不阻断其他可靠成员独立满足更高阶目标。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidLowerStockDoesNotBlockIndependentMiddleStock(bool negativeEstimate)
    {
        var workspace = Workspace(("史莱姆凝液", 0), ("史莱姆清", 3));
        if (negativeEstimate)
            workspace.Inventory.Events.Add(Delta("史莱姆凝液", -1));
        else
            workspace.Inventory.Errors["史莱姆凝液"] = new InventoryError { Message = "数量冲突" };

        var progress = MaterialDemandPolicy.EvaluateAll(workspace, Demands(("史莱姆原浆", 1)))["史莱姆原浆"];
        Assert.Equal(MaterialDemandState.Completed, progress.State);
        Assert.Equal("Family", progress.ConfirmationMethod);
    }

    /// <summary>更高阶的库存与增量不参与低阶升级，因此不能改变其完成状态、来源和时间。</summary>
    [Fact]
    public void HigherPendingDoesNotChangeLowerCraftingEvidence()
    {
        var workspace = Workspace(("史莱姆凝液", 3), ("史莱姆原浆", 1));
        var lower = workspace.Inventory.Items["史莱姆凝液"][0];
        var higher = workspace.Inventory.Items["史莱姆原浆"][0];
        higher.RecordedAt = lower.RecordedAt.AddHours(1);
        higher.Source = "unrelated";
        workspace.Inventory.Events.Add(Delta("史莱姆原浆", 1));

        var progress = MaterialDemandPolicy.EvaluateAll(workspace, Demands(("史莱姆清", 1)))["史莱姆清"];
        Assert.Equal(MaterialDemandState.Completed, progress.State);
        Assert.Equal("Family", progress.ConfirmationMethod);
        Assert.Equal(lower.RecordedAt, progress.CompletedAt);
        Assert.Equal(lower.Source, progress.Source);
    }

    /// <summary>没有基准的增量不提供材料，也不能把另一个可靠成员的合成确认降为待核实。</summary>
    [Fact]
    public void UnknownLowerPendingDoesNotChangeIndependentCraftingState()
    {
        var workspace = Workspace(("史莱姆清", 3));
        workspace.Inventory.Events.Add(Delta("史莱姆凝液", 3));
        var progress = MaterialDemandPolicy.EvaluateAll(workspace, Demands(("史莱姆原浆", 1)))["史莱姆原浆"];
        Assert.Equal(MaterialDemandState.Completed, progress.State);
        Assert.Equal("Family", progress.ConfirmationMethod);
    }

    /// <summary>低阶余量不足以合成时，其待核实记录不属于另一段升级链的完成依据。</summary>
    [Fact]
    public void LowerPendingWithoutCarryDoesNotChangeIndependentCraftingState()
    {
        var workspace = Workspace(("史莱姆凝液", 1), ("史莱姆清", 3));
        workspace.Inventory.Events.Add(Delta("史莱姆凝液", 1));
        var progress = MaterialDemandPolicy.EvaluateAll(workspace, Demands(("史莱姆原浆", 1)))["史莱姆原浆"];
        Assert.Equal(MaterialDemandState.Completed, progress.State);
        Assert.Equal("Family", progress.ConfirmationMethod);
    }

    /// <summary>随机收益估算与固定合成分开记录，宝石不使用角色合成预留率。</summary>
    [Fact]
    public void BonusEstimateIsMarkedAndNeverAppliedToGems()
    {
        var workspace = Workspace(("史莱姆凝液", 12), ("燃愿玛瑙碎屑", 12));
        workspace.Settings.DemandCraftingReservePercent = 20;
        var demands = Demands(("史莱姆清", 5), ("燃愿玛瑙断片", 5));
        var progress = MaterialDemandPolicy.EvaluateAll(workspace, demands);
        Assert.Equal("FamilyEstimate", progress["史莱姆清"].ConfirmationMethod);
        Assert.Equal(20, progress["史莱姆清"].CraftingReservePercent);
        Assert.NotEqual(MaterialDemandState.Completed, progress["燃愿玛瑙断片"].State);
    }

    /// <summary>计算为单向整数升级，不允许拆解高阶，余数不能凭空产生材料。</summary>
    [Fact]
    public void CalculatorPreservesRemaindersAndNeverDowngrades()
    {
        var available = MaterialFamilyCalculator.CalculateAvailable([(2, 0), (0, 1), (100, 0)], 0);
        Assert.Equal(0, available[1]);
        Assert.Equal(2, available[0]);
        Assert.Throws<ArgumentException>(() => MaterialFamilyCalculator.CalculateAvailable([(-1, 0)], 0));
    }

    /// <summary>建立不访问文件、网络和用户数据的库存快照。</summary>
    private static InventoryWorkspace Workspace(params (string Name, long Quantity)[] stock)
    {
        var workspace = new InventoryWorkspace();
        workspace.Settings.UseFamiliesForDemands = true;
        workspace.Settings.UseDeltaForDemands = false;
        workspace.Settings.DemandCraftingReservePercent = 0;
        foreach (var (name, quantity) in stock)
            workspace.Inventory.Items[name] = [new InventoryRecord { Quantity = quantity, RecordedAt = DateTimeOffset.UtcNow, Source = "test" }];
        return workspace;
    }

    /// <summary>生成同一张需求表，所有目标均应参与分配。</summary>
    private static MaterialDemandDocument Demands(params (string Name, long Quantity)[] targets)
        => new() { Items = targets.ToDictionary(target => target.Name, target => new MaterialDemand { TargetQuantity = target.Quantity }) };

    /// <summary>生成未确认的数量变化，供正负增量规则验证。</summary>
    private static InventoryEvent Delta(string name, long quantity)
        => new() { Name = name, Kind = "Delta", State = "Pending", Quantity = quantity, RecordedAt = DateTimeOffset.UtcNow };
}
