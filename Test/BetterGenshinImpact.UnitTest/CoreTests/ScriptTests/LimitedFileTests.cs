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

    public void Dispose()
    {
        Directory.Delete(_rootPath, recursive: true);
    }
}
