using System.Windows.Input;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Controls;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows;

/// <summary>事件溯源窗口，只读查看不会确认或删除事件。</summary>
public partial class AccountRecordsEventsDialog : FluentWindow
{
    /// <summary>应用主题并绑定只读快照。</summary>
    public AccountRecordsEventsDialog(AccountRecordsEventsViewModel viewModel)
    {
        DataContext = viewModel; InitializeComponent();
        Loaded += (_, _) => { if (viewModel.Selected != null) EventList.ScrollIntoView(viewModel.Selected); };
        SourceInitialized += (_, _) => WindowHelper.TryApplySystemBackdrop(this);
    }
    /// <summary>Esc 关闭。</summary>
    private void CloseWindow(object sender, ExecutedRoutedEventArgs e) { e.Handled = true; Close(); }
}
