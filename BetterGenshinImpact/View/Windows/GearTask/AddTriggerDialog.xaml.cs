using System;
using System.Windows;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Windows.GearTask;
using BetterGenshinImpact.ViewModel.Pages.Component;
using Microsoft.Extensions.DependencyInjection;
using BetterGenshinImpact.Service;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.View.Windows.GearTask;

public partial class AddTriggerDialog
{
    public AddTriggerDialogViewModel ViewModel { get; }

    public AddTriggerDialog(AddTriggerDialogViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        
        InitializeComponent();
        
        ViewModel.RequestClose += OnRequestClose;
        this.SourceInitialized += OnSourceInitialized;
        this.Loaded += OnLoaded;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // 应用与主窗口相同的背景主题
        WindowHelper.TryApplySystemBackdrop(this);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 自动聚焦到名称输入框
        TriggerNameTextBox.Focus();
        TriggerNameTextBox.SelectAll();
    }

    private void OnRequestClose(object? sender, bool result)
    {
        DialogResult = result;
        Close();
    }

    /// <summary>
    /// 显示新增触发器对话框
    /// </summary>
    /// <returns>如果用户点击确定返回创建的触发器ViewModel，否则返回null</returns>
    public static AddTriggerDialogViewModel? ShowAddTriggerDialog()
    {
        // 使用依赖注入获取 ViewModel
        var storageService = App.GetRequiredService<GearTaskStorageService>();
        var logger = App.GetRequiredService<ILogger<AddTriggerDialogViewModel>>();
        var viewModel = new AddTriggerDialogViewModel(storageService, logger);
            
        var dialog = new AddTriggerDialog(viewModel)
        {
            Owner = Application.Current.MainWindow
        };
        var result = dialog.ShowDialog();
        return result == true ? dialog.ViewModel : null;
    }

    /// <summary>
    /// 显示新增触发器对话框，指定触发器类型
    /// </summary>
    /// <param name="predefinedType">预定义的触发器类型</param>
    /// <returns>如果用户点击确定返回创建的触发器ViewModel，否则返回null</returns>
    public static AddTriggerDialogViewModel? ShowAddTriggerDialog(TriggerType predefinedType)
    {
        // 使用依赖注入获取 ViewModel
        var storageService = App.GetRequiredService<GearTaskStorageService>();
        var logger = App.GetRequiredService<ILogger<AddTriggerDialogViewModel>>();
        var viewModel = new AddTriggerDialogViewModel(storageService, logger, predefinedType);
            
        var dialog = new AddTriggerDialog(viewModel)
        {
            Owner = Application.Current.MainWindow
        };
        var result = dialog.ShowDialog();
        return result == true ? dialog.ViewModel : null;
    }
}