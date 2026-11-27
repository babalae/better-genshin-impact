using BetterGenshinImpact.Service.AccountRecords;

namespace BetterGenshinImpact.UnitTest.ServiceTests;

/// <summary>验证任务锁重叠、取消及人工提交与任务启动的互斥关系。</summary>
public sealed class AccountRecordsInteractionGateTests
{
    /// <summary>统一执行入口拒绝被锁定的写入，并在提交失败后释放资源。</summary>
    [Fact]
    public async Task ManualExecutor_BlocksBeforeActionAndReleasesAfterFailure()
    {
        var gate = new AccountRecordsInteractionGate();
        var called = false;
        using (await gate.LockForTaskAsync())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => gate.ExecuteManualAsync(() =>
            {
                called = true;
                return Task.CompletedTask;
            }));
        }
        Assert.False(called);
        await Assert.ThrowsAsync<IOException>(() => gate.ExecuteManualAsync(() => Task.FromException(new IOException("test"))));
        using var task = await gate.LockForTaskAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(gate.IsLocked);
    }
    /// <summary>最后一个任务结束后才允许人工写入，重复释放不会提前解锁。</summary>
    [Fact]
    public async Task OverlappingTasks_BlockManualWritesUntilAllComplete()
    {
        var gate = new AccountRecordsInteractionGate();
        using var first = await gate.LockForTaskAsync();
        using var second = await gate.LockForTaskAsync();
        Assert.True(gate.IsLocked);
        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.EnterManualAsync());
        first.Dispose();
        first.Dispose();
        Assert.True(gate.IsLocked);
        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.EnterManualAsync());
        second.Dispose();
        Assert.False(gate.IsLocked);
        using var manual = await gate.EnterManualAsync();
    }

    /// <summary>已开始的人工写入完成后，任务才能进入，避免检查与写入之间发生竞态。</summary>
    [Fact]
    public async Task TaskStart_WaitsForExistingManualCommit()
    {
        var gate = new AccountRecordsInteractionGate();
        using var manual = await gate.EnterManualAsync();
        var starting = gate.LockForTaskAsync();
        Assert.False(starting.IsCompleted);
        manual.Dispose();
        using var task = await starting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(gate.IsLocked);
    }

    /// <summary>等待人工提交时取消任务，不遗留人工操作锁。</summary>
    [Fact]
    public async Task CancelledStart_DoesNotLeaveLock()
    {
        var gate = new AccountRecordsInteractionGate();
        using var manual = await gate.EnterManualAsync();
        using var cancellation = new CancellationTokenSource();
        var starting = gate.LockForTaskAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting);
        manual.Dispose();
        Assert.False(gate.IsLocked);
        using var next = await gate.EnterManualAsync();
    }
}
