using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.QuestVoice;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;
using BetterGenshinImpact.Service;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>GPT 独立连接与旧默认声线设置；路由资源始终由共享目录编辑器管理。</summary>
public partial class GptSovitsSettingsViewModel : ObservableObject
{
    /// <summary>复用生产快照工厂，设置层不实现 HTTP 协议。</summary>
    private readonly QuestVoiceSessionFactory _factory;
    /// <summary>唯一的测试播放与取消所有者。</summary>
    private readonly QuestVoiceService _service;
    /// <summary>连接配置代次，迟到探测不得污染另一实例。</summary>
    private int _connectionGeneration;
    /// <summary>共享运行与兼容字段的模型。</summary>
    public QuestVoicePageViewModel Owner { get; }
    /// <summary>宿主原有配置，无数据迁移。</summary>
    public QuestVoiceConfig Config => Owner.Config;
    /// <summary>唯一 Profile 草稿及自动保存状态。</summary>
    public QuestVoiceManagementViewModel Management => Owner.Management;
    /// <summary>本地旧资源草稿。</summary>
    public GptSovitsDefaultVoiceDraft LocalDefaultVoice { get; private set; }
    /// <summary>服务器旧资源草稿。</summary>
    public GptSovitsDefaultVoiceDraft RemoteDefaultVoice { get; private set; }
    /// <summary>界面只显示当前实例，两个草稿独立保留。</summary>
    public GptSovitsDefaultVoiceDraft DefaultVoiceDraft => Config.UseRemoteService ? RemoteDefaultVoice : LocalDefaultVoice;
    /// <summary>连接状态仅由显式测试更新，不代表真实推理或后端空闲。</summary>
    [ObservableProperty] private string _connectionStatus = "尚未检测连接";
    /// <summary>URL 输入草稿，非法输入不会替换有效宿主配置。</summary>
    [ObservableProperty] private string _serviceUrl;
    /// <summary>当前输入的轻量校验结果。</summary>
    [ObservableProperty] private string _saveStatus = "有效设置自动保存 · 下次启动配音生效";
    /// <summary>测试文本不属于持久化业务配置。</summary>
    [ObservableProperty] private string _testText = "你好，旅行者。欢迎来到提瓦特。";
    /// <summary>绑定独立设置，不改变全局默认 Provider。</summary>
    public GptSovitsSettingsViewModel(QuestVoicePageViewModel owner, QuestVoiceSessionFactory factory, QuestVoiceService service)
    {
        Owner = owner; _factory = factory; _service = service;
        _serviceUrl = owner.ActiveServiceUrl;
        LocalDefaultVoice = new(Config, false, factory.ProgramDirectory);
        RemoteDefaultVoice = new(Config, true, factory.ProgramDirectory);
        owner.FlushGptSettings = () => LocalDefaultVoice.TryCommit() & RemoteDefaultVoice.TryCommit();
        Config.PropertyChanged += OnConfigChanged;
    }
    /// <summary>切换本远实例恢复各自 URL，废弃旧连接诊断。</summary>
    private void OnConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(QuestVoiceConfig.UseRemoteService) or nameof(QuestVoiceConfig.ServiceUrl) or nameof(QuestVoiceConfig.RemoteServiceUrl))
        {
            Interlocked.Increment(ref _connectionGeneration);
            if (ServiceUrl != Owner.ActiveServiceUrl && e.PropertyName == nameof(QuestVoiceConfig.UseRemoteService)) ServiceUrl = Owner.ActiveServiceUrl;
            OnPropertyChanged(nameof(DefaultVoiceDraft));
            ConnectionStatus = "连接设置已更改，请主动测试";
        }
    }
    /// <summary>延迟文本绑定后只提交合法 HTTP 地址，不在输入时联网。</summary>
    partial void OnServiceUrlChanged(string value)
    {
        Interlocked.Increment(ref _connectionGeneration);
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        { SaveStatus = "等待有效 HTTP/HTTPS URL（不含凭据、查询或片段）"; return; }
        Owner.ActiveServiceUrl = value.Trim();
        SaveStatus = "有效设置由 BetterGI 自动保存 · 下次启动配音生效";
    }
    /// <summary>为 GPT 明确构造默认声线，不借用当前全局 Provider。</summary>
    private RoutingVoice DefaultVoice()
    {
        var settings = _factory.CreateProviderSettings("gpt-sovits", false);
        var voice = settings.Voice;
        return new("configured", voice.Name, voice.GptWeightsPath, voice.SovitsWeightsPath, voice.ReferenceAudioPath, voice.PromptText,
            ProviderId: "gpt-sovits", InstanceId: settings.Provider!.InstanceId);
    }
    /// <summary>显式只读探测，不合成、切权或清除 Unknown。</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ProbeAsync(CancellationToken token)
    {
        var generation = Interlocked.Increment(ref _connectionGeneration);
        try
        {
            if (ServiceUrl.Trim() != Owner.ActiveServiceUrl.Trim()) { ConnectionStatus = "请先输入有效 URL"; return; }
            using var provider = TtsProviderRegistry.Create("gpt-sovits");
            var result = await provider.ProbeAsync(_factory.CreateVoiceSettings(DefaultVoice(), false), token);
            if (generation == _connectionGeneration) ConnectionStatus = result;
        }
        catch (OperationCanceledException) { }
        catch { if (generation == _connectionGeneration) ConnectionStatus = "连接失败，请检查服务、URL 和超时。"; }
    }
    /// <summary>旧默认声线也可试听，生产服务仍拥有唯一播放器和排空机制。</summary>
    [RelayCommand]
    private async Task PreviewDefaultAsync(CancellationToken token)
    {
        var generation = Interlocked.Increment(ref _connectionGeneration);
        if (!DefaultVoiceDraft.TryCommit()) { ConnectionStatus = "请先完成有效的默认声线资源"; return; }
        try { await _service.TestVoiceAsync(TestText, DefaultVoice(), token); }
        catch (OperationCanceledException) { }
        catch { if (generation == _connectionGeneration) ConnectionStatus = "试听失败，请检查当前实例资源与服务状态。"; }
    }
    /// <summary>明确撤销未完成的旧默认资源输入，不修改有效宿主配置。</summary>
    [RelayCommand]
    private void DiscardDefaultDraft()
    {
        if (Config.UseRemoteService) { RemoteDefaultVoice.Dispose(); RemoteDefaultVoice = new(Config, true, _factory.ProgramDirectory); }
        else { LocalDefaultVoice.Dispose(); LocalDefaultVoice = new(Config, false, _factory.ProgramDirectory); }
        OnPropertyChanged(nameof(DefaultVoiceDraft));
    }
    /// <summary>复用原测试取消入口。</summary>
    [RelayCommand] private void CancelPreview() => _service.CancelTestSpeech();
}
