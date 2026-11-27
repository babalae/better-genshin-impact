using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Controls;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows;

/// <summary>只读快照窗口，不提供库存修改命令。</summary>
public partial class AccountRecordsSnapshotsDialog : FluentWindow
{
    /// <summary>注入视图模型并应用系统主题；窗口关闭后停止后续历史加载。</summary>
    public AccountRecordsSnapshotsDialog(AccountRecordsSnapshotsViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        SourceInitialized += (_, _) => WindowHelper.TryApplySystemBackdrop(this);
        Loaded += async (_, _) => await viewModel.InitializeAsync();
        Closed += (_, _) => viewModel.Close();
    }
}
