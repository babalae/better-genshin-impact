using System.Collections.Generic;
using BetterGenshinImpact.GameTask.QuestVoice;
using BetterGenshinImpact.Service;

namespace BetterGenshinImpact.ViewModel.Pages.QuestVoiceSettings;

/// <summary>OpenAI 兼容语音服务独立连接及请求参数。</summary>
public sealed partial class OpenAiSpeechSettingsViewModel : EngineSettingsViewModel
{
    /// <summary>沿用宿主配置与生产服务，不创建其他引擎的配置副本。</summary>
    public OpenAiSpeechSettingsViewModel(QuestVoicePageViewModel host, QuestVoiceSessionFactory factory, QuestVoiceService service)
        : base("openai-speech", host, factory, service) { }
    /// <summary>当前播放器具备解码能力的格式。</summary>
    public IReadOnlyList<string> AudioFormats { get; } = new[] { "mp3", "wav", "pcm" };
}
