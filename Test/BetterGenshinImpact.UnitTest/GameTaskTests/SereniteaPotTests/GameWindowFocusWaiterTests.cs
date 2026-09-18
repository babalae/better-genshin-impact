using BetterGenshinImpact.GameTask.Common;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.SereniteaPotTests;

public class GameWindowFocusWaiterTests
{
    [Fact]
    public void ClosedGame_ShouldStopBeforeTryingToRestoreFocus()
    {
        var attempts = 0;
        Assert.Throws<InvalidOperationException>(() => GameWindowFocusWaiter.Wait(
            () => false, () => false, _ => attempts++, CancellationToken.None, _ => { }));
        Assert.Equal(0, attempts);
    }

    [Fact]
    public void CancellationDuringRecovery_ShouldStopFurtherWindowActions()
    {
        using var cts = new CancellationTokenSource();
        var attempts = 0;
        Assert.ThrowsAny<OperationCanceledException>(() => GameWindowFocusWaiter.Wait(
            () => true, () => false, _ => attempts++, cts.Token, _ => cts.Cancel()));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public void UnrecoverableFocus_ShouldHaveFiniteAttempts()
    {
        var attempts = 0;
        Assert.Throws<TimeoutException>(() => GameWindowFocusWaiter.Wait(
            () => true, () => false, _ => attempts++, CancellationToken.None, _ => { }));
        Assert.Equal(30, attempts);
    }

    [Fact]
    public void RecoveredFocus_ShouldStopSendingRestoreActions()
    {
        var attempts = 0;
        GameWindowFocusWaiter.Wait(() => true, () => attempts >= 3,
            _ => attempts++, CancellationToken.None, _ => { });
        Assert.Equal(3, attempts);
    }
}
