using System;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows.Pulonia;

/// <summary>
/// 任务节点编辑弹窗：基本信息与任务设置在“保存”后一次性提交，高级设置即时生效。
/// </summary>
public partial class PuloniaTaskNodeSettingsDialog : FluentWindow
{
    /// <summary>弹窗使用的视图模型。</summary>
    public PuloniaTaskNodeSettingsDialogViewModel ViewModel { get; }

    /// <summary>
    /// 使用待编辑节点的视图模型建立弹窗。
    /// </summary>
    /// <param name="viewModel">节点编辑视图模型。</param>
    public PuloniaTaskNodeSettingsDialog(PuloniaTaskNodeSettingsDialogViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
        SourceInitialized += (_, _) => WindowHelper.TryApplySystemBackdrop(this);
        ViewModel.RequestClose += OnRequestClose;
        Closed += OnClosed;
    }

    /// <summary>响应视图模型的保存或取消请求。</summary>
    private void OnRequestClose(object? sender, bool result) => DialogResult = result;

    /// <summary>窗口关闭时解除事件订阅。</summary>
    private void OnClosed(object? sender, EventArgs e)
    {
        ViewModel.Close();
        ViewModel.RequestClose -= OnRequestClose;
        Closed -= OnClosed;
    }
}
