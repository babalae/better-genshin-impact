using System.Windows.Controls;

namespace BetterGenshinImpact.View.Pages;

/// <summary>独立路由管理子视图，沿用父页面传入的 Management 草稿模型。</summary>
public partial class QuestVoiceManagementView : UserControl
{
    /// <summary>只建立视图；不读取文件、不创建服务、不操作游戏。</summary>
    public QuestVoiceManagementView() => InitializeComponent();
}
