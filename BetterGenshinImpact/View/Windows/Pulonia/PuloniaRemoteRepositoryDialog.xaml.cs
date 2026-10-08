using System;
using System.ComponentModel;
using System.Windows;
using BetterGenshinImpact.Core.Script.Repositories;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows.Pulonia;

/// <summary>独立远程仓库添加窗口，只负责展示草稿和交回成功注册。</summary>
public partial class PuloniaRemoteRepositoryDialog : FluentWindow
{
    /// <summary>窗口使用的远程仓库草稿与保存状态。</summary>
    public PuloniaRemoteRepositoryDialogViewModel ViewModel { get; }

    /// <summary>建立数据绑定，注册操作由视图模型执行。</summary>
    public PuloniaRemoteRepositoryDialog(ScriptRepositoryStore repositories)
    {
        ViewModel = new PuloniaRemoteRepositoryDialogViewModel(repositories);
        DataContext = ViewModel;
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>模态添加结束后只返回已经创建的仓库，取消时返回空。</summary>
    public static ScriptRepositoryRegistration? Show(ScriptRepositoryStore repositories, Window? owner)
    {
        var window = new PuloniaRemoteRepositoryDialog(repositories) { Owner = owner ?? Application.Current.MainWindow };
        return window.ShowDialog() == true ? window.ViewModel.AddedRepository : null;
    }

    /// <summary>保存成功后结束模态窗口，仓库列表由调用方刷新。</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ViewModel.AddedRepository) && ViewModel.AddedRepository is not null)
            DialogResult = true;
    }

    /// <summary>注册写入期间等待完成，避免用户取消后实际已经添加。</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (ViewModel.IsBusy) e.Cancel = true;
        base.OnClosing(e);
    }

    /// <summary>关闭时解除窗口对视图模型的监听。</summary>
    protected override void OnClosed(EventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        base.OnClosed(e);
    }
}
