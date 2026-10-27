using System.Linq;
using System.Windows.Controls;
using BetterGenshinImpact.ViewModel.Pages;

namespace BetterGenshinImpact.View.Pages;

/// <summary>GPT 唯一主要编辑页面，不创建会话或音频设备。</summary>
public partial class QuestVoiceGptSettingsPage : UserControl
{
    /// <summary>应用级独立设置模型。</summary>
    public GptSovitsSettingsViewModel ViewModel { get; }
    /// <summary>页面只呈现共享目录，切页不会丢弃输入。</summary>
    public QuestVoiceGptSettingsPage(GptSovitsSettingsViewModel viewModel)
    {
        ViewModel = viewModel; DataContext = this; InitializeComponent();
        Loaded += async (_, _) =>
        {
            await viewModel.Management.EnsureLoadedAsync();
            if (viewModel.Management.SelectedProfile?.ProviderId != "gpt-sovits")
                viewModel.Management.SelectedProfile = viewModel.Management.GptProfiles.FirstOrDefault();
        };
    }
}
