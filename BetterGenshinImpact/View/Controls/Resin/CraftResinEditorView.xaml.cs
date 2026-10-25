using System.Windows.Controls;
using BetterGenshinImpact.ViewModel.Resin;

namespace BetterGenshinImpact.View.Controls.Resin;

/// <summary>合成树脂编辑器内容，不在代码后置处理输入逻辑。</summary>
public partial class CraftResinEditorView : UserControl
{
    /// <summary>供窗口模板构造并继承 DataContext。</summary>
    public CraftResinEditorView() => InitializeComponent();

    /// <summary>显式注入草稿，供独立构造和布局测试。</summary>
    public CraftResinEditorView(CraftResinEditorViewModel viewModel) : this() => DataContext = viewModel;
}
