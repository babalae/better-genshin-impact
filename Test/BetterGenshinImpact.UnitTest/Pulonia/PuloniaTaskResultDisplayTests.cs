using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// 运行状态归类的展示约定，不依赖 WPF。
/// </summary>
public sealed class PuloniaTaskResultDisplayTests
{
    /// <summary>测试使用的固定时间。</summary>
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>一条龙行状态按完整地址前缀汇总，优先级为执行中、失败、待核验、完成。</summary>
    [Fact]
    public void SubtreeStatus_UsesAddressPrefixAndPriority()
    {
        const string row = "plan/root/row";
        Assert.Equal(PuloniaTaskSubtreeStatus.NotRun, PuloniaTaskRunClassifier.ClassifySubtree(null, "root", "row"));
        Assert.Equal(PuloniaTaskSubtreeStatus.NotRun, Classify(PuloniaTaskRunStatus.Succeeded, null,
            Result("plan/root/row2", PuloniaTaskNodeStatus.Failed)));
        Assert.Equal(PuloniaTaskSubtreeStatus.Running, Classify(PuloniaTaskRunStatus.Cancelling, row + "/child",
            Result(row + "/other", PuloniaTaskNodeStatus.Failed)));
        Assert.Equal(PuloniaTaskSubtreeStatus.Failed, Classify(PuloniaTaskRunStatus.Failed, null,
            Result(row, PuloniaTaskNodeStatus.Succeeded, PuloniaTaskOutcomeKind.ExecutedUnverified),
            Result(row + "/child", PuloniaTaskNodeStatus.TimedOut)));
        Assert.Equal(PuloniaTaskSubtreeStatus.NeedsVerification, Classify(PuloniaTaskRunStatus.Succeeded, null,
            Result(row, PuloniaTaskNodeStatus.Succeeded, PuloniaTaskOutcomeKind.ExecutedUnverified)));
        Assert.Equal(PuloniaTaskSubtreeStatus.Completed, Classify(PuloniaTaskRunStatus.Succeeded, null,
            Result(row, PuloniaTaskNodeStatus.Succeeded)));
        Assert.Equal(PuloniaTaskSubtreeStatus.SkippedOrStopped, Classify(PuloniaTaskRunStatus.Cancelled, null,
            Result(row, PuloniaTaskNodeStatus.Skipped)));

        PuloniaTaskSubtreeStatus Classify(PuloniaTaskRunStatus status, string? current, params PuloniaTaskNodeResult[] results)
            => PuloniaTaskRunClassifier.ClassifySubtree(row, status, current, results);
    }

    /// <summary>核验提示看任一尝试，重放确认只看每个节点最后一次尝试与运行是否待处理。</summary>
    [Fact]
    public void UncertainOutcomes_DistinguishAnyAttemptFromLastAttempt()
    {
        var retried = new[]
        {
            Result("plan/root/a", PuloniaTaskNodeStatus.Failed, PuloniaTaskOutcomeKind.PartiallySucceeded),
            Result("plan/root/a", PuloniaTaskNodeStatus.Succeeded)
        };
        Assert.True(PuloniaTaskRunClassifier.HasUncertainAttempt(retried));
        Assert.False(PuloniaTaskRunClassifier.RequiresReplayConfirmation(PuloniaTaskRunStatus.Failed, retried));
        Assert.True(PuloniaTaskRunClassifier.RequiresReplayConfirmation(PuloniaTaskRunStatus.NeedsAttention, retried));
        // 最后一次尝试仍是部分成功时需要确认。
        Assert.True(PuloniaTaskRunClassifier.RequiresReplayConfirmation(PuloniaTaskRunStatus.Failed, [retried[1], retried[0]]));

        Assert.Equal(PuloniaTaskRunStatusCategory.Running, PuloniaTaskRunClassifier.Categorize(PuloniaTaskRunStatus.Cancelling));
        Assert.Equal(PuloniaTaskRunStatusCategory.Failed, PuloniaTaskRunClassifier.Categorize(PuloniaTaskRunStatus.TimedOut));
        Assert.Equal(PuloniaTaskRunStatusCategory.Attention, PuloniaTaskRunClassifier.Categorize(PuloniaTaskRunStatus.Interrupted));
        Assert.Equal(PuloniaTaskRunStatusCategory.Neutral, PuloniaTaskRunClassifier.Categorize(PuloniaTaskRunStatus.Queued));
        Assert.True(PuloniaTaskRunClassifier.IsActive(PuloniaTaskRunStatus.Queued));
        Assert.False(PuloniaTaskRunClassifier.IsActive(PuloniaTaskRunStatus.Interrupted));
    }

    /// <summary>建立一次节点尝试结果。</summary>
    private static PuloniaTaskNodeResult Result(string address, PuloniaTaskNodeStatus status,
        PuloniaTaskOutcomeKind? outcome = null)
        => new(address, "节点", "solo.test", 1, status, "结果", Now, Now, outcomeKind: outcome);
}
