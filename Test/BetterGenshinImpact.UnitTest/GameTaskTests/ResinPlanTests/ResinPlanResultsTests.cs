using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.GameTask.ResinPlan;
using BetterGenshinImpact.GameTask.SoloTask;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.ResinPlanTests;

/// <summary>体力计划结果汇总：奖励合并、树脂合计与整次运行的结果种类，不启动游戏。</summary>
public sealed class ResinPlanResultsTests
{
    /// <summary>各阶段的奖励按名称合并，领奖次数与消耗逐类相加。</summary>
    [Fact]
    public void Complete_MergesRewardsAndAggregatesResin()
    {
        var data = new ResinPlanData
        {
            Steps =
            [
                Executed(SoloTaskResultKind.Succeeded, 2, original: 40, condensed: 0, ("教导", 3), ("摩拉", 1000)),
                Executed(SoloTaskResultKind.Succeeded, 1, original: 0, condensed: 1, ("摩拉", 500))
            ]
        };

        var result = ResinPlanResults.Complete(data);

        Assert.Equal(SoloTaskResultKind.Succeeded, result.Kind);
        Assert.Same(data, result.Data);
        Assert.Equal(new Dictionary<string, int> { ["教导"] = 3, ["摩拉"] = 1500 }, data.Rewards.ToDictionary());
        Assert.Equal(3, data.Resin.ClaimCount);
        Assert.Equal(40, data.Resin.Consumed.Original);
        Assert.Equal(1, data.Resin.Consumed.Condensed);
        Assert.StartsWith("体力计划结束：领奖 3 次", result.Message);
    }

    /// <summary>有失败步骤时，已有领奖为部分完成，否则为失败，原因码取第一个失败步骤。</summary>
    [Fact]
    public void Complete_FailedStepsDecidePartialOrFailed()
    {
        var failed = new ResinPlanStep { Name = "秘境养成", Executed = true, Kind = SoloTaskResultKind.Failed,
            ReasonCode = ResinPlanReasons.TaskException, Message = "提升指南识别失败" };
        var partial = ResinPlanResults.Complete(new ResinPlanData
            { Steps = [Executed(SoloTaskResultKind.Succeeded, 1, 40, 0), failed] });
        Assert.Equal(SoloTaskResultKind.Partial, partial.Kind);
        Assert.Equal(ResinPlanReasons.TaskException, partial.ReasonCode);
        Assert.Contains("秘境养成失败：提升指南识别失败", partial.Message);

        var onlyFailed = ResinPlanResults.Complete(new ResinPlanData { Steps = [failed] });
        Assert.Equal(SoloTaskResultKind.Failed, onlyFailed.Kind);
        Assert.NotNull(onlyFailed.Data);
    }

    /// <summary>没有领奖时为跳过：因树脂不足或达到上限跳过记为树脂不足，否则记为没有可领取的奖励。</summary>
    [Fact]
    public void Complete_NoClaimsIsSkippedWithReason()
    {
        var exhausted = ResinPlanResults.Complete(new ResinPlanData
        {
            Steps = [new ResinPlanStep { Name = "秘境养成", ReasonCode = SoloTaskReasons.ResinExhausted }]
        });
        Assert.Equal(SoloTaskResultKind.Skipped, exhausted.Kind);
        Assert.Equal(SoloTaskReasons.ResinExhausted, exhausted.ReasonCode);

        var nothing = ResinPlanResults.Complete(new ResinPlanData
        {
            Steps = [Executed(SoloTaskResultKind.Succeeded, 0, 0, 0)]
        });
        Assert.Equal(SoloTaskResultKind.Skipped, nothing.Kind);
        Assert.Equal(ResinPlanReasons.NothingClaimed, nothing.ReasonCode);
    }

    /// <summary>没有失败但存在未核验步骤时整次为未核验；限定预算用完时报告说明树脂已用完。</summary>
    [Fact]
    public void Complete_UnverifiedStepAndBudgetExhaustion()
    {
        var data = new ResinPlanData
        {
            Budget = new ResinPlanBudgetReport { MaxResin = 60, SpentPoints = 60 },
            Steps = [Executed(SoloTaskResultKind.Unverified, 3, 60, 0)]
        };

        var result = ResinPlanResults.Complete(data);

        Assert.Equal(SoloTaskResultKind.Unverified, result.Kind);
        Assert.Equal(ResinStopReason.ResinExhausted, data.Resin.StopReason);
        Assert.EndsWith("树脂已用完。", result.Message);
    }

    /// <summary>参数校验：负数用量、开启合成但未选地区都在操作游戏前拒绝。</summary>
    [Fact]
    public void Validate_RejectsInvalidParameters()
    {
        Assert.Null(ResinPlanTask.Validate(new ResinPlanParam()));
        Assert.NotNull(ResinPlanTask.Validate(new ResinPlanParam { MaxResin = -1 }));
        var craft = new ResinPlanParam { CraftCondensedFirst = true };
        craft.Craft.Country = "";
        Assert.NotNull(ResinPlanTask.Validate(craft));
    }

    /// <summary>建立一个已执行的阶段记录。</summary>
    private static ResinPlanStep Executed(SoloTaskResultKind kind, int claims, int original, int condensed,
        params (string Name, int Count)[] rewards) => new()
    {
        Name = "阶段",
        Executed = true,
        Kind = kind,
        Message = "结束",
        Resin = new ResinUsageReport
        {
            ClaimCount = claims,
            Claims = new() { Original = original / 20, Condensed = condensed },
            Consumed = new() { Original = original, Condensed = condensed },
            StopReason = ResinStopReason.TargetReached
        },
        Rewards = RewardSummary.From(rewards.ToDictionary(item => item.Name, item => item.Count)),
        RewardsRecognized = true
    };
}
