using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.IO;
using System.Text;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.QuestVoice;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>验证真实页面模型的独立引擎配置与嵌套通知，不启动游戏或合成。</summary>
public sealed class ExtraTtsViewModelTests
{
    /// <summary>自定义音色可以手输，各引擎切换不会改写旧火山配置。</summary>
    [Fact]
    public void Manual_voice_ids_are_saved_per_provider_and_preserve_huoshan()
    {
        using var page = new PassivePage();
        var model = page.Owner;
        model.Config.HuoshanVoiceId = "zh_female_qingxin";
        model.Config.ProviderId = "openai-speech";
        model.SelectedVoiceId = "compatible.custom.voice";
        Assert.Equal("compatible.custom.voice", model.SelectedVoiceId);
        model.Config.ProviderId = "voicevox";
        model.SelectedVoiceId = "42";
        Assert.Equal("42", model.SelectedVoiceId);
        model.Config.ProviderId = "openai-speech";
        Assert.Equal("compatible.custom.voice", model.SelectedVoiceId);
        Assert.Equal("zh_female_qingxin", model.Config.HuoshanVoiceId);
        model.SelectedVoice = null;
        Assert.Equal("compatible.custom.voice", model.SelectedVoiceId);
        var restored = JsonConvert.DeserializeObject<QuestVoiceConfig>(JsonConvert.SerializeObject(model.Config))!;
        Assert.Equal("compatible.custom.voice", restored.ExtraProviders["openai-speech"].VoiceId);
        Assert.Equal("42", restored.ExtraProviders["voicevox"].VoiceId);
    }

    /// <summary>嵌套配置修改音色时必须通知下拉代理；地址改变也使实例代理重新求值。</summary>
    [Fact]
    public void Nested_engine_changes_notify_voice_and_instance_projections()
    {
        using var page = new PassivePage();
        var model = page.Owner;
        model.Config.ProviderId = "voicevox";
        var changes = new List<string?>();
        model.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        model.Config.ExtraProviders["voicevox"].VoiceId = "13";
        Assert.Equal("13", model.SelectedVoiceId);
        Assert.Contains(nameof(model.SelectedVoiceId), changes);
        Assert.Contains(nameof(model.SelectedVoice), changes);
        changes.Clear();
        model.Config.ExtraProviders["voicevox"].UseRemote = true;
        Assert.Contains("ActiveExtraServiceUrl", changes);
    }

    /// <summary>仅切换到需要服务发现的引擎不得主动请求用户配置的地址。</summary>
    [Theory]
    [InlineData("voicevox")]
    [InlineData("vits-simple-api")]
    public async Task Switching_network_discovered_provider_does_not_contact_server(string providerId)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var page = new PassivePage();
        page.Owner.Config.ExtraProviders[providerId].LocalUrl = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/";
        page.Owner.Config.ProviderId = providerId;
        await Task.Delay(150);
        Assert.False(listener.Pending(), "切换引擎应等待用户点击刷新声音，不能自动联网。");
    }
    /// <summary>同引擎的实例或URL切走再切回，旧请求的成功和失败都不得覆盖当前选择。</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Late_voice_discovery_is_ignored_after_instance_roundtrip(bool management, bool rejected)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var page = new PassivePage();
        var owner = page.Owner;
        var address = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/";
        var extra = owner.Config.ExtraProviders["voicevox"];
        extra.LocalUrl = address;
        extra.RemoteUrl = address;
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = ReplyWhenReleasedAsync(listener, received, release, rejected);
        Task refresh;
        if (management)
        {
            owner.Management.NewCatalogCommand.Execute(null);
            owner.Management.AddProfileCommand.Execute(null);
            var profile = owner.Management.SelectedProfile!;
            profile.ProviderId = "voicevox";
            profile.ProviderVoiceId = "0";
            profile.InstanceId = "local";
            // 通过生成命令取真实刷新入口，测试在功能实现前仍可编译。
            var command = owner.Management.GetType().GetProperty("RefreshPresetVoicesCommand")?.GetValue(owner.Management)
                as CommunityToolkit.Mvvm.Input.IAsyncRelayCommand;
            Assert.NotNull(command);
            refresh = command.ExecuteAsync(null);
            await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            profile.InstanceId = "remote";
            profile.InstanceId = "local";
        }
        else
        {
            owner.Config.ProviderId = "voicevox";
            refresh = owner.RefreshVoicesCommand.ExecuteAsync(null);
            await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            extra.LocalUrl = address + "changed/";
            extra.LocalUrl = address;
        }
        owner.CommandError = "当前选择的诊断";
        release.SetResult();
        await server.WaitAsync(TimeSpan.FromSeconds(5));
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(management ? owner.Management.PresetVoices : owner.Voices);
        Assert.Equal("当前选择的诊断", owner.CommandError);
    }

    /// <summary>仅回复一次loopback声音请求，用门闩确定返回顺序，不依赖计时竞态。</summary>
    private static async Task ReplyWhenReleasedAsync(TcpListener listener, TaskCompletionSource received,
        TaskCompletionSource release, bool rejected)
    {
        using var client = await listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        Assert.StartsWith("GET /speakers ", await reader.ReadLineAsync());
        while (await reader.ReadLineAsync() is { Length: > 0 }) { }
        received.SetResult();
        await release.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var body = rejected ? "{}" : "[{\"name\":\"Test\",\"styles\":[{\"name\":\"Normal\",\"id\":13}]}]";
        var status = rejected ? "500 Error" : "200 OK";
        var response = $"HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
        await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
    }
    /// <summary>切换不同范围的引擎不应归一化或覆盖各自保存的速度。</summary>
    [Fact]
    public void Switching_provider_roundtrip_preserves_independent_rate_ranges()
    {
        using var page = new PassivePage();
        var model = page.Owner;
        model.Config.ExtraProviders["windows-sapi"].Rate = 2;
        model.Config.ExtraProviders["openai-speech"].Rate = 4;
        model.Config.ProviderId = "windows-sapi";
        Assert.Equal(2, model.SelectedEngineConfig!.Rate);
        model.Config.ProviderId = "openai-speech";
        Assert.Equal(4, model.SelectedEngineConfig!.Rate);
        model.Config.ProviderId = "windows-sapi";
        Assert.Equal(2, model.SelectedEngineConfig!.Rate);
        Assert.Equal(4, model.Config.ExtraProviders["openai-speech"].Rate);
    }

    /// <summary>旧JSON显式null字典也能构造页面并切换新增引擎。</summary>
    [Fact]
    public void Null_legacy_dictionary_is_normalized_before_subscription()
    {
        using var page = new PassivePage(nullExtras: true);
        Assert.NotNull(page.Owner.Config.ExtraProviders);
        page.Owner.Config.ProviderId = "voicevox";
        Assert.NotNull(page.Owner.SelectedEngineConfig);
        Assert.Equal("0", page.Owner.SelectedVoiceId);
    }
    /// <summary>OpenAI模型改变后设置与管理页立即更新静态建议，自定义音色仍独立保留。</summary>
    [Fact]
    public void Openai_model_change_refreshes_both_static_lists_and_preserves_manual_ids()
    {
        using var page = new PassivePage();
        var model = page.Owner;
        var extra = model.Config.ExtraProviders["openai-speech"];
        extra.Model = "gpt-4o-mini-tts";
        model.Config.ProviderId = "openai-speech";
        model.SelectedVoiceId = "custom.default";
        var management = model.Management;
        management.NewCatalogCommand.Execute(null);
        management.AddProfileCommand.Execute(null);
        management.SelectedProfile!.ProviderId = "openai-speech";
        management.SelectedProfile.ProviderVoiceId = "custom.profile";
        Assert.Equal(13, model.Voices.Count);
        Assert.Equal(13, management.PresetVoices.Count);
        extra.Model = "tts-1";
        Assert.Equal(9, model.Voices.Count);
        Assert.Equal(9, management.PresetVoices.Count);
        Assert.Equal("custom.default", model.SelectedVoiceId);
        Assert.Equal("custom.profile", management.SelectedProfile.ProviderVoiceId);
    }
    /// <summary>服务只提供空闲状态；不会创建播放器、OCR 或运行宿主。</summary>
    private sealed class PassivePage : IDisposable
    {
        /// <summary>夹具拥有的生产控制器。</summary>
        private readonly QuestVoiceSessionController _controller;
        /// <summary>夹具拥有的生产服务。</summary>
        private readonly QuestVoiceService _service;
        /// <summary>通过生产构造函数创建的页面模型。</summary>
        public QuestVoicePageViewModel Owner { get; }
        /// <summary>使用隔离路径和被动宿主建立空闲页面。</summary>
        public PassivePage(bool nullExtras = false)
        {
            var all = new AllConfig();
            if (nullExtras) JsonConvert.PopulateObject("{\"ExtraProviders\":null}", all.QuestVoiceConfig);
            all.QuestVoiceConfig.RoutingCatalogPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
            var factory = new QuestVoiceSessionFactory(all.QuestVoiceConfig, System.IO.Path.GetTempPath());
            _controller = new(factory);
            _service = new((GameRuntimeService)RuntimeHelpers.GetUninitializedObject(typeof(GameRuntimeService)),
                (TaskTriggerDispatcher)RuntimeHelpers.GetUninitializedObject(typeof(TaskTriggerDispatcher)),
                all, factory, _controller, NullLogger<QuestVoiceService>.Instance);
            Owner = new(all, _service, factory);
        }
        /// <summary>按生产资源所有权顺序释放服务与控制器。</summary>
        public void Dispose() { _service.Dispose(); _controller.Dispose(); }
    }
}