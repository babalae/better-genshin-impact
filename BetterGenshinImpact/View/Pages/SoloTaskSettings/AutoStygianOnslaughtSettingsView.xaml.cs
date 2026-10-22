using System.Windows.Controls;

namespace BetterGenshinImpact.View.Pages.SoloTaskSettings;

/// <summary>自动幽境危战任务的专属设置卡片，绑定 AutoStygianOnslaughtSettingsViewModel。</summary>
public partial class AutoStygianOnslaughtSettingsView : UserControl
{
    /// <summary>建立独立界面，DataContext 由宿主按当前任务注入。</summary>
    public AutoStygianOnslaughtSettingsView() => InitializeComponent();
}
