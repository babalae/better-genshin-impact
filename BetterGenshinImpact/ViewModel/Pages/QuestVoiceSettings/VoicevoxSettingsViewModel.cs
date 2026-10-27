using BetterGenshinImpact.GameTask.QuestVoice;
using BetterGenshinImpact.Service;

namespace BetterGenshinImpact.ViewModel.Pages.QuestVoiceSettings;

/// <summary>VOICEVOX 独立实例、style ID 与速度音高设置。</summary>
public sealed partial class VoicevoxSettingsViewModel : EngineSettingsViewModel
{
    /// <summary>沿用宿主配置与生产服务，不创建其他引擎的配置副本。</summary>
    public VoicevoxSettingsViewModel(QuestVoicePageViewModel host, QuestVoiceSessionFactory factory, QuestVoiceService service)
        : base("voicevox", host, factory, service) { }

}