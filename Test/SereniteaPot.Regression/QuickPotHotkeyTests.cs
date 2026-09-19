namespace SereniteaPot.Regression.Hotkey;

[CollectionDefinition("Pot hotkey", DisableParallelization = true)]
public class PotHotkeyCollection;

[Collection("Pot hotkey")]
public class QuickPotHotkeyTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void IneligibleHotkey_DoesNotInitializeRunnerOrActivateGame(bool initialized, bool foreground)
    {
        var state = HotkeyState.Current = new() { Initialized = initialized, Foreground = foreground };
        HotkeyProbe.Done();
        Assert.Equal(0, state.RunnerStarts);
        Assert.Equal(0, state.InputStarts);
        Assert.Equal(foreground, state.Foreground);
        Assert.Equal(initialized ? 0 : 1, state.Warnings);
    }

    [Fact]
    public void ForegroundInitializedGame_StartsExactlyOneTask()
    {
        var state = HotkeyState.Current = new() { Initialized = true, Foreground = true };
        HotkeyProbe.Done();
        Assert.Equal(1, state.RunnerStarts);
        Assert.Equal(1, state.InputStarts);
        Assert.Equal(0, state.Warnings);
    }
}

// Done is extracted unchanged from production. The runner deliberately activates
// the game, reproducing the real Init-before-Start ordering without any UI input.
sealed class HotkeyState
{
    internal static HotkeyState Current = new();
    internal bool Initialized, Foreground;
    internal int RunnerStarts, InputStarts, Warnings;
}
static partial class HotkeyProbe
{
    private static Task<bool> Start(CancellationToken ct)
    {
        HotkeyState.Current.InputStarts++;
        return Task.FromResult(true);
    }
}
sealed class TaskRunner
{
    internal void FireAndForget(Func<Task> action)
    {
        HotkeyState.Current.RunnerStarts++;
        HotkeyState.Current.Foreground = true;
        action().GetAwaiter().GetResult();
    }
}
sealed class TaskContext
{
    internal static TaskContext Instance() => new();
    internal bool IsInitialized => HotkeyState.Current.Initialized;
}
static class SystemControl { internal static bool IsGenshinImpactActiveByProcess() => HotkeyState.Current.Foreground; }
static class UIDispatcherHelper { internal static void Invoke(Action action) => action(); }
static class Toast { internal static void Warning(string text) => HotkeyState.Current.Warnings++; }
sealed class CancellationContext
{
    internal static CancellationContext Instance => new();
    internal TokenSource Cts => new();
}
sealed class TokenSource { internal CancellationToken Token => CancellationToken.None; }
