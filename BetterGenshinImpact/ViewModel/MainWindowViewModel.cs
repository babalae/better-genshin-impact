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

        private TaskDispatcher _taskDispatcher = new();


        [RelayCommand]
        private void OnLoaded()
        {
            //TestMask();
            //TestRect();
            //Debug.WriteLine(DpiHelper.ScaleY);
        }

        [RelayCommand]
        private void OnClosed()
        {
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

            ShowMaskWindow(hWnd);
            _taskDispatcher.Start(hWnd, SelectedMode.ToCaptureMode());
        }

        [RelayCommand]
        private void OnStopTrigger()
        {
            if (SelectedMode == null)
            {
                MessageBox.Show("请选择捕获方式");
                return;
            }

            _maskWindow?.Hide();
            _taskDispatcher.Stop();
        }

        private void ShowMaskWindow(IntPtr hWnd)
        {
            _maskWindow ??= MaskWindow.Instance();
           
            _maskWindow.Show();
            WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<IntPtr>(this, "hWnd", hWnd, hWnd));

        }
    }
}