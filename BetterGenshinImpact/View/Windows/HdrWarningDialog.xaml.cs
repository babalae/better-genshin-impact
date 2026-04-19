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
    OpenGraphicsSettings,
    Continue,
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
        SwitchButton.Visibility = decision.CanSwitchToHdrCapture ? Visibility.Visible : Visibility.Collapsed;
        SettingsButton.Visibility = decision.CanOpenGraphicsSettings ? Visibility.Visible : Visibility.Collapsed;
        ContinueButton.Appearance = decision.ContinueIsPrimary
            ? Wpf.Ui.Controls.ControlAppearance.Primary
            : Wpf.Ui.Controls.ControlAppearance.Secondary;
        SettingsButton.Appearance = !decision.CanSwitchToHdrCapture && decision.CanOpenGraphicsSettings && !decision.ContinueIsPrimary
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
        _result = HdrWarningDialogResult.SwitchToHdrCapture;
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
}
