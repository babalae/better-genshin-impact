using System;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.Service.Hdr;

namespace BetterGenshinImpact.View.Windows;

public enum HdrWarningDialogResult
{
    None,
    SwitchToHdrCapture,
    SwitchToBitBlt,
    OpenGraphicsSettings,
    Continue,
    Cancel,
}

public partial class HdrWarningDialog : Wpf.Ui.Controls.FluentWindow
{
    private HdrWarningDialogResult _result = HdrWarningDialogResult.None;
    private TaskCompletionSource<HdrWarningDialogResult>? _taskCompletionSource;

    public HdrWarningDialog(HdrStartDecision decision)
    {
        InitializeComponent();

        Title = decision.Title;
        MessageTextBlock.Text = decision.Message;
        Owner = Application.Current.MainWindow;
        if (decision.CanSwitchToHdrCapture)
        {
            SwitchButton.Visibility = Visibility.Visible;
            SwitchButton.Content = "切到 WGC(HDR)";
            SwitchButton.ToolTip = "切换到 WindowsGraphicsCapture（HDR）";
        }
        else if (decision.CanSwitchToBitBlt)
        {
            SwitchButton.Visibility = Visibility.Visible;
            SwitchButton.Content = "切到 BitBlt";
            SwitchButton.ToolTip = "切换到 BitBlt";
        }
        else
        {
            SwitchButton.Visibility = Visibility.Collapsed;
        }

        SettingsButton.Visibility = decision.CanOpenGraphicsSettings ? Visibility.Visible : Visibility.Collapsed;
        ContinueButton.Appearance = decision.ContinueIsPrimary
            ? Wpf.Ui.Controls.ControlAppearance.Primary
            : Wpf.Ui.Controls.ControlAppearance.Secondary;
        SettingsButton.Appearance = !decision.CanSwitchToHdrCapture && !decision.CanSwitchToBitBlt && decision.CanOpenGraphicsSettings && !decision.ContinueIsPrimary
            ? Wpf.Ui.Controls.ControlAppearance.Primary
            : Wpf.Ui.Controls.ControlAppearance.Secondary;

        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
    }

    public Task<HdrWarningDialogResult> ShowDialogAsync()
    {
        _taskCompletionSource = new TaskCompletionSource<HdrWarningDialogResult>();
        ShowDialog();
        return _taskCompletionSource.Task;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        WindowHelper.TryApplySystemBackdrop(this);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _taskCompletionSource?.TrySetResult(_result);
    }

    private void SwitchButton_Click(object sender, RoutedEventArgs e)
    {
        _result = string.Equals(SwitchButton.Content?.ToString(), "切到 BitBlt", StringComparison.Ordinal)
            ? HdrWarningDialogResult.SwitchToBitBlt
            : HdrWarningDialogResult.SwitchToHdrCapture;
        Close();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        _result = HdrWarningDialogResult.OpenGraphicsSettings;
        Close();
    }

    private void ContinueButton_Click(object sender, RoutedEventArgs e)
    {
        _result = HdrWarningDialogResult.Continue;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _result = HdrWarningDialogResult.Cancel;
        Close();
    }
}
