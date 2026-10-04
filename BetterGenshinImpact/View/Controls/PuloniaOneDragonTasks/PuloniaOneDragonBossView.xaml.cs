using System.Windows.Controls;

namespace BetterGenshinImpact.View.Controls.PuloniaOneDragonTasks;

/// <summary>自动首领讨伐任务的专属设置卡片，绑定 PuloniaOneDragonBossSettingsViewModel。</summary>
public partial class PuloniaOneDragonBossView : UserControl
{
    /// <summary>建立独立界面，DataContext 由宿主按当前任务注入。</summary>
    public PuloniaOneDragonBossView() => InitializeComponent();
}
