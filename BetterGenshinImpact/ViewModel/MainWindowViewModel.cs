using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Vision.Recognition;
using Vision.WindowCapture;

namespace BetterGenshinImpact.ViewModel
{
    public partial class MainWindowViewModel : ObservableObject
    {
        [ObservableProperty] private string[] _modeNames = WindowCaptureFactory.ModeNames();

        [ObservableProperty] private string? _selectedMode;

        private MaskWindow? _maskWindow;
        private readonly ILogger<MainWindowViewModel> _logger = App.GetLogger<MainWindowViewModel>();


        [RelayCommand]
        private void OnLoaded()
        {
            TestMask();
        }

        [RelayCommand]
        private void OnClosed()
        {
            _maskWindow?.Close();
            Application.Current.Shutdown();
        }

        private void TestMask()
        {
            //var hWnd = FindGenshinImpactHandle();
            //if (hWnd == IntPtr.Zero)
            //{
            //    MessageBox.Show("未找到原神窗口");
            //    return;
            //}

            //User32.GetWindowRect(hWnd, out var rect);
            //var x = rect.X;
            //var y = rect.Y;
            //var w = rect.Width;
            //var h = rect.Height;

            //var x = (int)Math.Ceiling(rect.X * PrimaryScreen.ScaleX);
            //var y = (int)Math.Ceiling(rect.Y * PrimaryScreen.ScaleY);
            //var w = (int)Math.Ceiling(rect.Width * PrimaryScreen.ScaleX);
            //var h = (int)Math.Ceiling(rect.Height * PrimaryScreen.ScaleY);
            //Debug.WriteLine($"原神窗口大小：{rect.Width} x {rect.Height}");
            //Debug.WriteLine($"原神窗口大小(计算DPI缩放后)：{w} x {h}");

            var x = 0;
            var y = 0;
            var w = 1000;
            var h = 1000;

            _maskWindow = MaskWindow.Instance();
            ////window.Owner = this;
            _maskWindow.Left = x;
            _maskWindow.Top = y;
            _maskWindow.Width = w;
            _maskWindow.Height = h;

            _maskWindow.Show();

            _logger.LogInformation("Mask Window showed 遮罩窗口启动成功");
        }
    }
}