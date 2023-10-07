using BetterGenshinImpact.Core;
using BetterGenshinImpact.GameTask;
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
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.ONNX.SVTR;
using OpenCvSharp;
using System.Windows.Interop;
using BetterGenshinImpact.Helpers;

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

    public AllConfig Config { get; set; }

    private MaskWindow? _maskWindow;
    private readonly ILogger<HomePageViewModel> _logger = App.GetLogger<HomePageViewModel>();

    private readonly TaskDispatcher _taskDispatcher = new();
    private readonly MouseKeyMonitor _mouseKeyMonitor = new();

    public HomePageViewModel(IConfigService configService)
    {
        Config = configService.Get();
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
#if DEBUG
        //var hWnd = SystemControl.FindGenshinImpactHandle();
        //if (hWnd != IntPtr.Zero)
        //{
        //    OnStartTrigger();
        //}
#endif
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
        //var hWnd = SystemControl.FindGenshinImpactHandle();
        //if (hWnd == IntPtr.Zero)
        //{
        //    System.Windows.MessageBox.Show("未找到原神窗口");
        //    return;
        //}

        //CaptureTestWindow captureTestWindow = new();
        //captureTestWindow.StartCapture(hWnd, Config.CaptureMode.ToCaptureMode());
        //captureTestWindow.Show();

        var picker = new PickerWindow();
        var hWnd = picker.PickCaptureTarget(new WindowInteropHelper(UIDispatcherHelper.MainWindow).Handle);
        if (hWnd != IntPtr.Zero)
        {
            var captureWindow = new CaptureTestWindow();
            captureWindow.StartCapture(hWnd, Config.CaptureMode.ToCaptureMode());
            captureWindow.Show();
        }
    }

    private bool CanStartTrigger() => StartButtonEnabled;

    [RelayCommand(CanExecute = nameof(CanStartTrigger))]
    private void OnStartTrigger()
    {
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
            _taskDispatcher.Start(hWnd, Config.CaptureMode.ToCaptureMode());
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

    [RelayCommand]
    private void OnSvtrTest()
    {
        var mat = new Mat(Global.Absolute("Config\\Model\\Yap\\0_2_「甜甜花」的种子_bin_38x227.jpg"), ImreadModes.Grayscale);
        new SvtrModelRunner().RunInferenceMore(mat);
    }
}