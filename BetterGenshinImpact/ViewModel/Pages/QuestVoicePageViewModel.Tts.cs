using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>按能力投影配置；页面不创建网络请求或持有播放器。</summary>
public partial class QuestVoicePageViewModel
{
    /// <summary>注册表决定引擎下拉列表。</summary>
    public IReadOnlyList<TtsProviderInfo> Providers => TtsProviderRegistry.Providers;
    /// <summary>当前引擎；运行期间拒绝切换，下次启动冻结。</summary>
    public TtsProviderInfo? SelectedProvider
    {
        get => Providers.FirstOrDefault(x => x.Id == Config.ProviderId);
        set { if (IsConfigEditable && value is not null) Config.ProviderId = value.Id; }
    }
    /// <summary>当前引擎的可读提示。</summary>
    public string ProviderDescription => SelectedProvider?.Description ?? "未知引擎，请选择已注册的 TTS。";
    /// <summary>只为真正需要服务器的引擎显示连接设置。</summary>
    public bool SupportsServerConnection => SelectedProvider?.Capabilities.ServerConnection == true;
    /// <summary>角色权重和原映射属于需要资产的 Provider。</summary>
    public bool SupportsVoiceAssets => SelectedProvider?.Capabilities.VoiceAssets == true;
    /// <summary>只有实际增量播放实现才显示流式开关。</summary>
    public bool SupportsStreaming => SelectedProvider?.Capabilities.Streaming == true;
    /// <summary>预设音色引擎显示声音和语言列表。</summary>
    public bool SupportsPresetVoices => SelectedProvider?.Capabilities.PresetVoices == true;
    /// <summary>恢复按钮仅属于有后端排空语义的引擎。</summary>
    public bool RequiresBackendDrain => SelectedProvider?.Capabilities.RequiresDrain == true;
    /// <summary>没有任何专属高级参数时折叠整个高级配置卡片。</summary>
    public bool HasProviderSettings => SupportsVoiceAssets || SupportsStreaming || SupportsServerConnection;
    /// <summary>列表与内部 voice ID 分开显示。</summary>
    public ObservableCollection<TtsVoiceInfo> Voices { get; } = new();
    /// <summary>直接绑定条目，避免列表刷新后 SelectedValue 保留而 SelectedItem 为空。</summary>
    public TtsVoiceInfo? SelectedVoice
    {
        get => Voices.FirstOrDefault(x => x.Id == Config.HuoshanVoiceId);
        set { if (value is not null) SelectedVoiceId = value.Id; }
    }
    /// <summary>忽略列表切换导致的暂时空选择，防止 TwoWay 将保存的音色清空。</summary>
    public string SelectedVoiceId
    {
        get => Config.HuoshanVoiceId;
        set { if (IsConfigEditable && !string.IsNullOrWhiteSpace(value) && Voices.Any(x => x.Id == value)) Config.HuoshanVoiceId = value; }
    }
    /// <summary>本地和远程实例各自保存地址。</summary>
    public string ActiveServiceUrl { get => Config.UseRemoteService ? Config.RemoteServiceUrl : Config.ServiceUrl;
        set { if (Config.UseRemoteService) Config.RemoteServiceUrl = value; else Config.ServiceUrl = value; } }
    /// <summary>远程路径是服务器路径，不进行本地文件选择或上传。</summary>
    public string ActiveGptWeightsPath { get => Config.UseRemoteService ? Config.RemoteGptWeightsPath : Config.GptWeightsPath;
        set { if (Config.UseRemoteService) Config.RemoteGptWeightsPath = value; else Config.GptWeightsPath = value; } }
    /// <summary>当前实例的 SoVITS 路径。</summary>
    public string ActiveSovitsWeightsPath { get => Config.UseRemoteService ? Config.RemoteSovitsWeightsPath : Config.SovitsWeightsPath;
        set { if (Config.UseRemoteService) Config.RemoteSovitsWeightsPath = value; else Config.SovitsWeightsPath = value; } }
    /// <summary>当前实例的参考音频路径。</summary>
    public string ActiveReferenceAudioPath { get => Config.UseRemoteService ? Config.RemoteReferenceAudioPath : Config.ReferenceAudioPath;
        set { if (Config.UseRemoteService) Config.RemoteReferenceAudioPath = value; else Config.ReferenceAudioPath = value; } }
    /// <summary>当前实例参考音频的逐字原文。</summary>
    public string ActivePromptText { get => Config.UseRemoteService ? Config.RemotePromptText : Config.PromptText;
        set { if (Config.UseRemoteService) Config.RemotePromptText = value; else Config.PromptText = value; } }
    /// <summary>远程资源只接受服务器已有路径，不显示本机浏览按钮。</summary>
    public bool CanBrowseVoiceAssets => !Config.UseRemoteService;
    /// <summary>用户明确点击测试后才发送到当前引擎。</summary>
    [ObservableProperty] private string _testText = "你好，旅行者。欢迎来到提瓦特。";

    /// <summary>统一配置自动保存后同步所有能力和实例代理属性。</summary>
    private void OnTtsConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var name in new[] { nameof(SelectedProvider), nameof(ProviderDescription), nameof(SupportsServerConnection),
                     nameof(SupportsVoiceAssets), nameof(SupportsStreaming), nameof(SupportsPresetVoices), nameof(RequiresBackendDrain),
                     nameof(ActiveServiceUrl), nameof(ActiveGptWeightsPath), nameof(ActiveSovitsWeightsPath),
                     nameof(ActiveReferenceAudioPath), nameof(ActivePromptText), nameof(CanBrowseVoiceAssets),
                     nameof(HasProviderSettings), nameof(SelectedVoiceId), nameof(SelectedVoice) })
            OnPropertyChanged(name);
        if (e.PropertyName == nameof(QuestVoiceConfig.ProviderId)) _ = RefreshVoicesAsync();
    }

    /// <summary>读声线列表；忽略切换引擎后迟到的结果。</summary>
    [RelayCommand]
    private async Task RefreshVoicesAsync()
    {
        var id = Config.ProviderId;
        try
        {
            var settings = _factory.CreateTtsSettings(validateProvider: false);
            using var provider = TtsProviderRegistry.Create(id);
            if (!provider.Info.Capabilities.PresetVoices) return;
            var voices = await provider.GetVoicesAsync(settings, default);
            if (Config.ProviderId != id) return;
            if (!Voices.SequenceEqual(voices))
            {
                Voices.Clear();
                foreach (var voice in voices) Voices.Add(voice);
            }
            OnPropertyChanged(nameof(SelectedVoiceId));
            OnPropertyChanged(nameof(SelectedVoice));
        }
        catch (Exception exception) { CommandError = exception.Message; }
    }

    /// <summary>调用生产服务的互斥测试入口。</summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task TestSpeechAsync(CancellationToken token)
    {
        CommandError = "";
        try { await _service.TestSpeechAsync(TestText, token); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { CommandError = exception.Message; }
    }
    /// <summary>及时停止本地测试播放，后台收尾仍由服务观察。</summary>
    [RelayCommand]
    private void CancelTestSpeech() => _service.CancelTestSpeech();
}
