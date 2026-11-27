using System.Windows.Controls;

namespace BetterGenshinImpact.View.Controls;

/// <summary>需求表继承页面注入的子视图模型，交互由 Behaviors 和命令实现。</summary>
public partial class AccountRecordsDemandsView : UserControl
{
    /// <summary>初始化本视图可独立解析的资源。</summary>
    public AccountRecordsDemandsView() => InitializeComponent();
}
