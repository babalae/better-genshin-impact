using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;
using Vanara.PInvoke;

namespace Vision.Recognition.Helper.Simulator
{
    public class PostMessageSimulator
    {
        public static readonly uint WM_LBUTTONDOWN = 0x201; //按下鼠标左键

        public static readonly uint WM_LBUTTONUP = 0x202; //释放鼠标左键

        private readonly IntPtr _hWnd;

        public PostMessageSimulator(IntPtr hWnd)
        {
            this._hWnd = hWnd;
        }

        /// <summary>
        /// 指定位置并按下左键
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        public void LeftButtonClick(int x, int y)
        {
            IntPtr p = (y << 16) | x;
            User32.PostMessage(_hWnd, WM_LBUTTONDOWN, IntPtr.Zero, p);
            Thread.Sleep(100);
            User32.PostMessage(_hWnd, WM_LBUTTONUP, IntPtr.Zero, p);
        }

        /// <summary>
        /// 默认位置左键按下
        /// </summary>
        public void LeftButtonDown()
        {
            User32.PostMessage(_hWnd, WM_LBUTTONDOWN, IntPtr.Zero);
        }

        /// <summary>
        /// 默认位置左键释放
        /// </summary>
        public void LeftButtonUp()
        {
            User32.PostMessage(_hWnd, WM_LBUTTONUP, IntPtr.Zero);
        }
    }
}