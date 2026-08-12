using BetterGenshinImpact.Core.Script.Dependence;

namespace BetterGenshinImpact.UnitTest.CoreTests.ScriptTests;

public sealed class LimitedFileTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), $"bgi-limited-file-{Guid.NewGuid():N}");

    public LimitedFileTests()
    {
        Directory.CreateDirectory(_rootPath);
    }

    [Fact]
    public void ReadTextSyncOrThrowReturnsFileContent()
    {
        File.WriteAllText(Path.Combine(_rootPath, "route.json"), "{\"name\":\"test\"}");

        var content = new LimitedFile(_rootPath).ReadTextSyncOrThrow("route.json");

        Assert.Equal("{\"name\":\"test\"}", content);
    }

    [Fact]
    public void ReadTextSyncOrThrowPreservesMissingFileException()
    {
        var exception = Assert.Throws<FileNotFoundException>(() =>
            new LimitedFile(_rootPath).ReadTextSyncOrThrow("missing.json"));

        Assert.EndsWith("missing.json", exception.FileName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AutoPathingRunFilePreservesMissingFileException()
    {
        var exception = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            new AutoPathingScript(_rootPath, config: null, new LimitedFile(_rootPath), (_, _) => { })
                .RunFile("missing-route.json"));

        Assert.EndsWith("missing-route.json", exception.FileName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AutoPathingRunFileFromUserPreservesMissingFileException()
    {
        var exception = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            new AutoPathingScript(_rootPath, config: null, new LimitedFile(_rootPath), (_, _) => { })
                .RunFileFromUser("missing-user-route.json"));

        Assert.EndsWith("missing-user-route.json", exception.FileName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AutoPathingRunFileDoesNotReportExecutionFailureAsReadFailure()
    {
        File.WriteAllText(Path.Combine(_rootPath, "invalid-route.json"), "not-json");
        var failureMessages = new List<string>();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            new AutoPathingScript(
                    _rootPath,
                    config: null,
                    new LimitedFile(_rootPath),
                    (message, _) => failureMessages.Add(message))
                .RunFile("invalid-route.json"));

        Assert.Equal(["执行地图追踪时候发生错误"], failureMessages);
    }

    public void Dispose()
    {
        Directory.Delete(_rootPath, recursive: true);
    }
}
