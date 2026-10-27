using System.ComponentModel;
using System.Windows;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows.Pulonia;

/// <summary>
/// 设置方案管理窗口；有未保存的草稿时关闭前确认。
/// </summary>
public partial class PuloniaSettingsProfileManagerWindow : FluentWindow
{
    /// <summary>窗口视图模型。</summary>
    private readonly PuloniaSettingsProfileManagerViewModel _viewModel;

    /// <summary>使用方案管理视图模型建立窗口。</summary>
    public PuloniaSettingsProfileManagerWindow(PuloniaSettingsProfileManagerViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        SourceInitialized += (_, _) => WindowHelper.TryApplySystemBackdrop(this);
        Closing += OnClosing;
    }

    /// <summary>点击关闭按钮。</summary>
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>有未保存的修改且用户不放弃时取消关闭。</summary>
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_viewModel.ConfirmClose())
            e.Cancel = true;
    }
}
