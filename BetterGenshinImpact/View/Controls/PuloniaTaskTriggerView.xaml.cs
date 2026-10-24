using System.Windows.Controls;

namespace BetterGenshinImpact.View.Controls;

/// <summary>
/// 触发配置视图，任务计划页与一条龙触发设置窗口共用；所有交互由 PuloniaPlanTriggersViewModel 及独立草稿命令承担。
/// </summary>
public partial class PuloniaTaskTriggerView : UserControl
{
    /// <summary>初始化可滚动的配置布局。</summary>
    public PuloniaTaskTriggerView() => InitializeComponent();
}
