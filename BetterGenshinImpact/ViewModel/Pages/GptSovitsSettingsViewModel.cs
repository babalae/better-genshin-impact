using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.QuestVoice;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.ViewModel.Pages.QuestVoiceSettings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>GPT-SoVITS 的服务连接与流式设置；声音资源由共享声线库管理。</summary>
public partial class GptSovitsSettingsViewModel : ObservableObject
{
    /// <summary>复用生产快照工厂，设置层不实现 HTTP 协议。</summary>
    private readonly QuestVoiceSessionFactory _factory;
    /// <summary>连接测试经服务层发出，ViewModel 不直接访问引擎。</summary>
    private readonly QuestVoiceService _service;
    /// <summary>连接配置代次，迟到探测不得污染另一实例。</summary>
    private int _connectionGeneration;
    /// <summary>设置窗口的宿主模型。</summary>
    public QuestVoiceSettingsViewModel Owner { get; }
    /// <summary>宿主原有配置，无数据迁移。</summary>
    public QuestVoiceConfig Config => Owner.Config;
    /// <summary>唯一 Profile 草稿及自动保存状态。</summary>
    public QuestVoiceManagementViewModel Management => Owner.Management;
    /// <summary>连接状态仅由显式测试更新，不代表真实推理或后端空闲。</summary>
    [ObservableProperty] private string _connectionStatus = "尚未检测连接";
    /// <summary>URL 输入草稿，非法输入不会替换有效宿主配置。</summary>
    [ObservableProperty] private string _serviceUrl;
    /// <summary>地址校验提示；为空表示当前输入有效。</summary>
    [ObservableProperty] private string _urlValidationMessage = "";

    /// <summary>绑定独立设置，不改变全局默认引擎。</summary>
    public GptSovitsSettingsViewModel(QuestVoiceSettingsViewModel owner, QuestVoiceSessionFactory factory, QuestVoiceService service)
    {
        Owner = owner; _factory = factory; _service = service;
        _serviceUrl = ActiveServiceUrl;
        Config.PropertyChanged += OnConfigChanged;
    }

    /// <summary>本地和远程实例各自保存地址。</summary>
    private string ActiveServiceUrl
    {
        get => Config.UseRemoteService ? Config.RemoteServiceUrl : Config.ServiceUrl;
        set { if (Config.UseRemoteService) Config.RemoteServiceUrl = value; else Config.ServiceUrl = value; }
    }

    /// <summary>进入 GPT-SoVITS 时加载声线库，并在没有选中 GPT 声线时选中第一条。</summary>
    public async Task SelectFirstProfileAsync()
    {
        await Management.EnsureLoadedAsync();
        if (Management.SelectedProfile?.ProviderId != "gpt-sovits")
            Management.SelectedProfile = Management.GptProfiles.FirstOrDefault();
    }

    /// <summary>切换本远实例恢复各自 URL，废弃旧连接诊断。</summary>
    private void OnConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(QuestVoiceConfig.UseRemoteService) or nameof(QuestVoiceConfig.ServiceUrl) or nameof(QuestVoiceConfig.RemoteServiceUrl))
        {
            Interlocked.Increment(ref _connectionGeneration);
            if (ServiceUrl != ActiveServiceUrl && e.PropertyName == nameof(QuestVoiceConfig.UseRemoteService)) ServiceUrl = ActiveServiceUrl;
            ConnectionStatus = "连接设置已更改，请主动测试";
        }
    }

    /// <summary>只提交合法 HTTP 地址，不在输入时联网。</summary>
    partial void OnServiceUrlChanged(string value)
    {
        Interlocked.Increment(ref _connectionGeneration);
        if (!EngineSettingsViewModel.IsValidUrl(value))
        { UrlValidationMessage = "请输入有效的 HTTP/HTTPS 地址（不含账号、查询参数或片段）"; return; }
        ActiveServiceUrl = value.Trim();
        UrlValidationMessage = "";
    }

    /// <summary>显式只读探测，不合成、切权或清除后端未知状态。</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ProbeAsync(CancellationToken token)
    {
        var generation = Interlocked.Increment(ref _connectionGeneration);
        try
        {
            if (ServiceUrl.Trim() != ActiveServiceUrl.Trim()) { ConnectionStatus = "请先输入有效 URL"; return; }
            var result = await _service.ProbeAsync(_factory.CreateProviderSettings("gpt-sovits", false), token);
            if (generation == _connectionGeneration) ConnectionStatus = result;
        }
        catch (OperationCanceledException) { }
        catch { if (generation == _connectionGeneration) ConnectionStatus = "连接失败，请检查服务、URL 和超时。"; }
    }
}
