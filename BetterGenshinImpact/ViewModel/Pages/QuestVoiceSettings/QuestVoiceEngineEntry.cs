using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages.QuestVoiceSettings;

/// <summary>设置窗口左侧引擎列表的一项：引擎信息、该引擎的设置模型，以及是否为默认引擎。</summary>
public partial class QuestVoiceEngineEntry(TtsProviderInfo provider, object settings) : ObservableObject
{
    /// <summary>注册表中的引擎信息。</summary>
    public TtsProviderInfo Provider { get; } = provider;
    /// <summary>该引擎的设置模型，由窗口按类型选择模板显示。</summary>
    public object Settings { get; } = settings;
    /// <summary>是否为正式配音使用的默认引擎，列表中显示 ★。</summary>
    [ObservableProperty] private bool _isDefault;
}
