using System.Windows;
using System.Windows.Controls;
using BetterGenshinImpact.ViewModel.Pages;

namespace BetterGenshinImpact.View.Pages.QuestVoiceSettings;

/// <summary>原生 BetterGI 样式的剧情/预合成双视图，不另建独立应用或视频调试入口。</summary>
public partial class QuestVoiceStoryPanel : UserControl
{
    /// <summary>建立数据驱动视图，数据由设置窗口共享模型提供。</summary>
    public QuestVoiceStoryPanel() => InitializeComponent();

    /// <summary>视图显示后非阻塞加载目录，失败在页面状态文字中提示。</summary>
    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (DataContext is QuestVoiceStoryWorkspace workspace)
            _ = workspace.EnsureLoadedAsync();
    }
}
