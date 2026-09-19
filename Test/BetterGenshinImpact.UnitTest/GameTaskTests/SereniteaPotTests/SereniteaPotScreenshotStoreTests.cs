using BetterGenshinImpact.GameTask.QuickSereniteaPot;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.SereniteaPotTests;

public sealed class SereniteaPotScreenshotStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "bgi-pot-retention-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void RepeatedFailures_AreBoundedAndPreserveUnrelatedFiles()
    {
        Directory.CreateDirectory(directory);
        var unrelated = Path.Combine(directory, "pot-manual.png");
        File.WriteAllText(unrelated, "user evidence");
        for (var i = 0; i < 12; i++) Save(6, maxFiles: 3, maxBytes: 13);
        var owned = Directory.GetFiles(directory, "pot-*.png").Where(p => p != unrelated).ToArray();
        Assert.InRange(owned.Length, 1, 2);
        Assert.True(owned.Sum(p => new FileInfo(p).Length) <= 13);
        Assert.Equal("user evidence", File.ReadAllText(unrelated));
    }

    [Fact]
    public void ExpiredFiles_AreRemoved()
    {
        var old = Save(1)!;
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow - TimeSpan.FromDays(15));
        Save(1);
        Assert.False(File.Exists(old));
    }

    [Fact]
    public void OversizeAndPartialWrites_DoNotAccumulate()
    {
        Assert.Null(Save(20, maxBytes: 10));
        Assert.Throws<InvalidOperationException>(() => SereniteaPotScreenshotStore.Save(directory, "failure", p =>
        { File.WriteAllBytes(p, [1]); throw new InvalidOperationException(); }));
        Assert.Null(SereniteaPotScreenshotStore.Save(directory, "failure", p =>
        { File.WriteAllBytes(p, [1]); return false; }));
        Assert.Empty(Directory.GetFiles(directory, "*.png"));
    }

    [Fact]
    public void OtherProcessHoldingGate_SkipsWriteWithoutWaiting()
    {
        Directory.CreateDirectory(directory);
        using var gate = new FileStream(Path.Combine(directory, ".pot-screenshots.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        Assert.Null(SereniteaPotScreenshotStore.Save(directory, "busy", _ => throw new Exception("Must not write")));
    }

    [Fact]
    public void ConcurrentSaves_RespectOneSharedLimit()
    {
        Parallel.For(0, 20, _ => Save(3, maxFiles: 4));
        Assert.InRange(Directory.GetFiles(directory, "*.png").Length, 1, 4);
    }

    private string? Save(int bytes, int maxFiles = 32, long maxBytes = 1024) =>
        SereniteaPotScreenshotStore.Save(directory, "failure", p =>
        { File.WriteAllBytes(p, new byte[bytes]); return true; }, maxFiles, maxBytes);

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
}
