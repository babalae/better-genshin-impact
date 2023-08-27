using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Vanara.PInvoke;
using Vision.WindowCapture;
using Vision.WindowCapture.BitBlt;
using Vision.WindowCapture.GraphicsCapture;

namespace BetterGenshinImpact
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private IWindowCapture _capture;

        public MainWindow()
        {
            InitializeComponent();
        }

        public string[] ModeNames { get; } = WindowCaptureFactory.ModeNames();

        public string SelectedMode { get; set; }

        protected override void OnRender(DrawingContext drawingContext)
        {
            drawingContext.DrawRectangle(Brushes.Transparent, new Pen(Brushes.Red, 2), new Rect(20, 20, 250, 250));

            base.OnRender(drawingContext);
        }


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
    }
}