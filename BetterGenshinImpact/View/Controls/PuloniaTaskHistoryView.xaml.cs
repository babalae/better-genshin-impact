using System.Windows.Controls;

namespace BetterGenshinImpact.View.Controls;

/// <summary>
/// 全局页面与计划页复用的只读历史浏览器，交互通过视图模型命令完成。
/// </summary>
public partial class PuloniaTaskHistoryView : UserControl
{
    /// <summary>
    /// 初始化共用布局，数据上下文由宿主页面提供。
    /// </summary>
    public PuloniaTaskHistoryView()
    {
        InitializeComponent();
    }
}
