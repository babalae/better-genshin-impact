using System;
using System.Windows;
using BetterGenshinImpact.Core.Script.Repositories;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows.Pulonia;

/// <summary>Pulonia 专用仓库管理窗口，集中管理选择器所使用的来源引用。</summary>
public partial class PuloniaRepositoryManagerWindow : FluentWindow
{
    /// <summary>窗口使用的管理视图模型。</summary>
    public PuloniaRepositoryManagerViewModel ViewModel { get; }

    /// <summary>建立绑定和窗口生命周期事件，业务命令均在视图模型执行。</summary>
    public PuloniaRepositoryManagerWindow(PuloniaRepositoryManagementService service, string? repositoryId)
    {
        ViewModel = new PuloniaRepositoryManagerViewModel(service, repositoryId);
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    /// <summary>从添加任务入口打开，并返回关闭时选择的稳定仓库身份。</summary>
    public static string? Show(ScriptRepositoryStore repositories, string? repositoryId, Window? owner)
    {
        var window = new PuloniaRepositoryManagerWindow(new PuloniaRepositoryManagementService(repositories), repositoryId)
        { Owner = owner ?? Application.Current.MainWindow };
        window.ShowDialog();
        return window.ViewModel.SelectedRepositoryId;
    }

    /// <summary>窗口呈现后读取配置，让初始化状态先于磁盘访问显示。</summary>
    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        Loaded -= OnLoaded;
        await ViewModel.InitializeAsync();
    }

    /// <summary>关闭时取消请求并解除共享官方配置的订阅。</summary>
    private void OnClosed(object? sender, EventArgs args)
    {
        ViewModel.Close();
        Loaded -= OnLoaded;
        Closed -= OnClosed;
    }
}
