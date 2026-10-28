using System;
using System.Windows;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows.Pulonia;

/// <summary>
/// 运行预览窗口；是否开始执行由视图模型记录，打开它的入口在关闭后读取。
/// </summary>
public partial class PuloniaTaskRunPreviewWindow : FluentWindow
{
    /// <summary>使用运行预览建立窗口。</summary>
    /// <param name="viewModel">运行预览。</param>
    public PuloniaTaskRunPreviewWindow(PuloniaTaskRunPreviewViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        SourceInitialized += (_, _) => WindowHelper.TryApplySystemBackdrop(this);
        viewModel.CloseRequested += OnCloseRequested;
        Closed += (_, _) => viewModel.CloseRequested -= OnCloseRequested;
    }

    /// <summary>视图模型选择开始执行后关闭窗口。</summary>
    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    /// <summary>关闭预览，不开始执行。</summary>
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
