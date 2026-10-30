using BetterGenshinImpact.Helpers.Ui;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows;

/// <summary>蓝色按钮的数据接入占位窗口。</summary>
public partial class AccountRecordsPlaceholderDialog : FluentWindow
{
    /// <summary>初始化占位内容并应用主题背景。</summary>
    public AccountRecordsPlaceholderDialog()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowHelper.TryApplySystemBackdrop(this);
    }

    /// <summary>支持 Esc 关闭，无需底部关闭按钮。</summary>
    private void CloseWindow(object sender, ExecutedRoutedEventArgs e)
    {
        e.Handled = true;
        Close();
    }
}
