using System.Windows.Controls;

namespace BetterGenshinImpact.View.Controls;

/// <summary>新一条龙的执行记录窗口内容：共用执行记录视图，不维护第二份记录布局；路线 CD 在账户页查看。</summary>
public partial class PuloniaOneDragonHistoryView : UserControl
{
    /// <summary>建立独立界面，不订阅或维护第二份执行状态。</summary>
    public PuloniaOneDragonHistoryView() => InitializeComponent();
}
