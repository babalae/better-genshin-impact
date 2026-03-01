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
    /// IceXboxkeyLRTB.xaml 的交互逻辑
    /// </summary>
    public partial class IceXboxkeyLRTB : UserControl
    {
        public IceXboxkeyLRTB()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Top 按键状态
        /// </summary>
        public bool TopKeyState
        {
            get { return (bool)GetValue(TopKeyStateProperty); }
            set { SetValue(TopKeyStateProperty, value); }
        }
        public static readonly DependencyProperty TopKeyStateProperty =
            DependencyProperty.Register("TopKeyState", typeof(bool), typeof(IceXboxkeyLRTB));

        /// <summary>
        /// Buttom 按键状态
        /// </summary>
        public bool ButtomKeyState
        {
            get { return (bool)GetValue(ButtomKeyStateProperty); }
            set { SetValue(ButtomKeyStateProperty, value); }
        }
        public static readonly DependencyProperty ButtomKeyStateProperty =
            DependencyProperty.Register("ButtomKeyState", typeof(bool), typeof(IceXboxkeyLRTB));

        /// <summary>
        /// Left 按键状态
        /// </summary>
        public bool LeftKeyState
        {
            get { return (bool)GetValue(LeftKeyStateProperty); }
            set { SetValue(LeftKeyStateProperty, value); }
        }
        public static readonly DependencyProperty LeftKeyStateProperty =
            DependencyProperty.Register("LeftKeyState", typeof(bool), typeof(IceXboxkeyLRTB));

        /// <summary>
        /// Right 按键状态
        /// </summary>
        public bool RightKeyState
        {
            get { return (bool)GetValue(RightKeyStateProperty); }
            set { SetValue(RightKeyStateProperty, value); }
        }
        public static readonly DependencyProperty RightKeyStateProperty =
            DependencyProperty.Register("RightKeyState", typeof(bool), typeof(IceXboxkeyLRTB));
    }
}
