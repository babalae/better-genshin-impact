using System.Windows.Controls;

namespace BetterGenshinImpact.View.Controls;

/// <summary>新一条龙的记录卡片视图，共用 Pulonia 历史模型和续跑命令。</summary>
public partial class PuloniaOneDragonHistoryView : UserControl
{
    /// <summary>建立独立界面，不订阅或维护第二份执行状态。</summary>
    public PuloniaOneDragonHistoryView() => InitializeComponent();
}
