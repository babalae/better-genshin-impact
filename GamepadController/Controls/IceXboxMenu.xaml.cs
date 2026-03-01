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
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace GamepadController.Controls
{
    /// <summary>
    /// IceXboxMenu.xaml 的交互逻辑
    /// </summary>
    public partial class IceXboxMenu : UserControl
    {
        public IceXboxMenu()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Back 按键状态
        /// </summary>
        public bool BackKeyState
        {
            get { return (bool)GetValue(BackKeyStateProperty); }
            set { SetValue(BackKeyStateProperty, value); }
        }
        public static readonly DependencyProperty BackKeyStateProperty =
            DependencyProperty.Register("BackKeyState", typeof(bool), typeof(IceXboxMenu));

        /// <summary>
        /// Start 按键状态
        /// </summary>
        public bool StartKeyState
        {
            get { return (bool)GetValue(StartKeyStateProperty); }
            set { SetValue(StartKeyStateProperty, value); }
        }
        public static readonly DependencyProperty StartKeyStateProperty =
            DependencyProperty.Register("StartKeyState", typeof(bool), typeof(IceXboxMenu));
    }
}
