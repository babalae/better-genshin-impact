using System;
using System.Windows;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Windows;

namespace BetterGenshinImpact.View.Windows;

public partial class AddTaskNodeDialog
{
    public AddTaskNodeDialogViewModel ViewModel { get; }

    public AddTaskNodeDialog(string taskType)
    {
        ViewModel = new AddTaskNodeDialogViewModel(taskType);
        DataContext = ViewModel;
        
        InitializeComponent();
        
        ViewModel.RequestClose += OnRequestClose;
        this.SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // 应用与主窗口相同的背景主题
        WindowHelper.TryApplySystemBackdrop(this);
    }

    private void OnRequestClose(object? sender, bool result)
    {
        DialogResult = result;
        Close();
    }

    /// <summary>
    /// 显示添加任务对话框
    /// </summary>
    /// <param name="taskType">任务类型</param>
    /// <param name="owner">父窗口</param>
    /// <returns>如果用户点击确定返回ViewModel，否则返回null</returns>
    public static AddTaskNodeDialogViewModel? ShowDialog(string taskType, Window? owner = null)
    {
        var dialog = new AddTaskNodeDialog(taskType);
        if (owner != null)
        {
            dialog.Owner = owner;
        }
        
        var result = dialog.ShowDialog();
        return result == true ? dialog.ViewModel : null;
    }
}