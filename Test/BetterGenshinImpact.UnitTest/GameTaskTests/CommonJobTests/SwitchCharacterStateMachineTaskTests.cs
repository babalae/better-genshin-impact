using BetterGenshinImpact.GameTask.Common.Job;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.CommonJobTests;

public class SwitchCharacterStateMachineTaskTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(" 胡桃 ", "胡桃")]
    public void NormalizeSlotName_ShouldTreatNullAndWhitespaceAsSkippedSlot(string? value, string expected)
    {
        Assert.Equal(expected, SwitchCharacterStateMachineTask.NormalizeSlotName(value));
    }

    [Fact]
    public void WrapSwitchCharacterException_ShouldPreserveBusinessFalseBoundary()
    {
        var inner = new InvalidOperationException("角色名称校验失败：不存在的角色");

        var exception = SwitchCharacterStateMachineTask.WrapSwitchCharacterException(inner);

        Assert.Contains("角色名称校验失败", exception.Message);
        Assert.Same(inner, exception.InnerException);
    }

    [Fact]
    public void BuildSelectionPlan_ShouldRebuildFromFirstAffectedSlot_WhenMovingCurrentRoleToSecondSlot()
    {
        var result = SwitchCharacterStateMachineTask.BuildSelectionPlanForTesting(
            [null, "胡桃", null, null],
            ["胡桃", "行秋", "钟离", "夜兰"]);

        Assert.True(result.Success, result.FailureReason);
        Assert.Equal([1, 2, 3, 4], result.SlotsToClear);
        AssertPlan(result,
            (1, "行秋", true),
            (2, "胡桃", false),
            (3, "钟离", true),
            (4, "夜兰", true));
    }

    [Fact]
    public void BuildSelectionPlan_ShouldRebuildSuffix_WhenMovingCurrentRoleToFourthSlot()
    {
        var result = SwitchCharacterStateMachineTask.BuildSelectionPlanForTesting(
            [null, null, null, "行秋"],
            ["胡桃", "行秋", "钟离", "夜兰"]);

        Assert.True(result.Success, result.FailureReason);
        Assert.Equal([2, 3, 4], result.SlotsToClear);
        AssertPlan(result,
            (2, "钟离", true),
            (3, "夜兰", true),
            (4, "行秋", false));
    }

    [Fact]
    public void BuildSelectionPlan_ShouldPreservePrefixAndAppendInFinalOrder_WhenInsertingNewRole()
    {
        var result = SwitchCharacterStateMachineTask.BuildSelectionPlanForTesting(
            [null, "纳西妲", null, null],
            ["胡桃", "行秋", "钟离", "夜兰"]);

        Assert.True(result.Success, result.FailureReason);
        Assert.Equal([2, 3, 4], result.SlotsToClear);
        AssertPlan(result,
            (2, "纳西妲", false),
            (3, "行秋", true),
            (4, "钟离", true));
    }

    [Fact]
    public void BuildSelectionPlan_ShouldFail_WhenTargetSlotHasUnfillablePrecedingEmptySlot()
    {
        var result = SwitchCharacterStateMachineTask.BuildSelectionPlanForTesting(
            [null, null, null, "钟离"],
            ["胡桃", "行秋", null, null]);

        Assert.False(result.Success);
        Assert.Equal([4], result.SlotsToClear);
        Assert.Empty(result.SelectionPlan);
        Assert.Contains("空槽 3", result.FailureReason);
    }

    private static void AssertPlan(
        SwitchCharacterStateMachineTask.SwitchPlanDebugResult result,
        params (int Slot, string Name, bool IsRefill)[] expected)
    {
        Assert.Equal(expected.Length, result.SelectionPlan.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Slot, result.SelectionPlan[i].Slot);
            Assert.Equal(expected[i].Name, result.SelectionPlan[i].Name);
            Assert.Equal(expected[i].IsRefill, result.SelectionPlan[i].IsRefill);
        }
    }
}
