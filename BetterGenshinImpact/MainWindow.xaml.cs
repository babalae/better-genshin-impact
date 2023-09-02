using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using BetterGenshinImpact.Extensions;
using BetterGenshinImpact.Utils;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Serilog;
using Vanara.PInvoke;
using Vision.WindowCapture;
using Vision.WindowCapture.BitBlt;
using Vision.WindowCapture.GraphicsCapture;
using Path = System.IO.Path;

namespace BetterGenshinImpact
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : System.Windows.Window
    {

        private readonly ILogger<MainWindow> _logger = App.GetLogger<MainWindow>();

        private MaskWindow _maskWindow;
        private IWindowCapture _capture;

        public MainWindow()
        {
            InitializeComponent();
        }

        public string[] ModeNames { get; } = WindowCaptureFactory.ModeNames();

        public string SelectedMode { get; set; }


        private void Button_Click(object sender, RoutedEventArgs e)
        {
            IntPtr hWnd = FindGenshinImpactHandle();
            if (hWnd == IntPtr.Zero)
            {
                MessageBox.Show("未找到原神窗口");
                return;
            }


            _capture = WindowCaptureFactory.Create(SelectedMode.ToCaptureMode());
            _capture.Start(hWnd);

            CompositionTarget.Rendering += Loop;

        }

        private void Loop(object? sender, EventArgs e)
        {
            var sw = new Stopwatch();
            sw.Start();
            var bitmap = _capture.Capture();
            sw.Stop();
            Debug.WriteLine("截图耗时:" + sw.ElapsedMilliseconds);

            if (bitmap != null)
            {
                sw.Reset();
                sw.Start();
                ImageResult.Source = bitmap.ToBitmapImage();
                sw.Stop();
                Debug.WriteLine("转换耗时:" + sw.ElapsedMilliseconds);
            }
        }

        public IntPtr FindGenshinImpactHandle()
        {
            var pros = Process.GetProcessesByName("YuanShen");
            if (pros.Any())
            {
                return pros[0].MainWindowHandle;
            }

            pros = Process.GetProcessesByName("GenshinImpact");
            if (pros.Any())
            {
                return pros[0].MainWindowHandle;
            }

            return IntPtr.Zero;
        }

        private void StopBtn_Click(object sender, RoutedEventArgs e)
        {
            _capture.Stop();
        }

        private void TestMaskBtn_Click(object sender, RoutedEventArgs e)
        {
            var hWnd = FindGenshinImpactHandle();
            if (hWnd == IntPtr.Zero)
            {
                MessageBox.Show("未找到原神窗口");
                return;
            }

            User32.GetWindowRect(hWnd, out var rect);
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

            _maskWindow =  MaskWindow.Instance();
            ////window.Owner = this;
            _maskWindow.Left = x;
            _maskWindow.Top = y;
            _maskWindow.Width = w;
            _maskWindow.Height = h;

            _maskWindow.Show();

            _logger.LogInformation("Mask Window showed 遮罩窗口启动成功");
            _logger.LogInformation("123");
        }

        private void MainWindow_OnClosed(object? sender, EventArgs e)
        {
            _maskWindow.Close();
        }
    }
}