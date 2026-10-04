using System.Windows.Controls;

namespace BetterGenshinImpact.View.Controls.PuloniaOneDragonTasks;

/// <summary>自动秘境任务的专属设置卡片，绑定 PuloniaOneDragonDomainSettingsViewModel。</summary>
public partial class PuloniaOneDragonDomainView : UserControl
{
    /// <summary>建立独立界面，DataContext 由宿主按当前任务注入。</summary>
    public PuloniaOneDragonDomainView() => InitializeComponent();
}
