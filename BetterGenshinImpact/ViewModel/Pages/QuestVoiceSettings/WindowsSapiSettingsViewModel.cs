using BetterGenshinImpact.GameTask.QuestVoice;
using BetterGenshinImpact.Service;

namespace BetterGenshinImpact.ViewModel.Pages.QuestVoiceSettings;

/// <summary>Windows SAPI 独立系统声音与速度设置。</summary>
public sealed partial class WindowsSapiSettingsViewModel : EngineSettingsViewModel
{
    /// <summary>沿用宿主配置与生产服务，不创建其他引擎的配置副本。</summary>
    public WindowsSapiSettingsViewModel(QuestVoicePageViewModel host, QuestVoiceSessionFactory factory, QuestVoiceService service)
        : base("windows-sapi", host, factory, service) { }

}