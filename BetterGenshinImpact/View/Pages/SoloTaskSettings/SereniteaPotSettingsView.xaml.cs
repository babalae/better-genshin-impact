using System.Windows.Controls;

namespace BetterGenshinImpact.View.Pages.SoloTaskSettings;

/// <summary>领取尘歌壶奖励任务的专属设置卡片，绑定 SereniteaPotSettingsViewModel。</summary>
public partial class SereniteaPotSettingsView : UserControl
{
    /// <summary>建立独立界面，DataContext 由宿主按当前任务注入。</summary>
    public SereniteaPotSettingsView() => InitializeComponent();
}
