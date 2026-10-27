using System.Windows.Controls;
using BetterGenshinImpact.ViewModel.Pages;

namespace BetterGenshinImpact.View.Pages;

/// <summary>自动配音「配置与映射」子页面，与「运行与日志」共用单例 ViewModel，切换页面不重建会话。</summary>
public partial class QuestVoiceSettingsPage : UserControl
{
    /// <summary>依赖注入的页面模型。</summary>
    public QuestVoicePageViewModel ViewModel { get; }

    /// <summary>视图只负责建立绑定，不创建或中断配音会话。</summary>
    public QuestVoiceSettingsPage(QuestVoicePageViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.Management.EnsureLoadedAsync();
    }
}
