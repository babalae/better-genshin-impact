using System;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows.Pulonia;

/// <summary>新一条龙专用的公共配置窗口，原任务计划继续使用原表单。</summary>
public partial class PuloniaOneDragonCommonSettingsDialog : FluentWindow
{
    /// <summary>与原计划分离的模态编辑草稿。</summary>
    public PuloniaOneDragonCommonSettingsViewModel ViewModel { get; }

    /// <summary>绑定独立草稿并订阅关闭请求。</summary>
    public PuloniaOneDragonCommonSettingsDialog(PuloniaOneDragonCommonSettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        ViewModel.RequestClose += OnRequestClose;
        Closed += OnClosed;
    }

    /// <summary>确认或取消时结束模态窗口。</summary>
    private void OnRequestClose(object? sender, bool result) => DialogResult = result;

    /// <summary>关闭时释放视图与草稿的事件引用。</summary>
    private void OnClosed(object? sender, EventArgs e)
    {
        ViewModel.RequestClose -= OnRequestClose;
        Closed -= OnClosed;
    }
}
