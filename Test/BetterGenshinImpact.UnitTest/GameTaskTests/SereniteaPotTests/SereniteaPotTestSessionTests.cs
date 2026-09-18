using BetterGenshinImpact.GameTask.QuickSereniteaPot;
using Newtonsoft.Json.Linq;
using Serilog;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.SereniteaPotTests;

public class SereniteaPotTestSessionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bgi-pot-test-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task LogScope_ShouldFollowAsyncWorkAndExcludeUnrelatedLogs()
    {
        using var session = new SereniteaPotTestSession(_directory, new { EntryType = "bag" });
        using var log = new LoggerConfiguration().WriteTo.Sink(new SereniteaPotTestLogSink()).CreateLogger();
        using (SereniteaPotTestLogSink.Capture(session))
        {
            log.Information("inside-test");
            await Task.Run(() => log.Information("child-task"));
        }
        log.Information("unrelated-task");
        session.Complete("entry-confirmed");
        session.Dispose();
        var content = File.ReadAllText(Path.Combine(session.DirectoryPath, "test.log"));
        Assert.Contains("inside-test", content);
        Assert.Contains("child-task", content);
        Assert.DoesNotContain("unrelated-task", content);
        Assert.Null(SereniteaPotTestLogSink.Current);
    }

    [Fact]
    public void NestedScope_ShouldRestoreThePreviousSession()
    {
        using var first = new SereniteaPotTestSession(_directory, new { });
        using var second = new SereniteaPotTestSession(_directory, new { });
        Assert.NotEqual(first.DirectoryPath, second.DirectoryPath);
        using (SereniteaPotTestLogSink.Capture(first))
        {
            using (SereniteaPotTestLogSink.Capture(second))
                Assert.Same(second, SereniteaPotTestLogSink.Current);
            Assert.Same(first, SereniteaPotTestLogSink.Current);
        }
        Assert.Null(SereniteaPotTestLogSink.Current);
    }

    [Fact]
    public void FailedTest_ShouldPersistStatusAndOriginalFailureStage()
    {
        using var session = new SereniteaPotTestSession(_directory, new { EntryType = "map" });
        session.FailureStage = "map-entry";
        session.Complete("failed", "entry was not confirmed");
        var result = JObject.Parse(File.ReadAllText(Path.Combine(session.DirectoryPath, "result.json")));
        Assert.Equal("failed", (string?)result["Status"]);
        Assert.Equal("map-entry", (string?)result["FailureStage"]);
        Assert.Equal("map", (string?)result["Metadata"]?["EntryType"]);
        Assert.NotNull(result["FinishedAt"]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
