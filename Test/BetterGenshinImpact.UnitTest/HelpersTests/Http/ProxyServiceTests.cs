using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Helpers.Http;
using BetterGenshinImpact.Service;
using LibGit2Sharp;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace BetterGenshinImpact.UnitTest.HelpersTests.Http;

public class ProxyServiceTests
{
    [Theory]
    [InlineData("http://127.0.0.1:7890", "http://127.0.0.1:7890")]
    [InlineData("https://localhost:443/", "https://localhost:443")]
    [InlineData("socks5://localhost:1080", "socks5://localhost:1080")]
    [InlineData(" HTTP://Proxy.Example:80/ ", "http://proxy.example:80")]
    [InlineData("SOCKS5://[::1]:1080", "socks5://[::1]:1080")]
    [InlineData("https://[2001:db8::1]:8443/", "https://[2001:db8::1]:8443")]
    public void ValidAddressRoundTrips(string input, string expected)
    {
        Assert.True(ProxyService.TryParseAddress(input, out var address));
        Assert.Equal(expected, ProxyService.FormatAddress(address!));
        Assert.True(ProxyService.TryParseAddress(ProxyService.FormatAddress(address!), out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("127.0.0.1:7890")]
    [InlineData("http://localhost")]
    [InlineData("socks5://localhost")]
    [InlineData("http://localhost:")]
    [InlineData("http://localhost:0")]
    [InlineData("http://localhost:-1")]
    [InlineData("http://localhost:65536")]
    [InlineData("http://localhost:abc")]
    [InlineData("ftp://localhost:21")]
    [InlineData("socks4://localhost:1080")]
    [InlineData("http://user:password@localhost:80")]
    [InlineData("http://@localhost:80")]
    [InlineData("http://localhost:80/path")]
    [InlineData("http://localhost:80/..")]
    [InlineData("http://localhost:80?query")]
    [InlineData("http://localhost:80/#fragment")]
    [InlineData("http://localhost:80/#")]
    [InlineData("http://localhost:80/?")]
    [InlineData("http://local host:80")]
    [InlineData("http://localhost:80\n--proxy-bypass-list=*")]
    [InlineData("http://::1:80")]
    [InlineData("http://[invalid]:80")]
    [InlineData("socks5://proxy\"--flag:80")]
    public void InvalidAddressIsRejected(string? input)
    {
        Assert.False(ProxyService.TryParseAddress(input, out var address));
        Assert.Null(address);
    }

    [Fact]
    public void MissingConfigUsesDefaultsAndSerializationRoundTrips()
    {
        var oldConfig = JsonSerializer.Deserialize<AllConfig>("{}", ConfigService.JsonOptions)!;
        Assert.False(oldConfig.NetworkConfig.Enabled);
        Assert.Equal(NetworkConfig.DefaultProxyUrl, oldConfig.NetworkConfig.ProxyUrl);
        oldConfig.NetworkConfig.Enabled = true;
        oldConfig.NetworkConfig.ProxyUrl = "socks5://[::1]:1080";
        var json = JsonSerializer.Serialize(oldConfig.NetworkConfig, ConfigService.JsonOptions);
        var copy = JsonSerializer.Deserialize<NetworkConfig>(json, ConfigService.JsonOptions)!;
        Assert.True(copy.Enabled);
        Assert.Equal(oldConfig.NetworkConfig.ProxyUrl, copy.ProxyUrl);
    }

    [Fact]
    public void NetworkConfigParticipatesInSaveNotifications()
    {
        var config = new AllConfig();
        var changes = 0;
        config.OnAnyChangedAction = () => changes++;
        config.InitEvent();
        config.NetworkConfig.ProxyUrl = "http://localhost:10001";
        config.NetworkConfig.Enabled = true;
        Assert.Equal(2, changes);
    }

    [Fact]
    public void InvalidStartupConfigIsDisabled()
    {
        var config = new NetworkConfig { Enabled = true, ProxyUrl = "http://localhost" };
        var service = new ProxyService(new WebProxy());
        service.Initialize(config);
        Assert.False(config.Enabled);
        Assert.False(service.Current.Enabled);
        Assert.True(service.HasInvalidConfiguration);
        Assert.False(service.RequiresWebViewRestart);
    }

    [Fact]
    public void SettingsChangesKeepStableProxyAndStartupSnapshot()
    {
        var config = new NetworkConfig { Enabled = true };
        var service = new ProxyService(new WebProxy());
        service.Initialize(config);
        var initial = service.Current;
        var proxy = service.Proxy;
        var changes = 0;
        service.SettingsChanged += (_, _) => changes++;
        config.ProxyUrl = "socks5://localhost:1080";
        Assert.Same(proxy, service.Proxy);
        Assert.Equal(initial, service.Startup);
        Assert.True(service.RequiresWebViewRestart);
        Assert.Equal("--disable-gpu --proxy-server=\"http://127.0.0.1:7890\"",
            service.GetWebViewBrowserArguments("--disable-gpu"));
        config.Enabled = false;
        Assert.False(service.Current.Enabled);
        Assert.Equal(2, changes);
    }

    [Theory]
    [InlineData("http://localhost:7890", ProxyType.Specified, false)]
    [InlineData("https://localhost:7890", ProxyType.None, true)]
    [InlineData("socks5://localhost:1080", ProxyType.None, true)]
    public void GitOptionsAndBrowserArgumentsFollowProtocol(string url, ProxyType type, bool unsupported)
    {
        var snapshot = new ProxySettingsSnapshot(new Uri(url));
        var options = snapshot.CreateGitProxyOptions();
        Assert.Equal(type, options.ProxyType);
        Assert.Equal(unsupported, snapshot.GitProxyUnsupported);
        Assert.Equal(type == ProxyType.Specified ? url : null, options.Url);
        Assert.Equal($"--flag=value --proxy-server=\"{url}\"",
            snapshot.GetBrowserArguments("--flag=value"));
        Assert.NotSame(options, snapshot.CreateGitProxyOptions());
    }

    [Fact]
    public void DisabledSnapshotDoesNotForceBrowserDirectConnection()
    {
        var snapshot = new ProxySettingsSnapshot(null);
        Assert.Equal("--existing", snapshot.GetBrowserArguments("--existing"));
        Assert.Equal(ProxyType.None, snapshot.CreateGitProxyOptions().ProxyType);
        Assert.False(snapshot.GitProxyUnsupported);
    }

    [Fact]
    public void HttpGitRemoteCannotSilentlyBypassCustomProxy()
    {
        var snapshot = new ProxySettingsSnapshot(new Uri("http://localhost:7890"));
        Assert.Throws<NotSupportedException>(() => snapshot.ValidateGitRemote("http://repo.example/repo.git"));
        snapshot.ValidateGitRemote("https://repo.example/repo.git");
        new ProxySettingsSnapshot(null).ValidateGitRemote("http://repo.example/repo.git");
    }

    [Fact]
    public async Task AlreadyUsedClientSwitchesProxyAndReturnsToOriginal()
    {
        await using var original = new LoopbackHttpServer("original");
        await using var first = new LoopbackHttpServer("first");
        await using var second = new LoopbackHttpServer("second");
        var config = new NetworkConfig();
        var service = new ProxyService(new WebProxy(original.Address));
        service.Initialize(config);
        using var client = CreateClient(service);
        const string target = "http://proxy-test.invalid/resource";

        Assert.Equal("original", await client.GetStringAsync(target));
        config.ProxyUrl = ProxyService.FormatAddress(first.Address);
        config.Enabled = true;
        Assert.Equal("first", await client.GetStringAsync(target));
        config.ProxyUrl = ProxyService.FormatAddress(second.Address);
        Assert.Equal("second", await client.GetStringAsync(target));
        config.Enabled = false;
        Assert.Equal("original", await client.GetStringAsync(target));
        Assert.Equal(2, original.Requests.Count);
        Assert.Single(first.Requests);
        Assert.Single(second.Requests);
    }

    [Fact]
    public async Task DisabledProxyPreservesBypassAndDirectBehavior()
    {
        await using var direct = new LoopbackHttpServer("direct");
        await using var proxy = new LoopbackHttpServer("proxy");
        var original = new WebProxy(proxy.Address) { BypassProxyOnLocal = true };
        var config = new NetworkConfig { Enabled = true, ProxyUrl = ProxyService.FormatAddress(proxy.Address) };
        var service = new ProxyService(original);
        service.Initialize(config);
        using var client = CreateClient(service);
        Assert.Equal("proxy", await client.GetStringAsync(direct.Address));
        config.Enabled = false;
        Assert.Equal("direct", await client.GetStringAsync(direct.Address));
        Assert.Single(direct.Requests);
        Assert.Single(proxy.Requests);
    }

    [Fact]
    public async Task SwitchingDoesNotCancelInFlightRequest()
    {
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var slow = new LoopbackHttpServer(async (_, token) =>
        {
            received.TrySetResult();
            await release.Task.WaitAsync(token);
            return (200, "slow");
        });
        await using var fast = new LoopbackHttpServer("fast");
        var config = new NetworkConfig { Enabled = true, ProxyUrl = ProxyService.FormatAddress(slow.Address) };
        var service = new ProxyService(new WebProxy());
        service.Initialize(config);
        using var client = CreateClient(service);
        var pending = client.GetStringAsync("http://proxy-test.invalid/slow");
        await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        config.ProxyUrl = ProxyService.FormatAddress(fast.Address);
        Assert.Equal("fast", await client.GetStringAsync("http://proxy-test.invalid/fast"));
        release.TrySetResult();
        Assert.Equal("slow", await pending);
    }

    [Fact]
    public void CustomProxyDoesNotReceiveOriginalCredentials()
    {
        var original = new WebProxy("http://original-proxy:8080")
        {
            Credentials = new NetworkCredential("user", "password")
        };
        var config = new NetworkConfig();
        var service = new ProxyService(original);
        service.Initialize(config);
        var originalAddress = service.Proxy.GetProxy(new Uri("http://example.com"))!;
        Assert.NotNull(service.Proxy.Credentials!.GetCredential(originalAddress, "Basic"));
        config.ProxyUrl = "http://custom-proxy:8080";
        config.Enabled = true;
        Assert.Null(service.Proxy.Credentials!.GetCredential(new Uri(config.ProxyUrl), "Basic"));
    }

    [Fact]
    public async Task AlreadyUsedClientSwitchesBetweenHttpAndSocks5()
    {
        await using var http = new LoopbackHttpServer("http");
        await using var socks = new LoopbackSocks5Server("socks");
        var config = new NetworkConfig { Enabled = true, ProxyUrl = ProxyService.FormatAddress(http.Address) };
        var service = new ProxyService(new WebProxy());
        service.Initialize(config);
        using var client = CreateClient(service);
        const string target = "http://proxy-test.invalid/resource";
        Assert.Equal("http", await client.GetStringAsync(target));
        config.ProxyUrl = ProxyService.FormatAddress(socks.Address);
        Assert.Equal("socks", await client.GetStringAsync(target));
        Assert.Equal("proxy-test.invalid", Assert.Single(socks.Destinations));
        config.ProxyUrl = ProxyService.FormatAddress(http.Address);
        Assert.Equal("http", await client.GetStringAsync(target));
    }

    [Fact]
    public async Task ConcurrentRequestsRemainUsableWhileProxyChanges()
    {
        await using var original = new LoopbackHttpServer("original");
        await using var first = new LoopbackHttpServer("first");
        await using var second = new LoopbackHttpServer("second");
        var config = new NetworkConfig { Enabled = true, ProxyUrl = ProxyService.FormatAddress(first.Address) };
        var service = new ProxyService(new WebProxy(original.Address));
        service.Initialize(config);
        using var client = CreateClient(service);
        var requests = Enumerable.Range(0, 60).Select(async i =>
        {
            await Task.Delay(i % 10);
            var result = await client.GetStringAsync("http://proxy-test.invalid/concurrent");
            Assert.Contains(result, new[] { "original", "first", "second" });
        });
        var switching = Task.Run(async () =>
        {
            for (var i = 0; i < 30; i++)
            {
                config.ProxyUrl = ProxyService.FormatAddress(i % 2 == 0 ? first.Address : second.Address);
                config.Enabled = i % 3 != 0;
                await Task.Delay(1);
            }
        });
        await Task.WhenAll(requests.Append(switching));
        Assert.Equal(60, original.Requests.Count + first.Requests.Count + second.Requests.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TlsConnectionsFollowChangedProxy(bool secureProxy)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=proxy-test.invalid", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        // Windows Schannel 需要可导入的私钥容器，释放证书时一并清理，不导入系统信任库。
        using var certificate = new X509Certificate2(generated.Export(X509ContentType.Pfx));
        await using var first = new LoopbackHttpServer("first", certificate, secureProxy);
        await using var second = new LoopbackHttpServer("second", certificate, secureProxy);
        var config = new NetworkConfig { Enabled = true, ProxyUrl = ProxyService.FormatAddress(first.Address) };
        var service = new ProxyService(new WebProxy());
        service.Initialize(config);
        using var handler = new SocketsHttpHandler
        {
            Proxy = service.Proxy,
            SslOptions = new SslClientAuthenticationOptions
            {
                // 仅信任本测试在内存中生成的证书，不修改操作系统信任库。
                RemoteCertificateValidationCallback = (_, presented, _, _) => presented?.GetCertHashString() == certificate.GetCertHashString()
            }
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var target = secureProxy ? "http://proxy-test.invalid/tls-proxy" : "https://proxy-test.invalid/tunnel";
        Assert.Equal("first", await client.GetStringAsync(target));
        config.ProxyUrl = ProxyService.FormatAddress(second.Address);
        Assert.Equal("second", await client.GetStringAsync(target));
        config.ProxyUrl = ProxyService.FormatAddress(first.Address);
        Assert.Equal("first", await client.GetStringAsync(target));
    }

    private static HttpClient CreateClient(ProxyService service) => new(new SocketsHttpHandler
    {
        UseProxy = true,
        Proxy = service.Proxy
    }) { Timeout = TimeSpan.FromSeconds(5) };
}
