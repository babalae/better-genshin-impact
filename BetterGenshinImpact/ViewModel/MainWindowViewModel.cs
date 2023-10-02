using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.View;
using BetterGenshinImpact.View.Test;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fischless.WindowCapture;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Windows;
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
            Debug.WriteLine(DpiHelper.ScaleY);
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

            _maskWindow?.Close();
            _taskDispatcher.Stop();
        }

        private void ShowMaskWindow(IntPtr hWnd)
        {
            User32.GetWindowRect(hWnd, out var rect);
            //var x = rect.X;
            //var y = rect.Y;
            //var w = rect.Width;
            //var h = rect.Height;

            var x = (int)Math.Ceiling(rect.X / DpiHelper.ScaleY);
            var y = (int)Math.Ceiling(rect.Y / DpiHelper.ScaleY);
            var w = (int)Math.Ceiling(rect.Width / DpiHelper.ScaleY);
            var h = (int)Math.Ceiling(rect.Height / DpiHelper.ScaleY);
            Debug.WriteLine($"原神窗口大小：{rect.Width} x {rect.Height}");
            Debug.WriteLine($"原神窗口大小(计算DPI缩放后)：{w} x {h}");

            //var x = 0;
            //var y = 0;
            //var w = 1200;
            //var h = 800;

            _maskWindow ??= MaskWindow.Instance();
            ////window.Owner = this;
            _maskWindow.Left = x;
            _maskWindow.Top = y;
            _maskWindow.Width = w;
            _maskWindow.Height = h;
            _maskWindow.Logger = App.GetLogger<MaskWindow>();
            _maskWindow.Show();

            _logger.LogInformation("- 遮罩窗口启动成功");
        }
    }
}