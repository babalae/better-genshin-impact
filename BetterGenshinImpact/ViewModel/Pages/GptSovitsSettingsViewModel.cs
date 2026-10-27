using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.QuestVoice;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>GPT 独立连接设置；声音资源仅由共享 Profile 声线库管理。</summary>
public partial class GptSovitsSettingsViewModel : ObservableObject
{
    /// <summary>复用生产快照工厂，设置层不实现 HTTP 协议。</summary>
    private readonly QuestVoiceSessionFactory _factory;
    /// <summary>连接配置代次，迟到探测不得污染另一实例。</summary>
    private int _connectionGeneration;
    /// <summary>共享运行与兼容字段的模型。</summary>
    public QuestVoicePageViewModel Owner { get; }
    /// <summary>宿主原有配置，无数据迁移。</summary>
    public QuestVoiceConfig Config => Owner.Config;
    /// <summary>唯一 Profile 草稿及自动保存状态。</summary>
    public QuestVoiceManagementViewModel Management => Owner.Management;
    /// <summary>连接状态仅由显式测试更新，不代表真实推理或后端空闲。</summary>
    [ObservableProperty] private string _connectionStatus = "尚未检测连接";
    /// <summary>URL 输入草稿，非法输入不会替换有效宿主配置。</summary>
    [ObservableProperty] private string _serviceUrl;
    /// <summary>当前输入的轻量校验结果。</summary>
    [ObservableProperty] private string _saveStatus = "有效设置自动保存 · 下次启动配音生效";
    /// <summary>绑定独立设置，不改变全局默认 Provider。</summary>
    public GptSovitsSettingsViewModel(QuestVoicePageViewModel owner, QuestVoiceSessionFactory factory)
    {
        Owner = owner; _factory = factory;
        _serviceUrl = owner.ActiveServiceUrl;
        Config.PropertyChanged += OnConfigChanged;
    }
    /// <summary>切换本远实例恢复各自 URL，废弃旧连接诊断。</summary>
    private void OnConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(QuestVoiceConfig.UseRemoteService) or nameof(QuestVoiceConfig.ServiceUrl) or nameof(QuestVoiceConfig.RemoteServiceUrl))
        {
            Interlocked.Increment(ref _connectionGeneration);
            if (ServiceUrl != Owner.ActiveServiceUrl && e.PropertyName == nameof(QuestVoiceConfig.UseRemoteService)) ServiceUrl = Owner.ActiveServiceUrl;
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
    /// <summary>显式只读探测，不合成、切权或清除 Unknown。</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ProbeAsync(CancellationToken token)
    {
        var generation = Interlocked.Increment(ref _connectionGeneration);
        try
        {
            if (ServiceUrl.Trim() != Owner.ActiveServiceUrl.Trim()) { ConnectionStatus = "请先输入有效 URL"; return; }
            using var provider = TtsProviderRegistry.Create("gpt-sovits");
            var result = await provider.ProbeAsync(_factory.CreateProviderSettings("gpt-sovits", false), token);
            if (generation == _connectionGeneration) ConnectionStatus = result;
        }
        catch (OperationCanceledException) { }
        catch { if (generation == _connectionGeneration) ConnectionStatus = "连接失败，请检查服务、URL 和超时。"; }
    }
}
