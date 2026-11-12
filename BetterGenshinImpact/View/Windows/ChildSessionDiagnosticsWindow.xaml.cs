using System.Windows;
using System.Windows.Media;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Windows;
using Wpf.Ui.Appearance;

namespace BetterGenshinImpact.View.Windows;

/// <summary>
/// 桌面分身的诊断与修复面板。
///
/// 入口有两个：控制中心 → 更多 → 诊断与修复…，以及启动失败后的诊断按钮。
/// 仅在用户主动打开面板后进行环境检查。
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

        // TryApplySystemBackdrop 会按「当时」的主题把 XAML 里的 DynamicResource 换成固定画刷，
        // 之后 Background 就不再跟着主题走了。面板可以一直开着（用户去改设置、切主题），
        // 所以主题一变就按新主题再刷一次，关窗时取消订阅。
        ApplicationThemeManager.Changed += OnApplicationThemeChanged;
        Closed += OnClosed;
    }

    private void OnClosed(object? sender, System.EventArgs e)
    {
        ApplicationThemeManager.Changed -= OnApplicationThemeChanged;
        Closed -= OnClosed;
    }

    /// <summary>
    /// 主题变了：重新套一遍背景与背景材质，否则浅色主题下会是深底深字。
    /// </summary>
    private void OnApplicationThemeChanged(
        ApplicationTheme currentApplicationTheme,
        Color systemAccent)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => WindowHelper.TryApplySystemBackdrop(this));
            return;
        }

        WindowHelper.TryApplySystemBackdrop(this);
    }

    /// <summary>
    /// 关闭按钮：面板不保存任何状态，关掉即可。
    /// </summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
