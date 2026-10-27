using System.Collections.Generic;
using BetterGenshinImpact.GameTask.QuestVoice;
using BetterGenshinImpact.Service;

namespace BetterGenshinImpact.ViewModel.Pages.QuestVoiceSettings;

/// <summary>VITS 与 BERT-VITS2 复合声音 ID 设置。</summary>
public sealed partial class VitsSettingsViewModel : EngineSettingsViewModel
{
    /// <summary>沿用宿主配置与生产服务，不创建其他引擎的配置副本。</summary>
    public VitsSettingsViewModel(QuestVoicePageViewModel host, QuestVoiceSessionFactory factory, QuestVoiceService service)
        : base("vits-simple-api", host, factory, service) { }
    /// <summary>当前生产适配器支持的语言，不推测其他模型的参数。</summary>
    public IReadOnlyList<string> Languages { get; } = new[] { "auto", "zh", "ja", "en" };
}
