using System.Windows.Input;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Controls;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows;

/// <summary>字典查看窗口，公共物品目录不提供文件编辑入口。</summary>
public partial class AccountRecordsDictionaryDialog : FluentWindow
{
    /// <summary>绑定查看模型并应用主题。</summary>
    public AccountRecordsDictionaryDialog(AccountRecordsDictionaryViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        SourceInitialized += (_, _) => WindowHelper.TryApplySystemBackdrop(this);
    }
    /// <summary>支持 Esc 关闭。</summary>
    private void CloseWindow(object sender, ExecutedRoutedEventArgs e) { e.Handled = true; Close(); }
}