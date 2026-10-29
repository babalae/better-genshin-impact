using System.Windows.Controls;
using BetterGenshinImpact.ViewModel.Pages;

namespace BetterGenshinImpact.View.Pages;

/// <summary>原生自动配音页面，沿用 MusicPage 的 DataContext 约定。</summary>
public partial class QuestVoicePage : UserControl
{
    /// <summary>依赖注入的页面模型。</summary>
    public QuestVoicePageViewModel ViewModel { get; }
    /// <summary>视图只负责建立绑定，不创建配音会话。</summary>
    public QuestVoicePage(QuestVoicePageViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
