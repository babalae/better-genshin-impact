using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Common;

namespace BetterGenshinImpact.UnitTest.GameTaskTests;

/// <summary>
/// 游戏等待的停止回归，不初始化应用宿主、不访问真实窗口或模拟输入。
/// </summary>
public sealed class GameTaskWaitTests
{
    /// <summary>
    /// 已取消时优先中断，不能再访问可能已被释放的游戏窗口。
    /// </summary>
    [Fact]
    public void AlreadyCancelled_DoesNotAccessRuntime()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var wait = new GameTaskWait(() => throw new InvalidOperationException("不应访问窗口"), cancellation.Token);
        Assert.Throws<NormalEndException>(() => wait.Sleep(1000));
    }

    /// <summary>
    /// 游戏失焦时的长同步等待必须响应取消，不等整段延时完成。
    /// </summary>
    [Fact]
    public async Task LongWait_CancellationInterruptsImmediately()
    {
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wait = new GameTaskWait(() => { entered.TrySetResult(); return true; }, cancellation.Token);
        var execution = Task.Run(() => wait.Sleep(60000));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            cancellation.Cancel();
            await Assert.ThrowsAsync<NormalEndException>(() => execution.WaitAsync(TimeSpan.FromSeconds(15)));
        }
        finally
        {
            cancellation.Cancel();
        }
    }

    /// <summary>
    /// 截图重试等无令牌调用也必须在运行环境解绑或游戏退出后结束等待。
    /// </summary>
    [Theory]
    [InlineData(60000)]
    [InlineData(Timeout.Infinite)]
    public async Task LegacyWait_RuntimeLossInterruptsWithoutToken(int millisecondsTimeout)
    {
        var available = 1;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wait = new GameTaskWait(() =>
        {
            entered.TrySetResult();
            return Volatile.Read(ref available) == 1;
        }, CancellationToken.None);
        var execution = Task.Run(() => wait.Sleep(millisecondsTimeout));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Volatile.Write(ref available, 0);
            var exception = await Assert.ThrowsAsync<NormalEndException>(
                () => execution.WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.Contains("运行环境已停止", exception.Message);
        }
        finally
        {
            Volatile.Write(ref available, 0);
        }
    }

    /// <summary>
    /// 已停止的环境不能进入新一轮暂停或焦点恢复。
    /// </summary>
    [Fact]
    public void StoppedRuntime_RejectsFurtherWaiting()
    {
        var wait = new GameTaskWait(() => false, CancellationToken.None);
        Assert.Throws<NormalEndException>(wait.ThrowIfStopped);
        Assert.Throws<NormalEndException>(() => wait.Sleep(0));
    }

    /// <summary>
    /// 正常环境允许零延时，同时保持 Thread.Sleep 对非法负值的约束。
    /// </summary>
    [Fact]
    public void HealthyRuntime_ZeroDelayCompletesAndInvalidDelayIsRejected()
    {
        var wait = new GameTaskWait(() => true, CancellationToken.None);
        wait.Sleep(0);
        Assert.Throws<ArgumentOutOfRangeException>(() => wait.Sleep(-2));
    }
}
