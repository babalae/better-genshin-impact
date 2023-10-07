using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

namespace Vision.WindowCapture.Test
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void CaptureBtn_OnClick(object sender, RoutedEventArgs e)
        {
            var picker = new PickerWindow();
            var hWnd = picker.PickCaptureTarget(new WindowInteropHelper(this).Handle);
            if (hWnd != IntPtr.Zero)
            {
                var captureWindow = new CaptureTestWindow();
                captureWindow.StartCapture(hWnd, CaptureModeEnum.WindowsGraphicsCapture);
                captureWindow.Show();
            }
        }
    }
}
