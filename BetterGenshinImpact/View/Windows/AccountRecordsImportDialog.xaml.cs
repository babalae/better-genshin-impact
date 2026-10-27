using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Controls;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows;

/// <summary>账号记录的统一导入窗口。</summary>
public partial class AccountRecordsImportDialog : FluentWindow
{
    /// <summary>初始化导入窗口，并应用与任务创建窗口一致的主题背景。</summary>
    public AccountRecordsImportDialog(AccountRecordsImportViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        viewModel.CloseRequested += Close;
        Closed += (_, _) => viewModel.CloseRequested -= Close;
        Closing += (_, e) => e.Cancel = viewModel.IsBusy;
        SourceInitialized += (_, _) => WindowHelper.TryApplySystemBackdrop(this);
    }

    /// <summary>处理窗口关闭命令，使 Esc 不依赖底部取消按钮。</summary>
    private void CloseWindow(object sender, ExecutedRoutedEventArgs e)
    {
        e.Handled = true;
        Close();
    }
}
