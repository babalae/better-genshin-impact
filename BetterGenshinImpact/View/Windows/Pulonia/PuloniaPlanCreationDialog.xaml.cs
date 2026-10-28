using System;
using System.Windows;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows.Pulonia;

/// <summary>
/// 新建任务计划弹窗：填写名称并选择设置方案，确认前不创建计划。
/// </summary>
public partial class PuloniaPlanCreationDialog : FluentWindow
{
    /// <summary>弹窗使用的视图模型。</summary>
    public PuloniaPlanCreationDialogViewModel ViewModel { get; }

    /// <summary>
    /// 使用已准备好默认名称与方案选项的视图模型建立弹窗。
    /// </summary>
    /// <param name="viewModel">新建任务计划视图模型。</param>
    public PuloniaPlanCreationDialog(PuloniaPlanCreationDialogViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
        SourceInitialized += (_, _) => WindowHelper.TryApplySystemBackdrop(this);
        ViewModel.RequestClose += OnRequestClose;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    /// <summary>
    /// 以模态方式显示弹窗，确认后返回名称与方案。
    /// </summary>
    /// <param name="viewModel">新建任务计划视图模型。</param>
    /// <param name="owner">所属窗口；为空时使用主窗口。</param>
    /// <returns>确认结果；取消时为 null。</returns>
    public static PuloniaPlanCreationResult? Show(PuloniaPlanCreationDialogViewModel viewModel, Window? owner = null)
    {
        var dialog = new PuloniaPlanCreationDialog(viewModel)
        {
            Owner = owner ?? Application.Current.MainWindow
        };
        return dialog.ShowDialog() == true ? viewModel.Result : null;
    }

    /// <summary>打开后聚焦名称输入框并全选预填名称，便于直接输入新名称。</summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PlanNameTextBox.Focus();
        PlanNameTextBox.SelectAll();
    }

    /// <summary>响应视图模型的确认或取消请求。</summary>
    private void OnRequestClose(object? sender, bool result) => DialogResult = result;

    /// <summary>窗口关闭时解除事件订阅。</summary>
    private void OnClosed(object? sender, EventArgs e)
    {
        ViewModel.RequestClose -= OnRequestClose;
        Loaded -= OnLoaded;
        Closed -= OnClosed;
    }
}
