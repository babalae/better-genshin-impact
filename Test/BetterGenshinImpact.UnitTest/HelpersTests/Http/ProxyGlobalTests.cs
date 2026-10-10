using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Helpers.Http;
using System.Net;
using System.Net.Http;

namespace BetterGenshinImpact.UnitTest.HelpersTests.Http;

[CollectionDefinition("Proxy globals", DisableParallelization = true)]
public class ProxyGlobalCollection;

[Collection("Proxy globals")]
public class ProxyGlobalTests
{
    [Fact]
    public async Task DefaultProxyAlsoUpdatesExistingNonFactoryClient()
    {
        await using var original = new LoopbackHttpServer("original");
        await using var configured = new LoopbackHttpServer("configured");
        var previousDefault = HttpClient.DefaultProxy;
        try
        {
            var config = new NetworkConfig { ProxyUrl = ProxyService.FormatAddress(configured.Address) };
            var service = new ProxyService(new WebProxy(original.Address));
            service.Initialize(config);
            service.UseAsDefaultProxy();
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            const string target = "http://proxy-test.invalid/default";
            Assert.Equal("original", await client.GetStringAsync(target));
            config.Enabled = true;
            Assert.Equal("configured", await client.GetStringAsync(target));
            config.Enabled = false;
            Assert.Equal("original", await client.GetStringAsync(target));
        }
        finally
        {
            HttpClient.DefaultProxy = previousDefault;
        }
    }
}
