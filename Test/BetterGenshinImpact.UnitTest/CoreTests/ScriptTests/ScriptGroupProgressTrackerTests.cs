using System;
using System.Collections.Generic;
using System.Threading;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Script.Group;

namespace BetterGenshinImpact.UnitTest.CoreTests.ScriptTests;

/// <summary>
/// <see cref="ScriptGroupProgressTracker"/> 的执行时序。测试宿主里 Application.Current 为空，
/// RunOnUiThread 会就地执行，因此每步之后都能直接断言展示字段。
/// 这里主要守护两个曾经出错的点：批量执行时秒表必须真的在走、以及跨配置组的总剩余不能漏算。
/// </summary>
public class ScriptGroupProgressTrackerTests
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

    private static List<ScriptGroupProgressStep> Both(
        List<ScriptGroupProgressStep> first,
        List<ScriptGroupProgressStep> second)
    {
        var list = new List<ScriptGroupProgressStep>(first);
        list.AddRange(second);
        return list;
    }

    [Fact]
    public void Batch_ShouldCountDown()
    {
        // 用独立实例：共享单例会被其它测试触发的清空打乱（进度跟踪依赖真实时钟）
        var tracker = ScriptGroupProgressTracker.CreateIsolated();
        var stepsA = Steps("A", 60);
        var stepsB = Steps("B", 60);

        using (tracker.BeginBatchSteps(Both(stepsA, stepsB)))
        {
            using (tracker.BeginTracking("A", stepsA))
            {
                tracker.OnStepStarted(0);

                Assert.True(tracker.IsRunning);
                Assert.Equal("1:00", tracker.RemainingText);
                Assert.True(tracker.HasTotalRemaining);
                Assert.Equal("2:00", tracker.TotalRemainingText);

                // 秒表必须真的在走：批量路径曾经跳过 Restart，导致倒计时永远停在初始值。
                // 展示字段只在 Refresh 时更新（定时器在测试宿主不会跳），所以这里查计算值。
                // 并行跑测试时 Sleep 可能被拖长，因此只断言「确实少了一秒以上、且还没走完」。
                Thread.Sleep(1200);
                Assert.InRange(tracker.GetGroupRemainingEstimate(0), 0.5, 59.0);
            }

            // 一组结束、下一组还没开始时，面板保持显示，总剩余是下一组的完整预估
            Assert.True(tracker.IsRunning);
            Assert.Equal("0:00", tracker.RemainingText);
            Assert.Equal("1:00", tracker.TotalRemainingText);

            using (tracker.BeginTracking("B", stepsB))
            {
                tracker.OnStepStarted(0);

                Assert.Equal("B", tracker.GroupName);
                Assert.Equal("1:00", tracker.RemainingText);
                // 已经是最后一个配置组，不再显示「预计总剩余时间」
                Assert.False(tracker.HasTotalRemaining);
                Assert.Equal(0, tracker.Progress, 3);
            }

            // 整批结束
            Assert.False(tracker.IsRunning);
            Assert.False(tracker.HasTotalRemaining);
            Assert.Equal("0:00", tracker.TotalRemainingText);
        }

        // 批次结束后再跑一个独立配置组：重新起表，不与上一批混在一起
        using (var single = tracker.BeginTracking("C", Steps("C", 30)))
        {
            Assert.True(tracker.IsRunning);
            Assert.Equal("0:30", tracker.RemainingText);
            Assert.False(tracker.HasTotalRemaining);
        }

        Assert.False(tracker.IsRunning);
    }

    [Fact]
    public void SingleGroup_ShouldHideWhenNoEstimate()
    {
        var tracker = ScriptGroupProgressTracker.CreateIsolated();

        using var scope = tracker.BeginTracking("A", Steps("A", 0));
        Assert.False(tracker.IsRunning);
        Assert.Equal(string.Empty, tracker.GroupName);
    }

    /// <summary>
    /// 超过 24 小时不能回绕：28 小时 35 分的总剩余曾经被显示成 4:35:24，
    /// 比其中单个 15 小时的任务还小。
    /// </summary>
    [Fact]
    public void Duration_ShouldNotWrapAt24Hours()
    {
        var tracker = ScriptGroupProgressTracker.CreateIsolated();

        // 28 小时 = 100800 秒
        using (tracker.BeginTracking("A", Steps("A", 100800)))
        {
            Assert.Equal("28:00:00", tracker.RemainingText);
        }

        using (tracker.BeginTracking("A", Steps("A", 54000)))
        {
            Assert.Equal("15:00:00", tracker.RemainingText);
        }

        // 不足 1 小时用 分:秒
        using (tracker.BeginTracking("A", Steps("A", 90)))
        {
            Assert.Equal("1:30", tracker.RemainingText);
        }

        using (tracker.BeginTracking("A", Steps("A", 59)))
        {
            Assert.Equal("0:59", tracker.RemainingText);
        }
    }

    /// <summary>
    /// 刷新节拍：显示值下一次变化的时刻。倒计时是向上取整后的秒，
    /// 因此「剩余时间的小数部分走完」就是它变化的时刻。
    /// 正好是整数秒时下一次变化在 1 秒后；已经超时（剩余 0）则尽快刷新，让超时保护补时。
    /// </summary>
    [Theory]
    [InlineData(59.4, 0.42)]   // 显示 60，跨过 59 时变 59
    [InlineData(59.0, 1.0)]    // 正好整数：1 秒后跨过 58
    [InlineData(10.0, 1.0)]
    [InlineData(0.5, 0.52)]
    [InlineData(0.0, 0.05)]    // 超时：尽快刷新
    [InlineData(-3.0, 0.05)]
    public void DelayUntilDisplayChanges_ShouldAimAtSecondBoundary(double remaining, double expected)
    {
        var delay = ScriptGroupProgressTracker.GetDelayUntilDisplayChanges(remaining);

        Assert.Equal(expected, delay.TotalSeconds, 2);
        Assert.InRange(delay.TotalSeconds, 0.05, 1.0);
    }

    /// <summary>
    /// 按对齐后的间隔推进刷新，倒计时每次必须只掉 1 秒：
    /// 不会出现「等 2 秒没动」或「1 秒掉 2 秒」。
    /// </summary>
    [Fact]
    public void AlignedRefresh_ShouldDropExactlyOneSecondPerTick()
    {
        var plan = new ScriptGroupProgressPlan();
        plan.StartGroup("A", Steps("A", 60));
        plan.StartStep(0);

        var elapsed = 0.0;
        var displayed = (int)Math.Ceiling(plan.Calculate(elapsed).GroupRemainingSeconds);
        Assert.Equal(60, displayed);

        for (var i = 0; i < 30; i++)
        {
            var remaining = plan.Calculate(elapsed).GroupRemainingSeconds;
            elapsed += ScriptGroupProgressTracker.GetDelayUntilDisplayChanges(remaining).TotalSeconds;

            var next = (int)Math.Ceiling(plan.Calculate(elapsed).GroupRemainingSeconds);
            Assert.Equal(displayed - 1, next);
            displayed = next;
        }
    }

    /// <summary>
    /// 用户点停止 / 任务被取消时要立即隐藏面板，不能沿用「等下一个配置组接上」的显示：
    /// 剩下的配置组被跳过时面板会一路往前推，界面看起来就是倒计时被快进到 0。
    /// </summary>
    [Fact]
    public void Stop_ShouldHideImmediatelyWhenRunAborted()
    {
        var tracker = ScriptGroupProgressTracker.CreateIsolated();
        var stepsA = Steps("A", 60);
        var stepsB = Steps("B", 60);

        try
        {
            CancellationContext.Instance.Set();
            using (tracker.BeginBatchSteps(Both(stepsA, stepsB)))
            {
                var groupA = tracker.BeginTracking("A", stepsA);
                tracker.OnStepStarted(0);
                Assert.True(tracker.IsRunning);
                Assert.True(tracker.HasTotalRemaining);

                // 用户停止
                CancellationContext.Instance.Cancel();
                groupA.Dispose();

                Assert.False(tracker.IsRunning);
                Assert.False(tracker.HasTotalRemaining);
                Assert.Equal("0:00", tracker.RemainingText);
            }
        }
        finally
        {
            // 还原全局取消状态，避免影响其它测试
            CancellationContext.Instance.Set();
        }
    }

    /// <summary>
    /// 回七天神像的补时 = 基准 30 秒 + 「回血等待间隔」配置。
    /// 这里用显式值验证计算规则（测试宿主不能读配置，会连带主程序启动副作用）。
    /// </summary>
    [Theory]
    [InlineData(0, 30)]
    [InlineData(5, 35)]
    [InlineData(30, 60)]
    [InlineData(-3, 30)]   // 配置异常时按 0 处理
    public void StatueHealExtra_ShouldAddTheConfiguredWait(double waitSeconds, double expected)
    {
        Assert.Equal(expected, ScriptGroupProgressTracker.GetStatueHealExtraSeconds(waitSeconds), 3);
    }

    [Fact]
    public void HealBonuses_ShouldExtendCurrentStepByGivenSeconds()
    {
        var tracker = ScriptGroupProgressTracker.CreateIsolated();
        var steps = Steps("A", 60);

        using (tracker.BeginTracking("A", steps))
        {
            tracker.OnStepStarted(0);
            Assert.Equal("1:00", tracker.RemainingText);

            // 队伍回血：补基准开销
            tracker.NotifyHealTriggered();
            Assert.Equal("1:30", tracker.RemainingText);

            // 回七天神像：补「基准 + 等待间隔」，测试里等待间隔取不到，用显式值验证
            tracker.NotifyHealTriggered(ScriptGroupProgressTracker.HealExtraSeconds + 5);
            Assert.Equal("2:05", tracker.RemainingText);

            // 重走路线：重置当前脚本计时，再补 35 秒 -> 剩 60+30+35+35
            tracker.NotifyRouteReset(ScriptGroupProgressTracker.HealExtraSeconds + 5);
            Assert.Equal("2:40", tracker.RemainingText);
        }
    }

    /// <summary>
    /// 跨配置组的总剩余同样不能回绕：28 小时的组 + 1 分钟的组要显示成 28:01:00
    /// </summary>
    [Fact]
    public void TotalRemaining_ShouldAlsoNotWrapAt24Hours()
    {
        var tracker = ScriptGroupProgressTracker.CreateIsolated();
        var stepsA = Steps("A", 100800);
        var stepsB = Steps("B", 60);

        using (tracker.BeginBatchSteps(Both(stepsA, stepsB)))
        {
            using (tracker.BeginTracking("A", stepsA))
            {
                tracker.OnStepStarted(0);

                Assert.Equal("28:00:00", tracker.RemainingText);
                Assert.Equal("28:01:00", tracker.TotalRemainingText);
            }

            // 组 A 结束、尚未进入组 B：总剩余只剩组 B 的 1 分钟
            Assert.Equal("1:00", tracker.TotalRemainingText);
        }
    }
}
