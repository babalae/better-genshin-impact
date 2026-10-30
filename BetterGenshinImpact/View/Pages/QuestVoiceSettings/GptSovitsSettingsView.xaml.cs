using System.Windows.Controls;

namespace BetterGenshinImpact.View.Pages.QuestVoiceSettings;

/// <summary>GPT-SoVITS 的服务、流式与声线设置；DataContext 为 GptSovitsSettingsViewModel。</summary>
public partial class GptSovitsSettingsView : UserControl
{
    /// <summary>只建立视图，不创建会话或音频设备。</summary>
    public GptSovitsSettingsView() => InitializeComponent();
}
