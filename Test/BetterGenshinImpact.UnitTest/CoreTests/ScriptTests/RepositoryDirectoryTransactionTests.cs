using BetterGenshinImpact.Core.Script;

namespace BetterGenshinImpact.UnitTest.CoreTests.ScriptTests;

public class RepositoryDirectoryTransactionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bgi-repository-test-" + Guid.NewGuid().ToString("N"));

    public RepositoryDirectoryTransactionTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void FailedDownloadKeepsOriginalRepository()
    {
        var target = CreateOriginal();
        Assert.Throws<IOException>(() => RepositoryDirectoryTransaction.Replace(target, staged =>
        {
            Directory.CreateDirectory(staged);
            File.WriteAllText(Path.Combine(staged, "partial"), "incomplete");
            throw new IOException("proxy connection failed");
        }, _ => { }));
        Assert.Equal("original", File.ReadAllText(Path.Combine(target, "repo.json")));
        Assert.Single(Directory.GetDirectories(_root));
    }

    [Fact]
    public void FailedValidationKeepsOriginalRepository()
    {
        var target = CreateOriginal();
        Assert.Throws<InvalidDataException>(() => RepositoryDirectoryTransaction.Replace(target,
            staged => Directory.CreateDirectory(staged),
            _ => throw new InvalidDataException("missing repo.json")));
        Assert.Equal("original", File.ReadAllText(Path.Combine(target, "repo.json")));
        Assert.Single(Directory.GetDirectories(_root));
    }

    [Fact]
    public void SuccessfulDownloadReplacesRepositoryAfterValidation()
    {
        var target = CreateOriginal();
        RepositoryDirectoryTransaction.Replace(target, staged =>
        {
            Directory.CreateDirectory(staged);
            File.WriteAllText(Path.Combine(staged, "repo.json"), "new");
        }, staged =>
        {
            Assert.Equal("original", File.ReadAllText(Path.Combine(target, "repo.json")));
            Assert.Equal("new", File.ReadAllText(Path.Combine(staged, "repo.json")));
        });
        Assert.Equal("new", File.ReadAllText(Path.Combine(target, "repo.json")));
        Assert.Single(Directory.GetDirectories(_root));
    }

    [Fact]
    public void MissingStagedDirectoryDoesNotMoveOriginal()
    {
        var target = CreateOriginal();
        Assert.Throws<DirectoryNotFoundException>(() => RepositoryDirectoryTransaction.Promote(Path.Combine(_root, "missing"), target));
        Assert.Equal("original", File.ReadAllText(Path.Combine(target, "repo.json")));
    }

    [Fact]
    public void LockedOriginalIsNotDeletedWhenPromotionFails()
    {
        var target = CreateOriginal();
        using var held = new FileStream(Path.Combine(target, "repo.json"), FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.Throws<IOException>(() => RepositoryDirectoryTransaction.Replace(target, staged =>
        {
            Directory.CreateDirectory(staged);
            File.WriteAllText(Path.Combine(staged, "repo.json"), "new");
        }, _ => { }));
        Assert.Equal("original", File.ReadAllText(Path.Combine(target, "repo.json")));
        Assert.Single(Directory.GetDirectories(_root));
    }

    private string CreateOriginal()
    {
        var target = Path.Combine(_root, "repo");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "repo.json"), "original");
        return target;
    }

    public void Dispose()
    {
        var fullPath = Path.GetFullPath(_root);
        var tempRoot = Path.GetFullPath(Path.GetTempPath());
        if (fullPath.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(fullPath).StartsWith("bgi-repository-test-", StringComparison.Ordinal)
            && Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, true);
        }
    }
}
