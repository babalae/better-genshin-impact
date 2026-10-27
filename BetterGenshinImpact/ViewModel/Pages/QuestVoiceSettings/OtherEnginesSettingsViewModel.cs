using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.GameTask.QuestVoice;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.Service;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages.QuestVoiceSettings;

/// <summary>只管理六种其他引擎的页面选择，不改变运行页面的默认 ProviderId。</summary>
public sealed partial class OtherEnginesSettingsViewModel : ObservableObject
{
    /// <summary>各引擎独立保存交互状态及地址草稿。</summary>
    private readonly IReadOnlyList<EngineSettingsViewModel> _engines;
    /// <summary>页面自己的引擎选择，与全局默认值解耦。</summary>
    [ObservableProperty] private TtsProviderInfo? _selectedEngine;
    /// <summary>当前实际展示的独立设置模型。</summary>
    [ObservableProperty] private EngineSettingsViewModel? _currentSettings;
    /// <summary>仅包含已实现的六种其他引擎。</summary>
    public IReadOnlyList<TtsProviderInfo> Engines { get; }

    /// <summary>页面单例拥有六个小模型，统一复用宿主配置、工厂和播放服务。</summary>
    public OtherEnginesSettingsViewModel(QuestVoicePageViewModel host, QuestVoiceSessionFactory factory, QuestVoiceService service)
    {
        _engines = new EngineSettingsViewModel[]
        {
            new HuoshanSettingsViewModel(host, factory, service),
            new WindowsSapiSettingsViewModel(host, factory, service),
            new VoicevoxSettingsViewModel(host, factory, service),
            new VitsSettingsViewModel(host, factory, service),
            new OpenAiSpeechSettingsViewModel(host, factory, service),
            new GoogleTtsSettingsViewModel(host, factory, service)
        };
        Engines = _engines.Select(x => x.Provider).ToArray();
        SelectedEngine = Engines.FirstOrDefault(x => x.Id == host.Config.ProviderId) ?? Engines[0];
    }

    /// <summary>先使旧模型请求失效，再发布新模型与对应模板。</summary>
    partial void OnSelectedEngineChanged(TtsProviderInfo? value)
    {
        CurrentSettings?.SetActive(false);
        CurrentSettings = _engines.FirstOrDefault(x => x.ProviderId == value?.Id);
        CurrentSettings?.SetActive(true);
    }
}