using GamepadController.Helper;
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

namespace GamepadController.Views
{
    /// <summary>
    /// QuanbaQ1Controller.xaml 的交互逻辑
    /// </summary>
    public partial class QuanbaQ1Controller : Window
    {
        public QuanbaQ1Controller()
        {
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;
            this.MouseDown += MainWindow_MouseDown;
        }

        private DirectInputHelper directInputHelper;

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            directInputHelper = new DirectInputHelper();
            directInputHelper.RockerChange += DirectInputHelper_RockerChange;
            directInputHelper.ButtonChange += DirectInputHelper_ButtonChange;
            directInputHelper.ConnectGamepad();
        }

        /// <summary>
        /// 拖动窗体
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MainWindow_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        /// <summary>
        /// 摇杆状态改变时触发
        /// </summary>
        /// <param name="obj"></param>
        private void DirectInputHelper_RockerChange(int[] obj)
        {
            this.Dispatcher.Invoke(() =>
            {
                switch (obj[0])
                {
                    case 0:
                        RockerUp.IsChecked = true; break;
                    case 18000:
                        RockerDown.IsChecked = true; break;
                    case 27000:
                        RockerLeft.IsChecked = true; break;
                    case 9000:
                        RockerRight.IsChecked = true; break;
                    case 31500:
                        RockerLefttop.IsChecked = true; break;
                    case 4500:
                        RockerRighttop.IsChecked = true; break;
                    case 22500:
                        RockerLeftdown.IsChecked = true; break;
                    case 13500:
                        RockerRightdown.IsChecked = true; break;
                    default:
                        RockerCore.IsChecked = true; break;
                }
            });
        }

        /// <summary>
        /// 按钮状态改变时触发
        /// </summary>
        /// <param name="obj"></param>
        private void DirectInputHelper_ButtonChange(bool[] obj)
        {
            this.Dispatcher.Invoke(() =>
            {
                A.IsChecked = obj[1];
                B.IsChecked = obj[2];
                X.IsChecked = obj[0];
                Y.IsChecked = obj[3];
                R1.IsChecked = obj[5];
                R2.IsChecked = obj[7];
                L1.IsChecked = obj[4];
                L2.IsChecked = obj[6];
            });
        }
    }
}
