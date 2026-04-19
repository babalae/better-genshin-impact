using System;
using System.Collections;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace GamepadController.Simulator
{
    /// <summary>
    /// 从Windows的Xbox控制器接收输入
    /// </summary>
    public class ControllerSimulator
    {
        private IXbox360Controller Xbox360Controller;
        public static int ButtonUp = Xbox360Button.Up.Id;
        public static int ButtonDown = Xbox360Button.Down.Id;
        public static int ButtonLeft = Xbox360Button.Left.Id;
        public static int ButtonRight = Xbox360Button.Right.Id;
        public static int ButtonStart = Xbox360Button.Start.Id;
        public static int ButtonBack = Xbox360Button.Back.Id;
        public static int ButtonLeftThumb = Xbox360Button.Left.Id;
        public static int ButtonRightThumb = Xbox360Button.Right.Id;
        public static int ButtonLeftShoulder = Xbox360Button.LeftShoulder.Id;
        public static int ButtonRightShoulder = Xbox360Button.RightShoulder.Id;
        public static int ButtonGuide = Xbox360Button.Guide.Id;
        public static int ButtonX = Xbox360Button.X.Id;
        public static int ButtonY = Xbox360Button.Y.Id;
        public static int ButtonA = Xbox360Button.A.Id;
        public static int ButtonB = Xbox360Button.B.Id;
        private static Hashtable ButtonMap = new Hashtable();

        private void InitButtons()
        {
            ButtonMap[ButtonUp] = Xbox360Button.Up;
            ButtonMap[ButtonDown] = Xbox360Button.Down;
            ButtonMap[ButtonLeft] = Xbox360Button.Left;
            ButtonMap[ButtonRight] = Xbox360Button.Right;
            ButtonMap[ButtonStart] = Xbox360Button.Start;
            ButtonMap[ButtonBack] = Xbox360Button.Back;
            ButtonMap[ButtonLeftThumb] = Xbox360Button.LeftThumb;
            ButtonMap[ButtonRightThumb] = Xbox360Button.RightThumb;
            ButtonMap[ButtonLeftShoulder] = Xbox360Button.LeftShoulder;
            ButtonMap[ButtonRightShoulder] = Xbox360Button.RightShoulder;
            ButtonMap[ButtonGuide] = Xbox360Button.Guide;
            ButtonMap[ButtonX] = Xbox360Button.X;
            ButtonMap[ButtonY] = Xbox360Button.Y;
            ButtonMap[ButtonA] = Xbox360Button.A;
            ButtonMap[ButtonB] = Xbox360Button.B;
        }

        public ControllerSimulator()
        {
            InitButtons();
            var viGEmClient = new ViGEmClient();
            Xbox360Controller = viGEmClient.CreateXbox360Controller();
            ConnectGamepad();
        }

        // 析构函数，断连虚拟手柄
        ~ControllerSimulator()
        {
            BreakOffGamepad();
        }

        private bool check()
        {
            if (!isConnected)
            {
                // Console.WriteLine($"controller not connected");
                return false;
            }

            if (Xbox360Controller == null)
            {
                // Console.WriteLine($"controller not init");
                return false;
            }

            return true;
        }

        public bool OnButtonPressed(int buttonId, int millisecondsPressDelay)
        {
            if (!check())
            {
                return false;
            }

            if (!ButtonMap.ContainsKey(buttonId))
            {
                return false;
            }

            Xbox360Button button = (Xbox360Button)ButtonMap[buttonId];
            Xbox360Controller.SetButtonState(button, true);
            Thread.Sleep(millisecondsPressDelay);
            Xbox360Controller.SetButtonState(button, false);

            return true;
        }

        /// <summary>
        /// 是否连接控制器
        /// </summary>
        public bool isConnected = false;

        /// <summary>
        /// 连接控制器
        /// </summary>
        /// <returns></returns>
        public bool ConnectGamepad()
        {
            try
            {
                if (!isConnected)
                {
                    Xbox360Controller.Connect();
                    isConnected = true;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }

            Console.WriteLine($"ConnectGamepad connect {isConnected}");
            return isConnected;
        }

        /// <summary>
        /// 断开控制器
        /// </summary>
        /// <returns></returns>
        public void BreakOffGamepad()
        {
            try
            {
                if (isConnected)
                {
                    isConnected = false;
                    Xbox360Controller.Disconnect();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }

            Console.WriteLine($"ConnectGamepad disconnect {isConnected}");
        }
    }
}