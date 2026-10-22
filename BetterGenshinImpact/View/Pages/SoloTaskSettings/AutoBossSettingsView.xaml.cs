using System.Windows.Controls;

namespace BetterGenshinImpact.View.Pages.SoloTaskSettings;

/// <summary>自动首领讨伐任务的专属设置卡片，绑定 AutoBossSettingsViewModel。</summary>
public partial class AutoBossSettingsView : UserControl
{
    /// <summary>建立独立界面，DataContext 由宿主按当前任务注入。</summary>
    public AutoBossSettingsView() => InitializeComponent();
}
