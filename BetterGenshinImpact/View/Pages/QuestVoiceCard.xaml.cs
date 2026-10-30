using System.Windows.Controls;

namespace BetterGenshinImpact.View.Pages;

/// <summary>实时触发页的“自动配音”卡片；DataContext 为 QuestVoiceCardViewModel，由所在页面传入。</summary>
public partial class QuestVoiceCard : UserControl
{
    /// <summary>只建立视图，不创建或中断配音会话。</summary>
    public QuestVoiceCard() => InitializeComponent();
}