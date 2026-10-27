using System.Windows.Input;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Controls;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows;

/// <summary>物品及最近来源记录的只读查看窗口。</summary>
public partial class AccountRecordsItemDetailsDialog : FluentWindow
{
    /// <summary>绑定快照并应用主题。</summary>
    public AccountRecordsItemDetailsDialog(AccountRecordsItemDetailsViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        SourceInitialized += (_, _) => WindowHelper.TryApplySystemBackdrop(this);
    }
    /// <summary>Esc 关闭窗口。</summary>
    private void CloseWindow(object sender, ExecutedRoutedEventArgs e) { e.Handled = true; Close(); }
}