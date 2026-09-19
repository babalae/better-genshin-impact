using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;

namespace SereniteaPot.Regression.Compatibility;

[CollectionDefinition("Domain compatibility", DisableParallelization = true)]
public class DomainCompatibilityCollection;

[Collection("Domain compatibility")]
public class DomainCancellationCompatibilityTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task DomainCompletion_LocalCombatCancellationAllowsRewardContinuation(int iteration)
    {
        using var parent = new CancellationTokenSource();
        SleepSource.Ready.Reset();
        await new DomainProbe(parent, cancelParent: false).Run().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(parent.IsCancellationRequested, $"Parent was cancelled during completion iteration {iteration}");
        Assert.False(AutoFightTask.FightStatusFlag);
        // Reaching the caller after await StartFight is the contract required by its reward stage.
    }

    [Fact]
    public async Task ParentCancellation_RemainsObservableAfterChildCleanup()
    {
        using var parent = new CancellationTokenSource();
        SleepSource.Ready.Reset();
        await new DomainProbe(parent, cancelParent: true).Run().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(parent.IsCancellationRequested);
        Assert.Throws<OperationCanceledException>(() => parent.Token.ThrowIfCancellationRequested());
    }

    [Fact]
    public void AlreadyCancelledSleep_KeepsNormalEndContract()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<NormalEndException>(() => SleepSource.Sleep(300, cts.Token));
    }

    [Fact]
    public async Task UnexpectedCombatFailure_IsNotSwallowedAsNormalCompletion()
    {
        using var parent = new CancellationTokenSource();
        SleepSource.Ready.Set();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new DomainProbe(parent, false, fail: true)
            .Run().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(AutoFightTask.FightStatusFlag);
    }
}

static class Logger
{
    public static void LogInformation(string text, params object[] args) { }
    public static void LogWarning(string text) { }
}
static class Simulation { public static void ReleaseAllKey() { } }
static class AutoFightTask { public static bool FightStatusFlag; }
static class NewRetry
{
    public static void Do(Action action, TimeSpan interval, int count) => action();
}
static partial class SleepSource
{
    internal static readonly ManualResetEventSlim Ready = new(false);
    private static void TrySuspend(CancellationToken ct = default) { }
    private static void CheckAndActivateGameWindow(CancellationToken ct = default) => Ready.Set();
}
class CombatScenes
{
    internal CancellationToken Token;
    public void BeforeTask(CancellationToken token) => Token = token;
}
class CombatCommand(bool fail)
{
    public void Execute(CombatScenes scenes)
    {
        if (fail) throw new InvalidOperationException("Unexpected combat error");
        SleepSource.Sleep(300, scenes.Token);
    }
}
partial class DomainProbe(CancellationTokenSource parent, bool cancelParent, bool fail = false)
{
    private readonly CancellationToken _ct = parent.Token;
    internal Task Run() => StartFight(new CombatScenes(), [new CombatCommand(fail)]);
    private bool IsDomainEnd()
    {
        if (!SleepSource.Ready.Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException("Combat never entered Sleep");
        Thread.Sleep(30);
        if (cancelParent) { parent.Cancel(); return false; }
        return true;
    }
    private static Task Delay(int ms, CancellationToken ct) => Task.Delay(ms, ct);
}
