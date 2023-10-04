using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.View;
using BetterGenshinImpact.View.Test;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fischless.WindowCapture;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Windows;
using BetterGenshinImpact.Core;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Vanara.PInvoke;

namespace BetterGenshinImpact.ViewModel
{
    public partial class MainWindowViewModel : ObservableObject
    {
        [ObservableProperty] private string[] _modeNames = WindowCaptureFactory.ModeNames();

        [ObservableProperty] private string? _selectedMode = CaptureModes.BitBlt.ToString();

        private MaskWindow? _maskWindow;
        private readonly ILogger<MainWindowViewModel> _logger = App.GetLogger<MainWindowViewModel>();

        private readonly TaskDispatcher _taskDispatcher = new();
        private readonly MouseKeyMonitor _mouseKeyMonitor = new();

        [RelayCommand]
        private void OnLoaded()
        {

        }

        [RelayCommand]
        private void OnClosed()
        {
            _mouseKeyMonitor.Unsubscribe();
            OnStopTrigger();
            _maskWindow?.Close();
            Application.Current.Shutdown();
        }

        [RelayCommand]
        private void OnStartCaptureTest()
        {
            if (SelectedMode == null)
            {
                MessageBox.Show("请选择捕获方式");
                return;
            }

            var hWnd = SystemControl.FindGenshinImpactHandle();
            if (hWnd == IntPtr.Zero)
            {
                MessageBox.Show("未找到原神窗口");
                return;
            }

            CaptureTestWindow captureTestWindow = new();
            captureTestWindow.StartCapture(hWnd, SelectedMode.ToCaptureMode());
            captureTestWindow.Show();
        }

        [RelayCommand]
        private void OnStartTrigger()
        {
            if (SelectedMode == null)
            {
                MessageBox.Show("请选择捕获方式");
                return;
            }

            var hWnd = SystemControl.FindGenshinImpactHandle();
            if (hWnd == IntPtr.Zero)
            {
                MessageBox.Show("未找到原神窗口");
                return;
            }
            _mouseKeyMonitor.Subscribe(hWnd);
            _maskWindow = MaskWindow.Instance(hWnd);
            _taskDispatcher.Start(hWnd, SelectedMode.ToCaptureMode());
        }

        [RelayCommand]
        private void OnStopTrigger()
        {
            _maskWindow?.Hide();
            _taskDispatcher.Stop();
        }
    }
}