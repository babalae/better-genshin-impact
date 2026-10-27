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
    /// <summary>已订阅的嵌套引擎配置，字典替换后重新挂接。</summary>
    private readonly HashSet<QuestVoiceEngineConfig> _observedEngineConfigs = new();
    /// <summary>每次选择、实例变更或刷新递增，防止旧请求覆盖当前列表。</summary>
    private int _voiceRequestGeneration;
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
    public bool RequiresBackendDrain => SelectedProvider?.Capabilities.RequiresDrain == true || Status.BackendUnknown;
    /// <summary>没有任何专属高级参数时折叠整个高级配置卡片。</summary>
    public bool HasProviderSettings => SupportsVoiceAssets || SupportsStreaming || SupportsServerConnection || SelectedEngineConfig is not null;
    /// <summary>新增引擎独立配置；旧 GPT 和火山保持原字段。</summary>
    public QuestVoiceEngineConfig? SelectedEngineConfig => _factory.ExtraConfig(Config.ProviderId);
    /// <summary>新增引擎服务连接设置，不显示 GPT 资产提示。</summary>
    public bool SupportsExtraServerConnection => SelectedEngineConfig is not null && SupportsServerConnection;
    /// <summary>按能力显示速度参数。</summary>
    public bool SupportsRate => SelectedProvider?.Capabilities.Rate == true;
    /// <summary>Google 只接受两个离散速度。</summary>
    public bool UsesDiscreteRate => Config.ProviderId == "google-translate";
    /// <summary>其他支持速度的引擎使用连续范围。</summary>
    public bool SupportsContinuousRate => SupportsRate && !UsesDiscreteRate;
    /// <summary>合法的 Google 速度选项。</summary>
    public IReadOnlyList<double> DiscreteRates { get; } = new[] { .5, 1d };
    /// <summary>OpenAI 兼容服务允许更宽速度范围。</summary>
    public double MinimumRate => Config.ProviderId == "openai-speech" ? .25 : .5;
    /// <summary>其他引擎均限制在两倍速度内。</summary>
    public double MaximumRate => Config.ProviderId == "openai-speech" ? 4 : 2;
    /// <summary>速度的允许范围提示。</summary>
    public string RateHint => UsesDiscreteRate ? "速度仅支持 0.5 或 1。" : $"速度倍率：{MinimumRate}～{MaximumRate}，1 为默认。";
    /// <summary>按能力显示音高参数。</summary>
    public bool SupportsPitch => SelectedProvider?.Capabilities.Pitch == true;
    /// <summary>按能力显示语言参数。</summary>
    public bool SupportsLanguage => SelectedProvider?.Capabilities.Language == true;
    /// <summary>按能力显示模型参数。</summary>
    public bool SupportsModel => SelectedProvider?.Capabilities.Model == true;
    /// <summary>按能力显示密钥输入。</summary>
    public bool SupportsApiKey => SelectedProvider?.Capabilities.ApiKey == true;
    /// <summary>按能力显示源音频格式。</summary>
    public bool SupportsAudioFormat => SelectedProvider?.Capabilities.AudioFormat == true;
    /// <summary>OpenAI 兼容语音允许的格式。</summary>
    public IReadOnlyList<string> AudioFormats { get; } = new[] { "wav", "mp3", "pcm" };
    /// <summary>新增引擎当前本地或远程服务地址。</summary>
    public string ActiveExtraServiceUrl
    {
        get => SelectedEngineConfig is { } extra ? extra.UseRemote ? extra.RemoteUrl : extra.LocalUrl : "";
        set
        {
            if (!IsConfigEditable || SelectedEngineConfig is not { } extra) return;
            if (extra.UseRemote) extra.RemoteUrl = value; else extra.LocalUrl = value;
        }
    }
    /// <summary>列表与内部 voice ID 分开显示。</summary>
    public ObservableCollection<TtsVoiceInfo> Voices { get; } = new();
    /// <summary>直接绑定条目，避免列表刷新后 SelectedValue 保留而 SelectedItem 为空。</summary>
    public TtsVoiceInfo? SelectedVoice
    {
        get => Voices.FirstOrDefault(x => x.Id == SelectedVoiceId);
        set { if (value is not null) SelectedVoiceId = value.Id; }
    }
    /// <summary>忽略列表切换导致的暂时空选择，防止 TwoWay 将保存的音色清空。</summary>
    public string SelectedVoiceId
    {
        get => SelectedEngineConfig?.VoiceId ?? Config.HuoshanVoiceId;
        set
        {
            if (!IsConfigEditable || value is null) return;
            if (SelectedEngineConfig is { } extra) extra.VoiceId = value;
            else if (Config.ProviderId == "huoshan" && !string.IsNullOrWhiteSpace(value)) Config.HuoshanVoiceId = value;
        }
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
        ObserveEngineConfigs();
        NotifyTtsProperties();
        if (e.PropertyName == nameof(QuestVoiceConfig.ProviderId)) RefreshVoicesForSelection();
    }

    /// <summary>字典替换或补齐后维护嵌套属性订阅。</summary>
    private void ObserveEngineConfigs()
    {
        // 旧JSON允许显式null；借助工厂补齐整个独立配置字典，不要求默认引擎也是新增引擎。
        if (Config.ExtraProviders is null) _factory.ExtraConfig("windows-sapi");
        var configurations = Config.ExtraProviders!;
        var current = configurations.Values.Where(x => x is not null).ToHashSet();
        foreach (var item in _observedEngineConfigs.Except(current).ToArray())
        {
            item.PropertyChanged -= OnEngineConfigChanged;
            _observedEngineConfigs.Remove(item);
        }
        foreach (var item in current.Except(_observedEngineConfigs))
        {
            item.PropertyChanged += OnEngineConfigChanged;
            _observedEngineConfigs.Add(item);
        }
    }

    /// <summary>嵌套输入同步选择和地址代理；连接变更只使旧列表失效，不联网。</summary>
    private void OnEngineConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        // OpenAI模型只影响本地建议列表，更新时仍保留用户手输声音。
        if (sender == SelectedEngineConfig && Config.ProviderId == "openai-speech"
            && e.PropertyName == nameof(QuestVoiceEngineConfig.Model)) RefreshVoicesForSelection();
        if (sender == SelectedEngineConfig && e.PropertyName is nameof(QuestVoiceEngineConfig.LocalUrl)
            or nameof(QuestVoiceEngineConfig.RemoteUrl) or nameof(QuestVoiceEngineConfig.UseRemote))
        {
            Interlocked.Increment(ref _voiceRequestGeneration);
            Voices.Clear();
        }
        NotifyTtsProperties();
    }

    /// <summary>统一通知能力和嵌套代理，保持单列页面绑定简单。</summary>
    private void NotifyTtsProperties()
    {
        foreach (var name in new[] { nameof(SelectedProvider), nameof(ProviderDescription), nameof(SupportsServerConnection),
                     nameof(SupportsVoiceAssets), nameof(SupportsStreaming), nameof(SupportsPresetVoices), nameof(RequiresBackendDrain),
                     nameof(ActiveServiceUrl), nameof(ActiveGptWeightsPath), nameof(ActiveSovitsWeightsPath),
                     nameof(ActiveReferenceAudioPath), nameof(ActivePromptText), nameof(CanBrowseVoiceAssets),
                     nameof(HasProviderSettings), nameof(SelectedVoiceId), nameof(SelectedVoice), nameof(SelectedEngineConfig),
                     nameof(ActiveExtraServiceUrl), nameof(SupportsExtraServerConnection), nameof(SupportsRate),
                     nameof(UsesDiscreteRate), nameof(SupportsContinuousRate), nameof(MinimumRate), nameof(MaximumRate), nameof(RateHint),
                     nameof(SupportsPitch), nameof(SupportsLanguage), nameof(SupportsModel), nameof(SupportsApiKey), nameof(SupportsAudioFormat) })
            OnPropertyChanged(name);
    }

    /// <summary>切换仅允许本机或静态声音列表自动枚举，服务发现必须用户点击。</summary>
    private void RefreshVoicesForSelection()
    {
        Interlocked.Increment(ref _voiceRequestGeneration);
        Voices.Clear();
        if (CanEnumerateVoicesOffline(Config.ProviderId)) _ = RefreshVoicesAsync();
    }

    /// <summary>这些引擎枚举声音时不需要网络。</summary>
    internal static bool CanEnumerateVoicesOffline(string id) => id is "windows-sapi" or "huoshan" or "google-translate" or "openai-speech";

    /// <summary>读取声音列表；请求代次、引擎与实例地址全部一致才发布结果和异常。</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task RefreshVoicesAsync()
    {
        var id = Config.ProviderId;
        var generation = Interlocked.Increment(ref _voiceRequestGeneration);
        var instance = SelectedEngineConfig?.UseRemote;
        var address = ActiveExtraServiceUrl;
        // 同一Provider和URL往返仍依靠代次拒绝旧请求。
        bool IsCurrent() => generation == Volatile.Read(ref _voiceRequestGeneration) && Config.ProviderId == id
            && SelectedEngineConfig?.UseRemote == instance && ActiveExtraServiceUrl == address;
        try
        {
            var settings = _factory.CreateTtsSettings(validateProvider: false);
            using var provider = TtsProviderRegistry.Create(id);
            if (!provider.Info.Capabilities.PresetVoices) return;
            var voices = await provider.GetVoicesAsync(settings, default);
            if (!IsCurrent()) return;
            if (!Voices.SequenceEqual(voices))
            {
                Voices.Clear();
                foreach (var voice in voices) Voices.Add(voice);
            }
            OnPropertyChanged(nameof(SelectedVoiceId));
            OnPropertyChanged(nameof(SelectedVoice));
        }
        catch (Exception exception) { if (IsCurrent()) CommandError = exception.Message; }
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
