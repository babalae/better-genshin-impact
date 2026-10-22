using System;
using System.Windows;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows.Pulonia;

/// <summary>
/// 新增独立任务弹窗；优先展示独立任务的专属设置界面，确认前不修改任务计划。
/// </summary>
public partial class PuloniaSoloTaskCreationDialog : FluentWindow
{
    /// <summary>弹窗使用的视图模型。</summary>
    public PuloniaSoloTaskCreationDialogViewModel ViewModel { get; }

    /// <summary>
    /// 使用已经准备好参数的视图模型建立弹窗。
    /// </summary>
    /// <param name="viewModel">新增独立任务视图模型。</param>
    public PuloniaSoloTaskCreationDialog(PuloniaSoloTaskCreationDialogViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
        SourceInitialized += (_, _) => WindowHelper.TryApplySystemBackdrop(this);
        ViewModel.RequestClose += OnRequestClose;
        Closed += OnClosed;
    }

    /// <summary>
    /// 以模态方式显示弹窗，确认后返回待插入的任务节点。
    /// </summary>
    /// <param name="viewModel">新增独立任务视图模型。</param>
    /// <param name="owner">所属窗口；为空时使用主窗口。</param>
    /// <returns>确认的任务节点；取消时为 null。</returns>
    public static PuloniaTask? Show(PuloniaSoloTaskCreationDialogViewModel viewModel, Window? owner = null)
    {
        var dialog = new PuloniaSoloTaskCreationDialog(viewModel)
        {
            Owner = owner ?? Application.Current.MainWindow
        };
        return dialog.ShowDialog() == true ? viewModel.Result : null;
    }

    /// <summary>响应视图模型的确认或取消请求。</summary>
    private void OnRequestClose(object? sender, bool result) => DialogResult = result;

    /// <summary>窗口关闭时解除事件订阅。</summary>
    private void OnClosed(object? sender, EventArgs e)
    {
        ViewModel.RequestClose -= OnRequestClose;
        Closed -= OnClosed;
    }
}
