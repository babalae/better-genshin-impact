using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Windows;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows;

/// <summary>
/// 一键宏配置窗口，按角色编辑手写宏脚本
/// </summary>
public partial class AvatarMacroConfigWindow : FluentWindow
{
    /// <summary>
    /// 窗口 ViewModel
    /// </summary>
    private readonly AvatarMacroConfigViewModel _viewModel;

    /// <summary>
    /// 创建窗口，配置文件读取失败时由 ViewModel 抛出异常
    /// </summary>
    public AvatarMacroConfigWindow()
    {
        _viewModel = new AvatarMacroConfigViewModel();
        DataContext = _viewModel;
        InitializeComponent();

        _viewModel.CloseRequested += OnCloseRequested;
        SourceInitialized += (_, _) => WindowHelper.TryApplySystemBackdrop(this);
        Closed += (_, _) => _viewModel.CloseRequested -= OnCloseRequested;
    }

    /// <summary>
    /// 响应 ViewModel 的关闭请求
    /// </summary>
    /// <param name="dialogResult">对话框结果</param>
    private void OnCloseRequested(bool? dialogResult)
    {
        DialogResult = dialogResult;
    }
}
