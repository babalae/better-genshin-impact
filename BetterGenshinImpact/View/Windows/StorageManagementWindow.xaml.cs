using System;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Windows;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows;

/// <summary>
/// 存储空间窗口，界面只负责主题与窗口生命周期
/// </summary>
public partial class StorageManagementWindow : FluentWindow
{
    /// <summary>
    /// 通过构造函数注入的窗口状态
    /// </summary>
    public StorageManagementViewModel ViewModel { get; }

    public StorageManagementWindow(StorageManagementViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        ViewModel.CloseRequested += OnCloseRequested;
    }

    /// <summary>
    /// 跟随应用主题启用系统背景
    /// </summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        WindowHelper.TryApplySystemBackdrop(this);
    }

    /// <summary>
    /// 响应 ViewModel 的关闭请求
    /// </summary>
    private void OnCloseRequested(object? sender, EventArgs e)
    {
        Close();
    }

    /// <summary>
    /// 关闭窗口时解除订阅并取消进行中的统计
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        SourceInitialized -= OnSourceInitialized;
        ViewModel.CloseRequested -= OnCloseRequested;
        ViewModel.Dispose();
        base.OnClosed(e);
    }
}
