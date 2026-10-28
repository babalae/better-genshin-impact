using System.Windows.Controls;

namespace BetterGenshinImpact.View.Pages.SoloTaskSettings;

/// <summary>体力计划的专属设置卡片，绑定 ResinPlanSettingsViewModel。</summary>
public partial class ResinPlanSettingsView : UserControl
{
    /// <summary>建立独立界面，DataContext 由宿主按当前任务注入。</summary>
    public ResinPlanSettingsView() => InitializeComponent();
}
