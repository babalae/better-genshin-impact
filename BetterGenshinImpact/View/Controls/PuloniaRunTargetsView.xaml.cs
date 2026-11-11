using System.Windows.Controls;

namespace BetterGenshinImpact.View.Controls;

/// <summary>
/// 任务计划页与新一条龙页共用的“运行于”：收起时概括所选实例，展开后勾选实例并可就地修改各实例的当前账户。
/// DataContext 为 PuloniaRunTargetsViewModel，交互通过视图模型的属性与命令完成。
/// </summary>
public partial class PuloniaRunTargetsView : UserControl
{
    /// <summary>
    /// 初始化共用布局，数据上下文由宿主页面提供。
    /// </summary>
    public PuloniaRunTargetsView()
    {
        InitializeComponent();
    }
}
