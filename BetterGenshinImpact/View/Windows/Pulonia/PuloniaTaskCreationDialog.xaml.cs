using System;
using System.Collections.Generic;
using System.Windows;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows.Pulonia;

/// <summary>
/// Pulonia 任务创建弹窗；确认前只编辑临时状态，不修改任务树。
/// </summary>
public partial class PuloniaTaskCreationDialog : FluentWindow
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
    public PuloniaTaskCreationDialog(IReadOnlyList<PuloniaTaskDefinition> definitions,
        PuloniaTaskResourceCatalog resourceCatalog, string creationTaskType, string? initialResourceId = null)
    {
        ViewModel = new PuloniaTaskCreationDialogViewModel(definitions, resourceCatalog, creationTaskType,
            initialResourceId);
        DataContext = ViewModel;
        InitializeComponent();
        ViewModel.RequestClose += OnRequestClose;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    /// <summary>
    /// 以模态方式显示创建弹窗，仅在确认成功后返回完整结果。
    /// </summary>
    public static PuloniaTaskCreationResult? Show(IReadOnlyList<PuloniaTaskDefinition> definitions,
        PuloniaTaskResourceCatalog resourceCatalog, string creationTaskType, string? initialResourceId = null,
        Window? owner = null)
    {
        var dialog = new PuloniaTaskCreationDialog(definitions, resourceCatalog, creationTaskType, initialResourceId)
        {
            Owner = owner ?? Application.Current.MainWindow
        };
        return dialog.ShowDialog() == true ? dialog.ViewModel.Result : null;
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
