using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Helpers.Http;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.ViewModel.Pages.View;
using System.Net;

namespace BetterGenshinImpact.UnitTest.HelpersTests.Http;

public class NetworkSettingsViewModelTests
{
    [Fact]
    public void EditingAddressDoesNotApplyUntilSaved()
    {
        var configService = new MemoryConfigService();
        var proxyService = new ProxyService(new WebProxy());
        proxyService.Initialize(configService.Config.NetworkConfig);
        using var viewModel = new NetworkSettingsViewModel(configService, proxyService);
        viewModel.ProxyUrl = " HTTP://localhost:80/ ";
        Assert.Equal(NetworkConfig.DefaultProxyUrl, configService.Config.NetworkConfig.ProxyUrl);
        viewModel.SaveAddressCommand.Execute(null);
        Assert.Equal("http://localhost:80", configService.Config.NetworkConfig.ProxyUrl);
        Assert.Equal("http://localhost:80", viewModel.ProxyUrl);
        Assert.False(proxyService.Current.Enabled);
    }

    [Fact]
    public void InvalidDraftDoesNotOverwriteConfigOrEnableProxy()
    {
        var configService = new MemoryConfigService();
        var proxyService = new ProxyService(new WebProxy());
        proxyService.Initialize(configService.Config.NetworkConfig);
        using var viewModel = new NetworkSettingsViewModel(configService, proxyService);
        viewModel.ProxyUrl = "not-an-address";
        viewModel.SaveAddressCommand.Execute(null);
        viewModel.Enabled = true;
        Assert.False(viewModel.Enabled);
        Assert.False(configService.Config.NetworkConfig.Enabled);
        Assert.Equal(NetworkConfig.DefaultProxyUrl, configService.Config.NetworkConfig.ProxyUrl);
        Assert.NotEmpty(viewModel.ValidationError);
    }

    [Fact]
    public void EnablingSavesCurrentValidDraftAndUpdatesStatus()
    {
        var configService = new MemoryConfigService();
        var proxyService = new ProxyService(new WebProxy());
        proxyService.Initialize(configService.Config.NetworkConfig);
        using var viewModel = new NetworkSettingsViewModel(configService, proxyService);
        viewModel.ProxyUrl = "socks5://localhost:1080";
        viewModel.Enabled = true;
        Assert.True(configService.Config.NetworkConfig.Enabled);
        Assert.True(viewModel.Enabled);
        Assert.Equal("socks5", proxyService.Current.Address!.Scheme);
        Assert.True(proxyService.Current.GitProxyUnsupported);
        Assert.True(proxyService.RequiresWebViewRestart);
        viewModel.ProxyUrl = "invalid";
        viewModel.Enabled = false;
        Assert.False(proxyService.Current.Enabled);
        Assert.Equal("socks5://localhost:1080", configService.Config.NetworkConfig.ProxyUrl);
    }

    [Fact]
    public void ExternalChangesRefreshCleanDraftButPreserveActiveEdits()
    {
        var configService = new MemoryConfigService();
        var proxyService = new ProxyService(new WebProxy());
        proxyService.Initialize(configService.Config.NetworkConfig);
        using var viewModel = new NetworkSettingsViewModel(configService, proxyService);
        configService.Config.NetworkConfig.ProxyUrl = "http://localhost:8080";
        Assert.Equal("http://localhost:8080", viewModel.ProxyUrl);
        viewModel.ProxyUrl = "socks5://localhost:";
        configService.Config.NetworkConfig.ProxyUrl = "http://localhost:9090";
        Assert.Equal("socks5://localhost:", viewModel.ProxyUrl);
    }

    private sealed class MemoryConfigService : IConfigService
    {
        public AllConfig Config { get; } = new();
        public AllConfig Get() => Config;
        public AllConfig Read() => Config;
        public void Save() { }
        public void Flush() { }
        public void Write(AllConfig config) { }
    }
}
