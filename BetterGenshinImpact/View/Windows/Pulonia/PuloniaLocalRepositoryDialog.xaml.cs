using System;
using System.ComponentModel;
using System.Windows;
using BetterGenshinImpact.Core.Script.Repositories;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows.Pulonia;

/// <summary>目录和 ZIP 共用的本地仓库添加窗口，成功后返回注册并自动关闭。</summary>
public partial class PuloniaLocalRepositoryDialog : FluentWindow
{
    /// <summary>窗口使用的路径输入、导入进度与添加结果。</summary>
    public PuloniaLocalRepositoryDialogViewModel ViewModel { get; }

    /// <summary>建立绑定，文件选择和仓库业务均交给视图模型。</summary>
    public PuloniaLocalRepositoryDialog(PuloniaRepositoryManagementService service)
    {
        ViewModel = new PuloniaLocalRepositoryDialogViewModel(service);
        DataContext = ViewModel;
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>模态添加结束后返回成功的注册对象，取消或失败不会返回草稿。</summary>
    public static ScriptRepositoryRegistration? Show(PuloniaRepositoryManagementService service, Window? owner)
    {
        var window = new PuloniaLocalRepositoryDialog(service) { Owner = owner ?? Application.Current.MainWindow };
        return window.ShowDialog() == true ? window.ViewModel.AddedRepository : null;
    }

    /// <summary>只有完整添加成功时才关闭窗口并向管理列表交回来源。</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ViewModel.AddedRepository) && ViewModel.AddedRepository is not null)
            DialogResult = true;
    }

    /// <summary>校验与写入期间等待完成，保证 ZIP 解压后继续完成仓库注册。</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (ViewModel.IsBusy) e.Cancel = true;
        base.OnClosing(e);
    }

    /// <summary>关闭时解除视图模型监听，后续进度不再关联窗口。</summary>
    protected override void OnClosed(EventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        base.OnClosed(e);
    }
}
