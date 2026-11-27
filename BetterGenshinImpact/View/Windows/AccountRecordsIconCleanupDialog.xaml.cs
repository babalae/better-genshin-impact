using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Controls;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows;

/// <summary>分类选择窗口，清理操作由页面视图模型执行。</summary>
public partial class AccountRecordsIconCleanupDialog : FluentWindow
{
    /// <summary>绑定独立选择状态并启用系统主题。</summary>
    public AccountRecordsIconCleanupDialog(AccountRecordsIconCleanupViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        SourceInitialized += (_, _) => WindowHelper.TryApplySystemBackdrop(this);
        viewModel.CloseRequested += Close;
        Closed += (_, _) => viewModel.CloseRequested -= Close;
    }
}
