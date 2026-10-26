using BetterGenshinImpact.Helpers.Ui;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows;

/// <summary>账号记录的统一导入占位窗口。</summary>
public partial class AccountRecordsImportDialog : FluentWindow
{
    /// <summary>初始化导入窗口，并应用与任务创建窗口一致的主题背景。</summary>
    public AccountRecordsImportDialog()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowHelper.TryApplySystemBackdrop(this);
    }
}
