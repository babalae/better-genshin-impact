using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.View.Controls.ChildSession;
using BetterGenshinImpact.ViewModel.Windows;
using Wpf.Ui.Controls;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace BetterGenshinImpact.View.Windows;

public partial class ChildSessionWindow : FluentWindow
{
    private readonly ChildSessionWindowViewModel _viewModel;
    private bool _closeRequestInProgress;

    internal RdpActiveXHost RdpHost { get; } = new();

    internal bool AllowClose { get; set; }

    public ChildSessionWindow(ChildSessionWindowViewModel viewModel)
    {
        DataContext = _viewModel = viewModel;
        InitializeComponent();
        RdpFormsHost.Child = RdpHost;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowHelper.TryApplySystemBackdrop(this);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (AllowClose || Dispatcher.HasShutdownStarted)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        base.OnClosing(e);

        if (_closeRequestInProgress)
        {
            return;
        }

        if (!_viewModel.HasChildSession)
        {
            Hide();
            return;
        }

        var result = ThemedMessageBox.Show(
            "关闭会断开 RDP 并注销桌面分身，其中所有正在运行的软件都会被关闭，未保存的数据会丢失。是否继续？",
            "关闭 BetterGI 桌面分身",
            MessageBoxButton.YesNo,
            ThemedMessageBox.MessageBoxIcon.Warning,
            MessageBoxResult.No,
            this);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _closeRequestInProgress = true;
        _ = LogoffAndHideAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        RdpFormsHost.Child = null;
        RdpHost.Dispose();
        base.OnClosed(e);
    }

    private async Task LogoffAndHideAsync()
    {
        try
        {
            await _viewModel.LogoffAndHideAsync();
        }
        finally
        {
            _closeRequestInProgress = false;
        }
    }
}
