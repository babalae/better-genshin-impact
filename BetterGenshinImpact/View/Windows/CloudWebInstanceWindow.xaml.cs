using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Windows;
using System;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows;

/// <summary>
/// 云原神实例管理窗口，界面只负责主题与窗口生命周期。
/// </summary>
public partial class CloudWebInstanceWindow : FluentWindow
{
    /// <summary>通过构造函数注入的实例管理状态。</summary>
    public CloudWebInstanceWindowViewModel ViewModel { get; }

    /// <summary>创建紧凑的模态管理窗口。</summary>
    public CloudWebInstanceWindow(CloudWebInstanceWindowViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        ViewModel.CloseRequested += OnCloseRequested;
    }

    /// <summary>跟随应用主题启用系统背景。</summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        WindowHelper.TryApplySystemBackdrop(this);
    }

    /// <summary>响应 ViewModel 的关闭请求，不关闭已运行的云原神游戏窗口。</summary>
    private void OnCloseRequested(object? sender, EventArgs e)
    {
        Close();
    }

    /// <summary>关闭窗口时解除订阅并停止后台状态刷新。</summary>
    protected override void OnClosed(EventArgs e)
    {
        SourceInitialized -= OnSourceInitialized;
        ViewModel.CloseRequested -= OnCloseRequested;
        ViewModel.Dispose();
        base.OnClosed(e);
    }
}
