using System.Collections.Generic;
using BetterGenshinImpact.GameTask.MapMask;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.MapMaskTests;

/// <summary>
/// 账号标记缓存：乐观更新、失败回滚与过期加载结果丢弃
/// </summary>
public sealed class HoYoLabMarkTrackerTests
{
    /// <summary>已加载列表的跟踪器</summary>
    private static HoYoLabMarkTracker Loaded(params long[] ids)
    {
        var tracker = new HoYoLabMarkTracker();
        tracker.CompleteLoad(tracker.BeginLoad(), new HashSet<long>(ids));
        return tracker;
    }

    /// <summary>同一点位请求未完成时不能再次切换，避免两次失败回滚到错误状态</summary>
    [Fact]
    public void TryBeginToggle_RejectsWhileInFlight()
    {
        var tracker = Loaded();

        Assert.True(tracker.TryBeginToggle(7, hidden: true));
        Assert.False(tracker.TryBeginToggle(7, hidden: false));
        Assert.Contains(7, tracker.MarkedIds!);
    }

    /// <summary>失败时恢复切换前的状态，并允许再次切换</summary>
    [Fact]
    public void EndToggle_Failure_RestoresPreviousState()
    {
        var tracker = Loaded(7);

        tracker.TryBeginToggle(7, hidden: false);
        tracker.EndToggle(7, hidden: false, success: false);

        Assert.Contains(7, tracker.MarkedIds!);
        Assert.True(tracker.TryBeginToggle(7, hidden: false));
    }

    /// <summary>切换前开始的加载结果是旧快照，必须丢弃，不能覆盖乐观更新</summary>
    [Fact]
    public void CompleteLoad_StartedBeforeToggle_IsDiscarded()
    {
        var tracker = Loaded();
        var staleLoad = tracker.BeginLoad();

        tracker.TryBeginToggle(7, hidden: true);
        var accepted = tracker.CompleteLoad(staleLoad, new HashSet<long>());

        Assert.False(accepted);
        Assert.Contains(7, tracker.MarkedIds!);
    }

    /// <summary>未加载时缓存为空，需要加载；重置后同样需要重新加载</summary>
    [Fact]
    public void NeedsLoad_UntilLoadedAndAfterReset()
    {
        var tracker = new HoYoLabMarkTracker();
        Assert.True(tracker.NeedsLoad);

        tracker.CompleteLoad(tracker.BeginLoad(), new HashSet<long> { 1 });
        Assert.False(tracker.NeedsLoad);

        tracker.Reset();
        Assert.True(tracker.NeedsLoad);
        Assert.Null(tracker.MarkedIds);
    }

    /// <summary>加载进行中不重复发起加载</summary>
    [Fact]
    public void NeedsLoad_FalseWhileLoading()
    {
        var tracker = new HoYoLabMarkTracker();
        tracker.BeginLoad();

        Assert.False(tracker.NeedsLoad);
    }
}
