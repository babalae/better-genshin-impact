namespace SereniteaPot.Regression.Entry;

[CollectionDefinition("Pot entry callers", DisableParallelization = true)]
public class PotEntryCallerCollection;

[Collection("Pot entry callers")]
public class PotEntryCallerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SlowRealmName_BothEntrancesWaitBeforeRewardFlow(bool bag)
    {
        var state = EntryState.Current = new() { MapReadyAfter = 10, NameReadyAfter = 12 };
        var probe = new PotEntryProbe(bag);
        Assert.True(await probe.Run());
        Assert.Equal("绘绮庭", probe.RealmName);
        Assert.True(state.Seconds >= 13);
        Assert.Equal(1, state.Approaches);
        Assert.Equal(1, state.Rewards);
        Assert.Equal(0, state.OcrBeforeMapReady);
        Assert.Equal(0, state.OcrWithoutFreshCapture);
        Assert.Equal(0, state.LiveImages);
        Assert.Empty(state.Components);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingRealmName_BothEntrancesStopBeforeMovementAndRewards(bool bag)
    {
        var state = EntryState.Current = new() { NameReadyAfter = double.PositiveInfinity };
        var probe = new PotEntryProbe(bag);
        Assert.False(await probe.Run());
        Assert.Equal("", probe.RealmName);
        Assert.Contains("realm-name", state.Failures);
        Assert.Equal(0, state.HomeClicks);
        Assert.Equal(0, state.Approaches);
        Assert.Equal(0, state.Rewards);
        Assert.Equal(1, state.Recoveries);
        Assert.Equal(30, state.Seconds);
        Assert.Equal(0, state.LiveImages);
        Assert.Empty(state.Components);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MapNeverReady_BothEntrancesDoNotOcrOrUseUnrelatedText(bool bag)
    {
        var state = EntryState.Current = new() { MapReadyAfter = double.PositiveInfinity };
        var probe = new PotEntryProbe(bag);
        Assert.False(await probe.Run());
        Assert.Equal(0, state.OcrCalls);
        Assert.Equal("", probe.RealmName);
        Assert.Equal(0, state.HomeClicks);
        Assert.Equal(0, state.Approaches);
        Assert.Equal(0, state.Rewards);
        Assert.Equal(0, state.LiveImages);
        Assert.Empty(state.Components);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelDuringRecognition_BothEntrancesDoNotCommitNameOrContinue(bool bag)
    {
        using var cts = new CancellationTokenSource();
        var state = EntryState.Current = new() { OnOcr = cts.Cancel };
        var probe = new PotEntryProbe(bag);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe.Run(cts.Token));
        Assert.Equal("", probe.RealmName);
        Assert.Equal(0, state.HomeClicks);
        Assert.Equal(0, state.Approaches);
        Assert.Equal(0, state.Rewards);
        Assert.Equal(0, state.Recoveries);
        Assert.Equal(0, state.LiveImages);
        Assert.Empty(state.Components);
    }
}

// The generated probe compiles the actual entry and Execute methods. Only the game,
// clock, recognition and subsequent reward operations are replaced here.
partial class PotEntryProbe(bool bag)
{
    private string dongTianName = "";
    private bool fail = false;
    private readonly EntryConfig SelectedConfig = new() { SereniteaPotTpType = bag ? "尘歌壶道具" : "地图传送" };
    internal string RealmName => dongTianName;
    internal Task<bool> Run(CancellationToken ct = default) => Execute(ct);
    private static Task<bool> OpenSereniteaPotMap(CancellationToken ct) => Task.FromResult(true);
    private static Task Delay(int ms, CancellationToken ct) => EntryState.Current.Delay(ms, ct);
    private static ImageRegion CaptureToRectArea(bool forceNew = false) => new(forceNew: forceNew);
    private static Task FindAYuan(CancellationToken ct) { EntryState.Current.Approaches++; return Task.CompletedTask; }
    private static Task<bool> GetReward(CancellationToken ct) { EntryState.Current.Rewards++; return Task.FromResult(true); }
    private static Task<bool> Finished(CancellationToken ct) => Task.FromResult(true);
}

sealed class EntryConfig { public string SereniteaPotTpType = ""; }
sealed class EntryState : TimeProvider
{
    internal static EntryState Current = new();
    private long milliseconds;
    public override long TimestampFrequency => 1000;
    public override long GetTimestamp() => milliseconds;
    internal double Seconds => milliseconds / 1000d;
    internal double MapReadyAfter, NameReadyAfter;
    internal int OcrCalls, OcrBeforeMapReady, OcrWithoutFreshCapture, HomeClicks, Approaches, Rewards, Recoveries, LiveImages;
    internal bool Teleported;
    internal Action? OnOcr;
    internal readonly Dictionary<string, BetterGenshinImpact.GameTask.AutoPathing.Suspend.ISuspendable> Components = [];
    internal readonly List<string> Failures = [];
    internal Task Delay(int ms, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        milliseconds += ms;
        return Task.CompletedTask;
    }
}

static class SereniteaPotWaiter
{
    internal static Task<string?> WaitForRealmNameAsync(Func<string?> readName,
        Func<int, CancellationToken, Task> delay, CancellationToken ct, TimeProvider? timeProvider = null) =>
        BetterGenshinImpact.GameTask.QuickSereniteaPot.SereniteaPotWaiter.WaitForRealmNameAsync(
            readName, delay, ct, timeProvider ?? EntryState.Current);
}
static class SereniteaPotTaskControl
{
    internal static BetterGenshinImpact.GameTask.QuickSereniteaPot.SereniteaPotActiveTime CreateTimer() =>
        new(EntryState.Current.Components, EntryState.Current);
}
static class SereniteaPotUi
{
    internal static void SaveFailure(string stage) => EntryState.Current.Failures.Add(stage);
    internal static Task<bool> WaitForEntry(CancellationToken ct, string stage, bool afterTeleport = true) => Task.FromResult(true);
    internal static Task<bool> WaitForMainUi(CancellationToken ct, string stage) => Task.FromResult(true);
}
static class QuickSereniteaPotTask { internal static Task<bool> Start(CancellationToken ct) => Task.FromResult(true); }
sealed class ReturnMainUiTask
{
    internal Task Start(CancellationToken ct) { EntryState.Current.Recoveries++; return Task.CompletedTask; }
}
static class Logger
{
    internal static void LogInformation(string message, params object[] args) { }
    internal static void LogWarning(string message, params object[] args) { }
}
static class Simulation { internal static void ReleaseAllKey() { } }
enum GIActions { OpenMap }
sealed class TaskContext
{
    internal static TaskContext Instance() => new();
    internal Simulator PostMessageSimulator => new();
}
sealed class Simulator { internal void SimulateAction(GIActions action) { } }
static class Bv { internal static bool IsInBigMapUi(ImageRegion capture) => EntryState.Current.Seconds >= EntryState.Current.MapReadyAfter; }
enum RecognitionTypes { Ocr }
readonly record struct Rect(int X, int Y, int Width, int Height);
sealed class RecognitionObject { public RecognitionTypes RecognitionType; public Rect RegionOfInterest; }
sealed class OcrText { public string Text = "绘绮庭"; }
static class ElementRecognition { internal static string Get(string name, ImageRegion capture) => name; }
static class RecognitionAssets { internal static string Get(string group, string name, ImageRegion capture) => name; }
sealed class ImageRegion(string? element = null, bool forceNew = false) : IDisposable
{
    private readonly EntryState state = Register();
    private bool disposed;
    private static EntryState Register() { EntryState.Current.LiveImages++; return EntryState.Current; }
    public int Width => 1920;
    public int Height => 1080;
    internal ImageRegion Find(string name) => new(name);
    internal bool IsExist() => element != "TeleportButton" || !state.Teleported;
    internal void Click()
    {
        if (element == "SereniteaPotHome") state.HomeClicks++;
        if (element == "TeleportButton") state.Teleported = true;
    }
    internal List<OcrText> FindMulti(RecognitionObject recognition)
    {
        state.OcrCalls++;
        if (!Bv.IsInBigMapUi(this)) state.OcrBeforeMapReady++;
        if (!forceNew) state.OcrWithoutFreshCapture++;
        state.OnOcr?.Invoke();
        return state.Seconds >= state.NameReadyAfter ? [new OcrText()] : [];
    }
    public void Dispose() { if (!disposed) { disposed = true; state.LiveImages--; } }
}
