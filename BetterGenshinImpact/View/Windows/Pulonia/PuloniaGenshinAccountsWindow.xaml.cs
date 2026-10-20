using System;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows.Pulonia;

/// <summary>原神账号管理窗口，业务和异步操作由视图模型负责。</summary>
public partial class PuloniaGenshinAccountsWindow : FluentWindow
{
    /// <summary>窗口独立的账号编辑会话。</summary>
    public PuloniaGenshinAccountsViewModel ViewModel { get; }
    /// <summary>建立绑定；关闭时取消本窗口的输入操作。</summary>
    public PuloniaGenshinAccountsWindow(PuloniaGenshinAccountsViewModel viewModel)
    {
        ViewModel = viewModel; DataContext = viewModel; InitializeComponent();
        SourceInitialized += (_, _) => WindowHelper.TryApplySystemBackdrop(this);
        Closed += OnClosed;
    }
    /// <summary>释放窗口拥有的操作，不停止其他入口的运行。</summary>
    private void OnClosed(object? sender, EventArgs e) => ViewModel.Dispose();
}
