using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.View.Test;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Windows;
using Windows.Win32.Foundation;
using Vision.Recognition;
using Vision.WindowCapture;
using static Windows.Win32.PInvoke;

namespace BetterGenshinImpact.ViewModel
{
    public partial class MainWindowViewModel : ObservableObject
    {
        [ObservableProperty] private string[] _modeNames = WindowCaptureFactory.ModeNames();

        [ObservableProperty] private string? _selectedMode = CaptureModeEnum.BitBlt.ToString();

        private MaskWindow? _maskWindow;
        private readonly ILogger<MainWindowViewModel> _logger = App.GetLogger<MainWindowViewModel>();

        private TaskDispatcher _taskDispatcher = new();


        [RelayCommand]
        private void OnLoaded()
        {
            //TestMask();
            //TestRect();
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
            TestMask();
            if (SelectedMode == null)
            { 
                MessageBox.Show("请选择捕获方式");
                return;
            }
            _taskDispatcher.Start(SelectedMode.ToCaptureMode());
        }

        [RelayCommand]
        private void OnStopTrigger()
        {
            if (SelectedMode == null)
            {
                MessageBox.Show("请选择捕获方式");
                return;
            }
            _taskDispatcher.Stop();
        }

        //private void TestRect()
        //{
        //    var hWnd = SystemControl.FindGenshinImpactHandle();
        //    if (hWnd == IntPtr.Zero)
        //    {
        //        MessageBox.Show("未找到原神窗口");
        //        return;
        //    }

        //    User32.GetWindowRect(hWnd, out var rect);
        //    //var x = rect.X;
        //    //var y = rect.Y;
        //    //var w = rect.Width;
        //    //var h = rect.Height;

        //    var x = (int)Math.Ceiling(rect.X * PrimaryScreen.ScaleX);
        //    var y = (int)Math.Ceiling(rect.Y * PrimaryScreen.ScaleY);
        //    var w = (int)Math.Ceiling(rect.Width * PrimaryScreen.ScaleX);
        //    var h = (int)Math.Ceiling(rect.Height * PrimaryScreen.ScaleY);
        //    Debug.WriteLine($"原神窗口大小：{rect.Width} x {rect.Height}");
        //    Debug.WriteLine($"原神窗口大小(计算DPI缩放后)：{w} x {h}");

        //    User32.GetClientRect(hWnd, out var clientRect);
        //    var cx = clientRect.X;
        //    var cy = clientRect.Y;
        //    var cw = clientRect.Width;
        //    var ch = clientRect.Height;

        //    Debug.WriteLine($"原神窗口内控件大小：{clientRect.Width} x {clientRect.Height}");


        //    var h2 = User32.GetSystemMetrics(User32.SystemMetric.SM_CYFRAME);
        //    var h3 = User32.GetSystemMetrics(User32.SystemMetric.SM_CYCAPTION);
        //    _logger.LogInformation($"标题栏高度: {h2}  {h3}");

        //}

        private void TestMask()
        {
            var hWnd = SystemControl.FindGenshinImpactHandle();
            if (hWnd == IntPtr.Zero)
            {
                MessageBox.Show("未找到原神窗口");
                return;
            }

            GetWindowRect((HWND)hWnd, out var rect);
            var x = rect.X;
            var y = rect.Y;
            var w = rect.Width;
            var h = rect.Height;

            //var x = (int)Math.Ceiling(rect.X * PrimaryScreen.ScaleX);
            //var y = (int)Math.Ceiling(rect.Y * PrimaryScreen.ScaleY);
            //var w = (int)Math.Ceiling(rect.Width * PrimaryScreen.ScaleX);
            //var h = (int)Math.Ceiling(rect.Height * PrimaryScreen.ScaleY);
            //Debug.WriteLine($"原神窗口大小：{rect.Width} x {rect.Height}");
            //Debug.WriteLine($"原神窗口大小(计算DPI缩放后)：{w} x {h}");

            //var x = 0;
            //var y = 0;
            //var w = 1200;
            //var h = 800;

            _maskWindow = MaskWindow.Instance();
            ////window.Owner = this;
            _maskWindow.Left = x;
            _maskWindow.Top = y;
            _maskWindow.Width = w;
            _maskWindow.Height = h;
            _maskWindow.Logger = App.GetLogger<MaskWindow>();
            _maskWindow.Show();

            _logger.LogInformation("Mask Window showed 遮罩窗口启动成功");
        }
    }
}