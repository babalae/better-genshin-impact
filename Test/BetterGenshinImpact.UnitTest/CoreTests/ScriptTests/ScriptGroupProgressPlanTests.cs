using System.Collections.Generic;
using BetterGenshinImpact.Core.Script.Group;

namespace BetterGenshinImpact.UnitTest.CoreTests.ScriptTests;

/// <summary>
/// 配置组进度计划的纯逻辑：预计剩余时间 = 未开始脚本的预估之和 + 当前脚本的剩余预估，
/// 因此每跑完（或被跳过）一个脚本，剩余时间都会按剩下的脚本重算。
/// 多配置组时当前组剩余与整批总剩余分开给出。
/// </summary>
public class ScriptGroupProgressPlanTests
{
    private static List<ScriptGroupProgressStep> Steps(string group, params double[] estimates)
    {
        var steps = new List<ScriptGroupProgressStep>();
        for (var i = 0; i < estimates.Length; i++)
        {
            steps.Add(new ScriptGroupProgressStep(group, $"{group}-{i}", estimates[i]));
        }

        return steps;
    }

    [Fact]
    public void SingleGroup_ShouldTrackProgressAndRemaining()
    {
        var plan = new ScriptGroupProgressPlan();
        plan.StartGroup("A", Steps("A", 30, 30, 30));
        plan.StartStep(0);

        var atStart = plan.Calculate(0);
        Assert.Equal(0, atStart.Progress, 3);
        Assert.Equal(90, atStart.GroupRemainingSeconds, 3);
        Assert.Equal(90, atStart.TotalRemainingSeconds, 3);
        Assert.False(plan.HasMultipleGroups);

        // 第一个脚本跑了 10 秒：剩余 = 当前脚本还剩 20 + 后面两个脚本 60
        var running = plan.Calculate(10);
        Assert.Equal(10d / 90 * 100, running.Progress, 3);
        Assert.Equal(80, running.GroupRemainingSeconds, 3);

        // 第二个脚本开始：第一个脚本的实际耗时不再参与剩余时间，直接换成剩下脚本的预估
        plan.StartStep(1);
        var second = plan.Calculate(0);
        Assert.Equal(30d / 90 * 100, second.Progress, 3);
        Assert.Equal(60, second.GroupRemainingSeconds, 3);

        // 组结束
        plan.EndGroup();
        var finished = plan.Calculate(0);
        Assert.Equal(100, finished.Progress, 3);
        Assert.Equal(0, finished.GroupRemainingSeconds, 3);
        Assert.False(plan.HasPendingSteps);
    }

    [Fact]
    public void SkippedSteps_ShouldLeaveRemainingImmediately()
    {
        var plan = new ScriptGroupProgressPlan();
        plan.StartGroup("A", Steps("A", 30, 30, 30));

        // 前两个脚本被跳过（没有单独上报结束），第三个直接开始
        plan.StartStep(2);

        var result = plan.Calculate(0);
        Assert.Equal(60d / 90 * 100, result.Progress, 3);
        Assert.Equal(30, result.GroupRemainingSeconds, 3);
    }

    [Fact]
    public void MultipleGroups_ShouldReportGroupAndTotalSeparately()
    {
        var plan = new ScriptGroupProgressPlan();
        plan.Append(Steps("A", 30));
        plan.Append(Steps("B", 60));
        Assert.True(plan.HasMultipleGroups);

        plan.StartGroup("A", Steps("A", 30));
        plan.StartStep(0);

        var inGroupA = plan.Calculate(0);
        Assert.Equal(30, inGroupA.GroupRemainingSeconds, 3);
        Assert.Equal(90, inGroupA.TotalRemainingSeconds, 3);
        Assert.True(plan.HasLaterGroups);

        // 当前组结束、还没有进入下一个组的瞬间，总剩余必须仍然是下一个组的完整预估
        plan.EndGroup();
        var betweenGroups = plan.Calculate(0);
        Assert.Equal(0, betweenGroups.GroupRemainingSeconds, 3);
        Assert.Equal(60, betweenGroups.TotalRemainingSeconds, 3);
        Assert.True(plan.HasLaterGroups);

        // 进入第二个配置组：当前组剩余与总剩余（没有后续组）相同，进度重新按本组计算
        plan.StartGroup("B", Steps("B", 60));
        plan.StartStep(0);

        var inGroupB = plan.Calculate(0);
        Assert.Equal(0, inGroupB.Progress, 3);
        Assert.Equal(60, inGroupB.GroupRemainingSeconds, 3);
        Assert.Equal(60, inGroupB.TotalRemainingSeconds, 3);
        // 已经是最后一个配置组，不再显示「预计总剩余时间」
        Assert.False(plan.HasLaterGroups);
    }

    /// <summary>
    /// 预置的整批预估要用实际要执行的脚本替换（继续执行 / 跳过规则会让两者不一致）
    /// </summary>
    [Fact]
    public void StartGroup_ShouldReplacePrefilledEstimate()
    {
        var plan = new ScriptGroupProgressPlan();
        plan.Append(Steps("A", 30, 30));
        plan.Append(Steps("B", 60));

        plan.StartGroup("A", Steps("A", 90));
        plan.StartStep(0);

        var result = plan.Calculate(0);
        Assert.Equal(90, result.GroupRemainingSeconds, 3);
        Assert.Equal(150, result.TotalRemainingSeconds, 3);
    }

    [Fact]
    public void GroupWithoutEstimate_ShouldNotProduceProgress()
    {
        var plan = new ScriptGroupProgressPlan();
        plan.StartGroup("A", Steps("A", 0, 0));
        plan.StartStep(0);

        var result = plan.Calculate(5);
        Assert.Equal(0, result.Progress, 3);
        Assert.Equal(0, result.GroupRemainingSeconds, 3);
        Assert.Equal(0, result.TotalRemainingSeconds, 3);
    }

    [Fact]
    public void Reset_ShouldClearPlan()
    {
        var plan = new ScriptGroupProgressPlan();
        plan.Append(Steps("A", 30));
        plan.Reset();

        Assert.False(plan.HasSteps);
        Assert.False(plan.HasPendingSteps);
        Assert.False(plan.HasMultipleGroups);
        Assert.Equal(0, plan.TotalEstimatedSeconds, 3);
    }

    /// <summary>
    /// 超时保护：当前脚本跑超了预估就按 10 秒粒度补时，
    /// 否则它的剩余会被夹在 0，倒计时看起来就是卡住了。
    /// </summary>
    [Fact]
    public void OverrunStep_ShouldExtendByTenSecondIncrements()
    {
        var plan = new ScriptGroupProgressPlan();
        plan.StartGroup("A", Steps("A", 20, 40));
        plan.StartStep(0);

        // 跑了 25 秒（超出 20 秒预估）：补到 30 秒，本组剩余 = 5（当前脚本）+ 40（后面的脚本）
        var overrun = plan.Calculate(25);
        Assert.Equal(45, overrun.GroupRemainingSeconds, 3);
        Assert.Equal(45, overrun.TotalRemainingSeconds, 3);
        Assert.Equal(70, plan.TotalEstimatedSeconds, 3);

        // 还超就继续补：95 秒 -> 补到 100 秒，剩余始终为正而不是卡在 0
        var more = plan.Calculate(95);
        Assert.Equal(45, more.GroupRemainingSeconds, 3);
        // 100 + 40：确认是按 10 秒粒度累加出来的
        Assert.Equal(140, plan.TotalEstimatedSeconds, 3);
    }

    /// <summary>倒计时不会停在 0：只要脚本还没结束，剩余时间就一直是正数</summary>
    [Theory]
    [InlineData(21)]
    [InlineData(33)]
    [InlineData(47)]
    [InlineData(300)]
    public void OverrunStep_ShouldAlwaysKeepPositiveRemaining(double elapsed)
    {
        var plan = new ScriptGroupProgressPlan();
        plan.StartGroup("A", Steps("A", 20));
        plan.StartStep(0);

        Assert.True(plan.Calculate(elapsed).GroupRemainingSeconds > 0);
    }

    /// <summary>脚本结束后剩余时间按后面的脚本重算，补过的时间不带到下一个脚本</summary>
    [Fact]
    public void OverrunExtension_ShouldNotLeakToNextStep()
    {
        var plan = new ScriptGroupProgressPlan();
        plan.StartGroup("A", Steps("A", 20, 40));
        plan.StartStep(0);
        plan.Calculate(55); // 第一个脚本被补到 60 秒

        plan.StartStep(1);
        var next = plan.Calculate(0);

        // 只剩第二个脚本自己的 40 秒
        Assert.Equal(40, next.GroupRemainingSeconds, 3);
        Assert.Equal(40, next.TotalRemainingSeconds, 3);
    }

    [Fact]
    public void ZeroEstimateStep_ShouldNotBeExtended()
    {
        var plan = new ScriptGroupProgressPlan();
        plan.StartGroup("A", Steps("A", 0, 30));
        plan.StartStep(0);

        // 预估为 0 的脚本（非地图追踪）不参与补时，避免无限增长
        var result = plan.Calculate(600);
        Assert.Equal(30, result.GroupRemainingSeconds, 3);
        Assert.Equal(30, plan.TotalEstimatedSeconds, 3);
    }
}
