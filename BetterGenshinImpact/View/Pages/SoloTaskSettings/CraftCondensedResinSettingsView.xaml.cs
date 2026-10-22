using System.Windows.Controls;

namespace BetterGenshinImpact.View.Pages.SoloTaskSettings;

/// <summary>合成浓缩树脂任务的专属设置卡片，绑定 CraftCondensedResinSettingsViewModel。</summary>
public partial class CraftCondensedResinSettingsView : UserControl
{
    /// <summary>建立独立界面，DataContext 由宿主按当前任务注入。</summary>
    public CraftCondensedResinSettingsView() => InitializeComponent();
}
