using BetterGenshinImpact.Utils.Extensions;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using Vision.WindowCapture;

namespace BetterGenshinImpact.View.Test
{
    /// <summary>
    /// CaptureTestWindow.xaml 的交互逻辑
    /// </summary>
    public partial class CaptureTestWindow : Window
    {
        private IWindowCapture? _capture;
        public CaptureTestWindow()
        {
            InitializeComponent();
        }

        public void StartCapture(IntPtr hWnd, CaptureMode captureMode)
        {
            if (hWnd == IntPtr.Zero)
            {
                MessageBox.Show("请选择窗口");
                return;
            }


            _capture = WindowCaptureFactory.Create(captureMode);
            _capture.Start(hWnd);

            CompositionTarget.Rendering += Loop;
        }

        private void Loop(object? sender, EventArgs e)
        {
            var sw = new Stopwatch();
            sw.Start();
            var bitmap = _capture?.Capture();
            sw.Stop();
            Debug.WriteLine("截图耗时:" + sw.ElapsedMilliseconds);

            if (bitmap != null)
            {
                sw.Reset();
                sw.Start();
                DisplayCaptureResultImage.Source = bitmap.ToBitmapImage();
                sw.Stop();
                Debug.WriteLine("转换耗时:" + sw.ElapsedMilliseconds);
            }
        }
    }
}
