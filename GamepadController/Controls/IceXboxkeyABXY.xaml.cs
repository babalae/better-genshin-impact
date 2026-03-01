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
    /// IceXboxkeyABXY.xaml 的交互逻辑
    /// </summary>
    public partial class IceXboxkeyABXY : UserControl
    {
        public IceXboxkeyABXY()
        {
            InitializeComponent();
        }

        /// <summary>
        /// X 按键状态
        /// </summary>
        public bool XKeyState
        {
            get { return (bool)GetValue(XKeyStateProperty); }
            set { SetValue(XKeyStateProperty, value); }
        }
        public static readonly DependencyProperty XKeyStateProperty =
            DependencyProperty.Register("XKeyState", typeof(bool), typeof(IceXboxkeyABXY));

        /// <summary>
        /// Y 按键状态
        /// </summary>
        public bool YKeyState
        {
            get { return (bool)GetValue(YKeyStateProperty); }
            set { SetValue(YKeyStateProperty, value); }
        }
        public static readonly DependencyProperty YKeyStateProperty =
            DependencyProperty.Register("YKeyState", typeof(bool), typeof(IceXboxkeyABXY));

        /// <summary>
        /// A 按键状态
        /// </summary>
        public bool AKeyState
        {
            get { return (bool)GetValue(AKeyStateProperty); }
            set { SetValue(AKeyStateProperty, value); }
        }
        public static readonly DependencyProperty AKeyStateProperty =
            DependencyProperty.Register("AKeyState", typeof(bool), typeof(IceXboxkeyABXY));

        /// <summary>
        /// B 按键状态
        /// </summary>
        public bool BKeyState
        {
            get { return (bool)GetValue(BKeyStateProperty); }
            set { SetValue(BKeyStateProperty, value); }
        }
        public static readonly DependencyProperty BKeyStateProperty =
            DependencyProperty.Register("BKeyState", typeof(bool), typeof(IceXboxkeyABXY));
    }
}
