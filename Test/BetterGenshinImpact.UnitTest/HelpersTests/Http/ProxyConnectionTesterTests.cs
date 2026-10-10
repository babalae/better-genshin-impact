using BetterGenshinImpact.Helpers.Http;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace BetterGenshinImpact.UnitTest.HelpersTests.Http;

public class ProxyConnectionTesterTests
{
    [Fact]
    public async Task TestUsesProvidedProxyAndReportsHttpStatus()
    {
        await using var proxy = new LoopbackHttpServer((_, _) => Task.FromResult((403, "denied")));
        var originalSnapshot = ProxyService.Instance.Current;
        var result = await ProxyConnectionTester.TestAsync(proxy.Address, new Uri("http://proxy-test.invalid"));
        Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
        Assert.Null(result.Error);
        Assert.Same(originalSnapshot, ProxyService.Instance.Current);
        Assert.Contains("User-Agent: BetterGI-Network-Test/1.0", Assert.Single(proxy.Requests));
    }

    [Fact]
    public async Task TestDistinguishesCancellationFromTimeout()
    {
        await using var proxy = new LoopbackHttpServer(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return (200, "unused");
        });
        var timedOut = await ProxyConnectionTester.TestAsync(proxy.Address, new Uri("http://proxy-test.invalid"),
            timeout: TimeSpan.FromMilliseconds(150));
        Assert.True(timedOut.TimedOut);
        Assert.False(timedOut.Canceled);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var canceled = await ProxyConnectionTester.TestAsync(proxy.Address, new Uri("http://proxy-test.invalid"), cancellation.Token);
        Assert.True(canceled.Canceled);
        Assert.False(canceled.TimedOut);
    }

    [Fact]
    public async Task TestReportsConnectionFailureWithoutChangingProxy()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var result = await ProxyConnectionTester.TestAsync(new Uri($"http://127.0.0.1:{port}"),
            new Uri("http://proxy-test.invalid"), timeout: TimeSpan.FromSeconds(3));
        Assert.NotNull(result.Error);
        Assert.Null(result.StatusCode);
        Assert.False(result.Canceled);
    }

    [Fact]
    public async Task SharedFactoryConstructsOnlyOneClientForConcurrentCallers()
    {
        var calls = 0;
        var key = Guid.NewGuid().ToString("N");
        var clients = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
            HttpClientFactory.GetClient(key, () =>
            {
                Interlocked.Increment(ref calls);
                return HttpClientFactory.CreateClient(TimeSpan.FromSeconds(17));
            }))));
        Assert.Equal(1, calls);
        Assert.All(clients, client => Assert.Same(clients[0], client));
        Assert.Equal(TimeSpan.FromSeconds(17), clients[0].Timeout);
    }

    [Fact]
    public void IndependentFactoryPreservesOptionsAndDisposalOwnership()
    {
        SocketsHttpHandler? capturedHandler = null;
        using var first = HttpClientFactory.CreateClient(TimeSpan.FromSeconds(15), handler =>
        {
            capturedHandler = handler;
            handler.UseCookies = false;
            handler.MaxAutomaticRedirections = 5;
        });
        using var second = HttpClientFactory.CreateClient();
        Assert.NotSame(first, second);
        Assert.Equal(TimeSpan.FromSeconds(15), first.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(100), second.Timeout);
        Assert.False(capturedHandler!.UseCookies);
        Assert.Equal(5, capturedHandler.MaxAutomaticRedirections);
        Assert.Same(ProxyService.Instance.Proxy, capturedHandler.Proxy);
        first.DefaultRequestHeaders.Add("X-Test", "private");
        Assert.False(second.DefaultRequestHeaders.Contains("X-Test"));
        using var system = HttpClientFactory.CreateClient(configureHandler: handler =>
            Assert.Same(ProxyService.Instance.OriginalProxy, handler.Proxy), useSystemProxy: true);
    }
}
