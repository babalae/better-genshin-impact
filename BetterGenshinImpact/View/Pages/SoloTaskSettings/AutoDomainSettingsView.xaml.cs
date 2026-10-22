using System.Windows.Controls;

namespace BetterGenshinImpact.View.Pages.SoloTaskSettings;

/// <summary>自动秘境任务的专属设置卡片，绑定 AutoDomainSettingsViewModel。</summary>
public partial class AutoDomainSettingsView : UserControl
{
    /// <summary>建立独立界面，DataContext 由宿主按当前任务注入。</summary>
    public AutoDomainSettingsView() => InitializeComponent();
}
