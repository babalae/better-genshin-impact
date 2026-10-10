using BetterGenshinImpact.Helpers.Http;
using LibGit2Sharp;

namespace BetterGenshinImpact.UnitTest.HelpersTests.Http;

[Collection("Proxy globals")]
public class GitProxyTests
{
    [Fact]
    public async Task RemoteReferenceQueryUsesConfiguredHttpProxy()
    {
        await using var proxy = new LoopbackHttpServer((_, _) => Task.FromResult((503, "unavailable")));
        var snapshot = new ProxySettingsSnapshot(proxy.Address);
        var options = snapshot.CreateGitProxyOptions();
        var error = await Task.Run(() => Assert.ThrowsAny<LibGit2SharpException>(() =>
            Repository.ListRemoteReferences("https://git-proxy-test.invalid/repo.git", (_, _, _) => new DefaultCredentials(), options).ToArray()))
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(proxy.Requests.Count > 0, error.ToString());
        Assert.All(proxy.Requests, request => Assert.StartsWith("CONNECT git-proxy-test.invalid:443", request));
    }

    [Fact]
    public async Task CloneUsesConfiguredHttpProxy()
    {
        await using var proxy = new LoopbackHttpServer((_, _) => Task.FromResult((503, "unavailable")));
        var snapshot = new ProxySettingsSnapshot(proxy.Address);
        var options = new CloneOptions();
        var proxyOptions = snapshot.CreateGitProxyOptions();
        options.FetchOptions.ProxyOptions.ProxyType = proxyOptions.ProxyType;
        options.FetchOptions.ProxyOptions.Url = proxyOptions.Url;
        var path = Path.Combine(Path.GetTempPath(), "bgi-git-proxy-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var error = await Task.Run(() => Assert.ThrowsAny<LibGit2SharpException>(() =>
                Repository.Clone("https://git-proxy-test.invalid/repo.git", path, options))).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(proxy.Requests.Count > 0, error.ToString());
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public async Task FetchUsesConfiguredHttpProxyAndPreservesLocalRepository()
    {
        await using var proxy = new LoopbackHttpServer((_, _) => Task.FromResult((503, "unavailable")));
        var snapshot = new ProxySettingsSnapshot(proxy.Address);
        var options = new FetchOptions();
        var proxyOptions = snapshot.CreateGitProxyOptions();
        options.ProxyOptions.ProxyType = proxyOptions.ProxyType;
        options.ProxyOptions.Url = proxyOptions.Url;
        var path = Path.Combine(Path.GetTempPath(), "bgi-git-proxy-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Repository.Init(path);
            using var repository = new Repository(path);
            repository.Network.Remotes.Add("origin", "https://git-proxy-test.invalid/repo.git");
            var error = await Task.Run(() => Assert.ThrowsAny<LibGit2SharpException>(() =>
                Commands.Fetch(repository, "origin", new[] { "+refs/heads/release:refs/remotes/origin/release" }, options, null)))
                .WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(proxy.Requests.Count > 0, error.ToString());
            Assert.True(Repository.IsValid(path));
        }
        finally
        {
            Cleanup(path);
        }
    }

    private static void Cleanup(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (fullPath.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(fullPath).StartsWith("bgi-git-proxy-test-", StringComparison.Ordinal)
            && Directory.Exists(fullPath))
        {
            BetterGenshinImpact.Helpers.DirectoryHelper.DeleteDirectoryWithReadOnlyCheck(fullPath);
        }
    }
}
