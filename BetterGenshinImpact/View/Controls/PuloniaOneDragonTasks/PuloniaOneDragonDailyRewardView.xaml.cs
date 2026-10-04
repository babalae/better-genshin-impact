using System.Windows.Controls;

namespace BetterGenshinImpact.View.Controls.PuloniaOneDragonTasks;

/// <summary>领取每日奖励任务的专属设置卡片，绑定 PuloniaOneDragonDailyRewardSettingsViewModel。</summary>
public partial class PuloniaOneDragonDailyRewardView : UserControl
{
    /// <summary>建立独立界面，DataContext 由宿主按当前任务注入。</summary>
    public PuloniaOneDragonDailyRewardView() => InitializeComponent();
}
