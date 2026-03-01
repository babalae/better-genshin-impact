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
    /// IceXboxLTRT.xaml 的交互逻辑
    /// </summary>
    public partial class IceXboxLTRT : UserControl
    {
        public IceXboxLTRT()
        {
            InitializeComponent();
        }

        /// <summary>
        /// LT 按键状态
        /// </summary>
        public byte LTKeyState
        {
            get { return (byte)GetValue(LTKeyStateProperty); }
            set { SetValue(LTKeyStateProperty, value); }
        }
        public static readonly DependencyProperty LTKeyStateProperty =
            DependencyProperty.Register("LTKeyState", typeof(byte), typeof(IceXboxLTRT));

        /// <summary>
        /// RT 按键状态
        /// </summary>
        public byte RTKeyState
        {
            get { return (byte)GetValue(RTKeyStateProperty); }
            set { SetValue(RTKeyStateProperty, value); }
        }
        public static readonly DependencyProperty RTKeyStateProperty =
            DependencyProperty.Register("RTKeyState", typeof(byte), typeof(IceXboxLTRT));
    }
}
