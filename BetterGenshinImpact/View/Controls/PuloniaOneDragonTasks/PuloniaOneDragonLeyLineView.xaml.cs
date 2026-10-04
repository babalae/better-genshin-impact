using System.Windows.Controls;

namespace BetterGenshinImpact.View.Controls.PuloniaOneDragonTasks;

/// <summary>自动地脉花任务的专属设置卡片，绑定 PuloniaOneDragonLeyLineSettingsViewModel。</summary>
public partial class PuloniaOneDragonLeyLineView : UserControl
{
    /// <summary>建立独立界面，DataContext 由宿主按当前任务注入。</summary>
    public PuloniaOneDragonLeyLineView() => InitializeComponent();
}
