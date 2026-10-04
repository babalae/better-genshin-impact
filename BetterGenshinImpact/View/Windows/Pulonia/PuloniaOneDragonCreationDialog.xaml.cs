using System;
using System.Collections.Generic;
using System.Windows;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows.Pulonia;

/// <summary>
/// 新一条龙专用任务创建弹窗；确认前只编辑临时状态，不修改任务树。
/// </summary>
public partial class PuloniaOneDragonCreationDialog : FluentWindow
{
    /// <summary>
    /// 弹窗使用的创建视图模型。
    /// </summary>
    public PuloniaTaskCreationDialogViewModel ViewModel { get; }

    /// <summary>
    /// 防止窗口重复加载资源索引。
    /// </summary>
    private bool _isInitialized;

    /// <summary>
    /// 使用当前能力定义和指定创建入口建立弹窗。
    /// </summary>
    public PuloniaOneDragonCreationDialog(IReadOnlyList<PuloniaTaskDefinition> definitions,
        PuloniaTaskResourceCatalog resourceCatalog, string creationTaskType)
    {
        ViewModel = new PuloniaTaskCreationDialogViewModel(definitions, resourceCatalog, creationTaskType);
        DataContext = ViewModel;
        InitializeComponent();
        ViewModel.RequestClose += OnRequestClose;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    /// <summary>
    /// 窗口首次显示后再建立资源索引，让加载状态能够先呈现。
    /// </summary>
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized)
            return;
        _isInitialized = true;
        await ViewModel.InitializeAsync();
    }

    /// <summary>
    /// 响应视图模型的确认或取消请求。
    /// </summary>
    private void OnRequestClose(object? sender, bool result)
    {
        if (result && ViewModel.Result is null)
            return;
        DialogResult = result;
    }

    /// <summary>
    /// 窗口关闭时解除事件订阅。
    /// </summary>
    private void OnClosed(object? sender, EventArgs e)
    {
        ViewModel.RequestClose -= OnRequestClose;
        Loaded -= OnLoaded;
        Closed -= OnClosed;
    }
}
