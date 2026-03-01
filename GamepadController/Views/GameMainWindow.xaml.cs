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
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
// using Wpf.Ui;
// using Wpf.Ui.Abstractions;
// using Wpf.Ui.Controls;
// using Wpf.Ui.Tray.Controls;

namespace GamepadController.Views
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    // public partial class GameMainWindow : FluentWindow, INavigationWindow
    public partial class GameMainWindow : Window
    {
        public GameMainWindow()
        {
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            QuanbaQ1.Background = new ImageBrush(new BitmapImage(new Uri(@"pack://application:,,,/Images/QuanbaQ1.jpg")));
            XboxOne.Background = new ImageBrush(new BitmapImage(new Uri(@"pack://application:,,,/Images/XboxOne.jpg")));
        }

        /// <summary>
        /// 拳霸Q1摇杆
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void QuanbaQ1_Click(object sender, RoutedEventArgs e)
        {
            QuanbaQ1Controller window = new QuanbaQ1Controller();
            window.Show();
        }

        /// <summary>
        /// XboxOne手柄
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void XboxOne_Click(object sender, RoutedEventArgs e)
        {
            XboxOneController window = new XboxOneController();
            window.Show();
        }
    }
}
