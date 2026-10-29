using System.Collections.Generic;
using BetterGenshinImpact.GameTask.QuestVoice;
using BetterGenshinImpact.Service;

namespace BetterGenshinImpact.ViewModel.Pages.QuestVoiceSettings;

/// <summary>Google Translate TTS 语言及离散速度设置。</summary>
public sealed partial class GoogleTtsSettingsViewModel : EngineSettingsViewModel
{
    /// <summary>沿用宿主配置与生产服务，不创建其他引擎的配置副本。</summary>
    public GoogleTtsSettingsViewModel(QuestVoicePageViewModel host, QuestVoiceSessionFactory factory, QuestVoiceService service)
        : base("google-translate", host, factory, service) { }
    /// <summary>上游协议实际支持的慢速及正常速度。</summary>
    public IReadOnlyList<double> Rates { get; } = new[] { .5, 1d };
}
