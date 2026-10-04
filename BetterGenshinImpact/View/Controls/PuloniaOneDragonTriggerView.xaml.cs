using System.Windows.Controls;

namespace BetterGenshinImpact.View.Controls;

/// <summary>新一条龙的触发器卡片界面，共用原计划的调度配置与草稿。</summary>
public partial class PuloniaOneDragonTriggerView : UserControl
{
    /// <summary>建立独立视图，运行时仍由唯一 Pulonia 调度宿主管理。</summary>
    public PuloniaOneDragonTriggerView() => InitializeComponent();
}
