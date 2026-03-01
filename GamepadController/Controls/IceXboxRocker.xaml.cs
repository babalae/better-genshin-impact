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
    /// IceXboxRocker.xaml 的交互逻辑
    /// </summary>
    public partial class IceXboxRocker : UserControl
    {
        public IceXboxRocker()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 摇杆X
        /// </summary>
        public short ThumbX
        {
            get { return (short)GetValue(ThumbXProperty); }
            set { SetValue(ThumbXProperty, value); }
        }
        public static readonly DependencyProperty ThumbXProperty =
            DependencyProperty.Register("ThumbX", typeof(short), typeof(IceXboxRocker));

        /// <summary>
        /// 摇杆Y
        /// </summary>
        public short ThumbY
        {
            get { return (short)GetValue(ThumbYProperty); }
            set { SetValue(ThumbYProperty, value); }
        }
        public static readonly DependencyProperty ThumbYProperty =
            DependencyProperty.Register("ThumbY", typeof(short), typeof(IceXboxRocker));

        /// <summary>
        /// 摇杆按键
        /// </summary>
        public bool ThumbKeyState
        {
            get { return (bool)GetValue(ThumbKeyStateProperty); }
            set { SetValue(ThumbKeyStateProperty, value); }
        }
        public static readonly DependencyProperty ThumbKeyStateProperty =
            DependencyProperty.Register("ThumbKeyState", typeof(bool), typeof(IceXboxRocker));
    }
}
