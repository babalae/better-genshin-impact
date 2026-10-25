using System.Windows.Controls;
using BetterGenshinImpact.ViewModel.Resin;

namespace BetterGenshinImpact.View.Controls.Resin;

/// <summary>领奖树脂编辑器内容，仅负责初始化视图和接收草稿 VM。</summary>
public partial class ResinUsageEditorView : UserControl
{
    /// <summary>供窗口 DataTemplate 构造，沿用模板提供的 DataContext。</summary>
    public ResinUsageEditorView() => InitializeComponent();

    /// <summary>供独立宿主与布局测试显式注入草稿。</summary>
    public ResinUsageEditorView(ResinUsageEditorViewModel viewModel) : this() => DataContext = viewModel;
}
