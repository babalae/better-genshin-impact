using System.Windows.Controls;

namespace BetterGenshinImpact.View.Controls;

/// <summary>触发配置视图，所有交互由宿主计划视图模型及独立草稿命令承担。</summary>
public partial class PuloniaTaskTriggerView : UserControl
{
    /// <summary>初始化可滚动的配置布局。</summary>
    public PuloniaTaskTriggerView() => InitializeComponent();
}
