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
    /// IceXboxLBRB.xaml 的交互逻辑
    /// </summary>
    public partial class IceXboxLBRB : UserControl
    {
        public IceXboxLBRB()
        {
            InitializeComponent();
        }

        /// <summary>
        /// LB 按键状态
        /// </summary>
        public bool LBKeyState
        {
            get { return (bool)GetValue(LBKeyStateProperty); }
            set { SetValue(LBKeyStateProperty, value); }
        }
        public static readonly DependencyProperty LBKeyStateProperty =
            DependencyProperty.Register("LBKeyState", typeof(bool), typeof(IceXboxLBRB));

        /// <summary>
        /// RB 按键状态
        /// </summary>
        public bool RBKeyState
        {
            get { return (bool)GetValue(RBKeyStateProperty); }
            set { SetValue(RBKeyStateProperty, value); }
        }
        public static readonly DependencyProperty RBKeyStateProperty =
            DependencyProperty.Register("RBKeyState", typeof(bool), typeof(IceXboxLBRB));
    }
}
