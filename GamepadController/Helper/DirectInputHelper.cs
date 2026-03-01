using SharpDX.DirectInput;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GamepadController.Helper
{
    /// <summary>
    /// DirectInput是用于输入设备（包括鼠标，键盘，操纵杆和其他游戏控制器）
    /// https://docs.microsoft.com/en-us/previous-versions/windows/desktop/ee418273(v=vs.85)
    /// </summary>
    public class DirectInputHelper
    {
        /// <summary>
        /// 是否连接控制器
        /// </summary>
        public bool isGetJoystick = false;

        /// <summary>
        /// 连接到的控制器
        /// </summary>
        private Joystick curJoystick;

        /// <summary>
        /// 控制器状态捕获计时器
        /// </summary>
        private Timer _timer;

        /// <summary>
        /// 当前摇杆状态
        /// 用于判断两次摇杆差异
        /// </summary>
        private int[] RockerData;

        /// <summary>
        /// 当前按键状态
        /// 用于判断两次按键差异
        /// </summary>
        private bool[] ButtonData;

        /// <summary>
        /// 摇杆变化事件
        /// </summary>
        public event Action<int[]> RockerChange;

        /// <summary>
        /// 按钮变化事件
        /// </summary>
        public event Action<bool[]> ButtonChange;

        /// <summary>
        /// 连接控制器
        /// </summary>
        /// <returns></returns>
        public bool ConnectGamepad()
        {
            if (!isGetJoystick && _timer == null)
            {
                var vDirectInput = new DirectInput();
                var allDevices = vDirectInput.GetDevices();
                foreach (var item in allDevices)
                {
                    if (item.Type == DeviceType.Gamepad)
                    {
                        curJoystick = new Joystick(vDirectInput, item.InstanceGuid);
                        curJoystick.Acquire();
                        isGetJoystick = true;
                        _timer = new Timer(obj => Update());
                        _timer.Change(0, 1000 / 60);
                    }
                }
            }
            return isGetJoystick;
        }

        /// <summary>
        /// 断开控制器
        /// </summary>
        /// <returns></returns>
        public void BreakOffGamepad()
        {
            if (_timer != null)
            {
                _timer.Dispose();
                _timer = null;
            }
            if (isGetJoystick)
            {
                isGetJoystick = false;
            }
        }

        /// <summary>
        /// 捕获控制器数据
        /// </summary>
        private void Update()
        {
            try
            {
                var joys = curJoystick.GetCurrentState();
                // 摇杆
                if (RockerData == null || !Enumerable.SequenceEqual(RockerData, joys.PointOfViewControllers))
                {
                    RockerData = joys.PointOfViewControllers;
                    RockerChange?.Invoke(RockerData);
                }
                // 按钮
                if (ButtonData == null || !Enumerable.SequenceEqual(ButtonData, joys.Buttons))
                {
                    ButtonData = joys.Buttons;
                    ButtonChange?.Invoke(ButtonData);
                }
            }
            catch (Exception)
            {
                BreakOffGamepad();
            }
        }
    }
}
