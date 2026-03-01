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
    /// XboxOneController.xaml 的交互逻辑
    /// </summary>
    public partial class XboxOneController : Window
    {
        
        public XboxOneController()
        {
            InitializeComponent();
            this.Loaded += XboxOneController_Loaded;
            this.MouseDown += XboxOneController_MouseDown; ;
        }

        private XInputHelper xInputHelper;

        private void XboxOneController_Loaded(object sender, RoutedEventArgs e)
        {
            xInputHelper = new XInputHelper();
            xInputHelper.ButtonsChange += XInputHelper_ButtonsChange;
            xInputHelper.LeftTriggerChange += XInputHelper_LeftTriggerChange;
            xInputHelper.RightTriggerChange += XInputHelper_RightTriggerChange;
            xInputHelper.LeftThumbXChange += XInputHelper_LeftThumbXChange;
            xInputHelper.LeftThumbYChange += XInputHelper_LeftThumbYChange;
            xInputHelper.RightThumbXChange += XInputHelper_RightThumbXChange;
            xInputHelper.RightThumbYChange += XInputHelper_RightThumbYChange;
            xInputHelper.ConnectGamepad();
            Console.WriteLine("XboxOneController_Loaded");
        }

        /// <summary>
        /// 拖动窗体
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void XboxOneController_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        /// <summary>
        /// 按键改变时触发
        /// </summary>
        /// <param name="obj"></param>
        private void XInputHelper_ButtonsChange(SharpDX.XInput.GamepadButtonFlags obj)
        {
            var vObjSplit = obj.ToString()?.Replace(" ", "")?.Split(',');
            this.Dispatcher.Invoke(new Action(() =>
            {
                IceXboxkeyABXY.XKeyState = vObjSplit.Contains($"{SharpDX.XInput.GamepadButtonFlags.X}");
                IceXboxkeyABXY.YKeyState = vObjSplit.Contains($"{SharpDX.XInput.GamepadButtonFlags.Y}");
                IceXboxkeyABXY.AKeyState = vObjSplit.Contains($"{SharpDX.XInput.GamepadButtonFlags.A}");
                IceXboxkeyABXY.BKeyState = vObjSplit.Contains($"{SharpDX.XInput.GamepadButtonFlags.B}");

                IceXboxkeyLRTB.TopKeyState = vObjSplit.Contains($"{SharpDX.XInput.GamepadButtonFlags.DPadUp}");
                IceXboxkeyLRTB.ButtomKeyState = vObjSplit.Contains($"{SharpDX.XInput.GamepadButtonFlags.DPadDown}");
                IceXboxkeyLRTB.LeftKeyState = vObjSplit.Contains($"{SharpDX.XInput.GamepadButtonFlags.DPadLeft}");
                IceXboxkeyLRTB.RightKeyState = vObjSplit.Contains($"{SharpDX.XInput.GamepadButtonFlags.DPadRight}");

                IceXboxMenu.BackKeyState = vObjSplit.Contains($"{SharpDX.XInput.GamepadButtonFlags.Back}");
                IceXboxMenu.StartKeyState = vObjSplit.Contains($"{SharpDX.XInput.GamepadButtonFlags.Start}");

                IceXboxLBRB.LBKeyState = vObjSplit.Contains($"{SharpDX.XInput.GamepadButtonFlags.LeftShoulder}");
                IceXboxLBRB.RBKeyState = vObjSplit.Contains($"{SharpDX.XInput.GamepadButtonFlags.RightShoulder}");

                IceXboxRockerLeft.ThumbKeyState = vObjSplit.Contains($"{SharpDX.XInput.GamepadButtonFlags.LeftThumb}");
                IceXboxRockerRight.ThumbKeyState = vObjSplit.Contains($"{SharpDX.XInput.GamepadButtonFlags.RightThumb}");
            }));
        }

        /// <summary>
        /// LT 按键变化事件
        /// </summary>
        /// <param name="obj"></param>
        private void XInputHelper_LeftTriggerChange(byte obj)
        {
            this.Dispatcher.Invoke(new Action(() =>
            {
                IceXboxLTRT.LTKeyState = obj;
            }));
        }

        /// <summary>
        /// RT 按键变化事件
        /// </summary>
        /// <param name="obj"></param>
        private void XInputHelper_RightTriggerChange(byte obj)
        {
            this.Dispatcher.Invoke(new Action(() =>
            {
                IceXboxLTRT.RTKeyState = obj;
            }));
        }

        /// <summary>
        /// 左摇杆 X 变化事件
        /// </summary>
        /// <param name="obj"></param>
        private void XInputHelper_LeftThumbXChange(short obj)
        {
            this.Dispatcher.Invoke(new Action(() =>
            {
                IceXboxRockerLeft.ThumbX = obj;
            }));
        }

        /// <summary>
        /// 左摇杆 Y 变化事件
        /// </summary>
        /// <param name="obj"></param>
        private void XInputHelper_LeftThumbYChange(short obj)
        {
            this.Dispatcher.Invoke(new Action(() =>
            {
                IceXboxRockerLeft.ThumbY = obj;
            }));
        }

        /// <summary>
        /// 右摇杆 X 变化事件
        /// </summary>
        /// <param name="obj"></param>
        private void XInputHelper_RightThumbXChange(short obj)
        {
            this.Dispatcher.Invoke(new Action(() =>
            {
                IceXboxRockerRight.ThumbX = obj;
            }));
        }

        /// <summary>
        /// 右摇杆 Y 变化事件
        /// </summary>
        /// <param name="obj"></param>
        private void XInputHelper_RightThumbYChange(short obj)
        {
            this.Dispatcher.Invoke(new Action(() =>
            {
                IceXboxRockerRight.ThumbY = obj;
            }));
        }
    }
}
