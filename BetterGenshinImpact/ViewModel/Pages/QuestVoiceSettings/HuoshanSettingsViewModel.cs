using BetterGenshinImpact.GameTask.QuestVoice;
using BetterGenshinImpact.Service;

namespace BetterGenshinImpact.ViewModel.Pages.QuestVoiceSettings;

/// <summary>火山翻译只使用生产 Provider 提供的三种中文预设声音。</summary>
public sealed partial class HuoshanSettingsViewModel : EngineSettingsViewModel
{
    /// <summary>沿用宿主配置与生产服务，不创建其他引擎的配置副本。</summary>
    public HuoshanSettingsViewModel(QuestVoicePageViewModel host, QuestVoiceSessionFactory factory, QuestVoiceService service)
        : base("huoshan", host, factory, service) { }

}