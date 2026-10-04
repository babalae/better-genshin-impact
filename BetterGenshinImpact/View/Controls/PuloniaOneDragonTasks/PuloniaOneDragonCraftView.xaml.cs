using System.Windows.Controls;

namespace BetterGenshinImpact.View.Controls.PuloniaOneDragonTasks;

/// <summary>合成树脂任务的专属设置卡片，绑定 PuloniaOneDragonCraftSettingsViewModel。</summary>
public partial class PuloniaOneDragonCraftView : UserControl
{
    /// <summary>建立独立界面，DataContext 由宿主按当前任务注入。</summary>
    public PuloniaOneDragonCraftView() => InitializeComponent();
}
