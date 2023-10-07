using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Win32.Foundation;

namespace Vision.WindowCapture.Test
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

        public async void StartCapture(IntPtr hWnd, CaptureModeEnum captureMode)
        {
            if (hWnd == IntPtr.Zero)
            {
                MessageBox.Show("请选择窗口");
                return;
            }


            _capture = WindowCaptureFactory.Create(captureMode);
            await _capture.StartAsync((HWND)hWnd);

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
                DisplayCaptureResultImage.Source = ToBitmapImage(bitmap);
                sw.Stop();
                Debug.WriteLine("转换耗时:" + sw.ElapsedMilliseconds);
            }
        }

        public static BitmapImage ToBitmapImage( Bitmap bitmap)
        {
            var ms = new MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
            var image = new BitmapImage();
            image.BeginInit();
            ms.Seek(0, SeekOrigin.Begin);
            image.StreamSource = ms;
            image.EndInit();
            return image;
        }
    }

}
