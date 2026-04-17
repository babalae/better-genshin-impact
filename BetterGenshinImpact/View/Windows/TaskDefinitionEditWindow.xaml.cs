using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Windows;
using System;
using System.Windows;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows;

public partial class TaskDefinitionEditWindow : FluentWindow
{
    public TaskDefinitionEditWindowViewModel ViewModel { get; }

    public TaskDefinitionEditWindow(TaskDefinitionEditWindowViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        
        // 设置窗口关闭事件
        ViewModel.RequestClose += (sender, result) =>
        {
            DialogResult = result;
            Close();
        };
        
        // 窗口加载和初始化事件
        this.Loaded += TaskDefinitionEditWindowLoaded;
        this.SourceInitialized += TaskDefinitionEditWindow_SourceInitialized;
    }

    private void TaskDefinitionEditWindow_SourceInitialized(object? sender, EventArgs e)
    {
        // 应用与主窗口相同的背景主题
        WindowHelper.TryApplySystemBackdrop(this);
    }

    private void TaskDefinitionEditWindowLoaded(object sender, RoutedEventArgs e)
    {
        // 自动聚焦到名称输入框
        NameTextBox.Focus();
    }
}