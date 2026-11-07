using System.Windows;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Windows;

namespace BetterGenshinImpact.View.Windows;

/// <summary>
/// 桌面分身的诊断与修复面板。
///
/// 入口有两个：控制中心 → 更多 → 诊断与修复…，以及启动失败后自动弹出。
/// 检查与修复都在服务层完成，这里只负责显示。
/// </summary>
public partial class ChildSessionDiagnosticsWindow
{
    /// <summary>
    /// 用视图模型构造窗口。
    /// </summary>
    /// <param name="viewModel">诊断面板的视图模型。</param>
    public ChildSessionDiagnosticsWindow(ChildSessionDiagnosticsWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += (_, _) => WindowHelper.TryApplySystemBackdrop(this);
    }

    /// <summary>
    /// 关闭按钮：面板不保存任何状态，关掉即可。
    /// </summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
