using BetterGenshinImpact.Core;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.View.Test;
using BetterGenshinImpact.View;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fischless.WindowCapture;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.Messaging.Messages;
using CommunityToolkit.Mvvm.Messaging;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class HomePageViewModel : ObservableObject, INavigationAware
{
    [ObservableProperty] private string[] _modeNames = WindowCaptureFactory.ModeNames();

    [ObservableProperty] private string? _selectedMode = CaptureModes.BitBlt.ToString();

    private bool _taskDispatcherEnabled = false;
    [ObservableProperty] private Visibility _startButtonVisibility = Visibility.Visible;
    [ObservableProperty] private Visibility _stopButtonVisibility = Visibility.Collapsed;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(StartTriggerCommand))]
    private bool _startButtonEnabled = true;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(StopTriggerCommand))]
    private bool _stopButtonEnabled = true;

    private MaskWindow? _maskWindow;
    private readonly ILogger<HomePageViewModel> _logger = App.GetLogger<HomePageViewModel>();

    private readonly TaskDispatcher _taskDispatcher = new();
    private readonly MouseKeyMonitor _mouseKeyMonitor = new();

    public HomePageViewModel()
    {
        WeakReferenceMessenger.Default.Register<PropertyChangedMessage<object>>(this, (sender, msg) =>
        {
            if (msg.PropertyName == "Close")
            {
                OnClosed();
            }
        });
    }


    [RelayCommand]
    private void OnLoaded()
    {
        Debug.WriteLine("HomePageViewModel Loaded");
    }

    private void OnClosed()
    {
        OnStopTrigger();
        // 等待任务结束
        _maskWindow?.Close();
        Debug.WriteLine("HomePageViewModel Closed");
    }

    [RelayCommand]
    private void OnStartCaptureTest()
    {
        if (SelectedMode == null)
        {
            System.Windows.MessageBox.Show("请选择捕获方式");
            return;
        }

        var hWnd = SystemControl.FindGenshinImpactHandle();
        if (hWnd == IntPtr.Zero)
        {
            System.Windows.MessageBox.Show("未找到原神窗口");
            return;
        }

        CaptureTestWindow captureTestWindow = new();
        captureTestWindow.StartCapture(hWnd, SelectedMode.ToCaptureMode());
        captureTestWindow.Show();
    }

    private bool CanStartTrigger() => StartButtonEnabled;

    [RelayCommand(CanExecute = nameof(CanStartTrigger))]
    private void OnStartTrigger()
    {
        if (SelectedMode == null)
        {
            System.Windows.MessageBox.Show("请选择捕获方式");
            return;
        }

        var hWnd = SystemControl.FindGenshinImpactHandle();
        if (hWnd == IntPtr.Zero)
        {
            System.Windows.MessageBox.Show("未找到原神窗口");
            return;
        }


        if (!_taskDispatcherEnabled)
        {
            _mouseKeyMonitor.Subscribe(hWnd);
            _maskWindow = MaskWindow.Instance(hWnd);
            _taskDispatcher.Start(hWnd, SelectedMode.ToCaptureMode());
            _taskDispatcherEnabled = true;
            StartButtonVisibility = Visibility.Collapsed;
            StopButtonVisibility = Visibility.Visible;
        }
    }

    private bool CanStopTrigger() => StopButtonEnabled;

    [RelayCommand(CanExecute = nameof(CanStopTrigger))]
    private void OnStopTrigger()
    {
        if (_taskDispatcherEnabled)
        {
            _mouseKeyMonitor.Unsubscribe();
            _maskWindow?.Hide();
            _taskDispatcher.Stop();
            _taskDispatcherEnabled = false;
            StartButtonVisibility = Visibility.Visible;
            StopButtonVisibility = Visibility.Collapsed;
        }
    }

    public void OnNavigatedTo()
    {
    }

    public void OnNavigatedFrom()
    {

    }
}