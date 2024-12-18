// This code is distributed under MIT license. 
// Copyright (c) 2015 George Mamaladze
// See license.txt or https://mit-license.org/

using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Gma.System.MouseKeyHook.WinApi;

namespace Gma.System.MouseKeyHook
{
    /// <summary>
    ///     Provides extended data for the MouseClickExt and MouseMoveExt events.
    /// </summary>
    public class MouseEventExtArgs : MouseEventArgs
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="MouseEventExtArgs" /> class.
        /// </summary>
        /// <param name="buttons">One of the MouseButtons values indicating which mouse button was pressed.</param>
        /// <param name="clicks">The number of times a mouse button was pressed.</param>
        /// <param name="point">The x and y coordinate of a mouse click, in pixels.</param>
        /// <param name="delta">A signed count of the number of detents the wheel has rotated.</param>
        /// <param name="timestamp">The system tick count when the event occurred.</param>
        /// <param name="isMouseButtonDown">True if event signals mouse button down.</param>
        /// <param name="isMouseButtonUp">True if event signals mouse button up.</param>
        /// <param name="isHorizontalWheel">True if event signals horizontal wheel action.</param>
        internal MouseEventExtArgs(MouseButtons buttons, int clicks, Point point, int delta, int timestamp,
            bool isMouseButtonDown, bool isMouseButtonUp, bool isHorizontalWheel)
            : base(buttons, clicks, point.X, point.Y, delta)
        {
            IsMouseButtonDown = isMouseButtonDown;
            IsMouseButtonUp = isMouseButtonUp;
            IsHorizontalWheel = isHorizontalWheel;
            Timestamp = timestamp;
        }

        /// <summary>
        ///     Set this property to <b>true</b> inside your event handler to prevent further processing of the event in other
        ///     applications.
        /// </summary>
        public bool Handled { get; set; }

        /// <summary>
        ///     True if event contains information about wheel scroll.
        /// </summary>
        public bool WheelScrolled
        {
            get { return Delta != 0; }
        }

        /// <summary>
        ///     True if event signals a click. False if it was only a move or wheel scroll.
        /// </summary>
        public bool Clicked
        {
            get { return Clicks > 0; }
        }

        /// <summary>
        ///     True if event signals mouse button down.
        /// </summary>
        public bool IsMouseButtonDown { get; }

        /// <summary>
        ///     True if event signals mouse button up.
        /// </summary>
        public bool IsMouseButtonUp { get; }

        /// <summary>
        ///     True if event signals horizontal wheel action.
        /// </summary>
        public bool IsHorizontalWheel { get; }

        /// <summary>
        ///     The system tick count of when the event occurred.
        /// </summary>
        public int Timestamp { get; }

        /// <summary>
        /// </summary>
        internal Point Point
        {
            get { return new Point(X, Y); }
        }

        internal static MouseEventExtArgs FromRawDataApp(CallbackData data)
        {
            var wParam = data.WParam;
            var lParam = data.LParam;
            var mSwapButton = data.MSwapButton;

            var marshalledMouseStruct =
                (AppMouseStruct) Marshal.PtrToStructure(lParam, typeof(AppMouseStruct));
            return FromRawDataUniversal(wParam, marshalledMouseStruct.ToMouseStruct(), mSwapButton);
        }

        internal static MouseEventExtArgs FromRawDataGlobal(CallbackData data)
        {
            var wParam = data.WParam;
            var lParam = data.LParam;
            var mSwapButton = data.MSwapButton;

            var marshalledMouseStruct = (MouseStruct) Marshal.PtrToStructure(lParam, typeof(MouseStruct));
            return FromRawDataUniversal(wParam, marshalledMouseStruct, mSwapButton);
        }

        /// <summary>
        ///     Creates <see cref="MouseEventExtArgs" /> from relevant mouse data.
        /// </summary>
        /// <param name="wParam">First Windows Message parameter.</param>
        /// <param name="mouseInfo">A MouseStruct containing information from which to construct MouseEventExtArgs.</param>
        /// <returns>A new MouseEventExtArgs object.</returns>
        private static MouseEventExtArgs FromRawDataUniversal(IntPtr wParam, MouseStruct mouseInfo, int mSwapButton)
        {
            var button = MouseButtons.None;
            short mouseDelta = 0;
            var clickCount = 0;

            var isMouseButtonDown = false;
            var isMouseButtonUp = false;
            var isHorizontalWheel = false;


            switch ((long) wParam)
            {
                case Messages.WM_LBUTTONDOWN:
                    isMouseButtonDown = true;
                    button = MouseButtons.Left;
                    clickCount = 1;
                    break;
                case Messages.WM_LBUTTONUP:
                    isMouseButtonUp = true;
                    button = MouseButtons.Left;
                    clickCount = 1;
                    break;
                case Messages.WM_LBUTTONDBLCLK:
                    isMouseButtonDown = true;
                    button = MouseButtons.Left;
                    clickCount = 2;
                    break;
                case Messages.WM_RBUTTONDOWN:
                    isMouseButtonDown = true;
                    button = MouseButtons.Right;
                    clickCount = 1;
                    break;
                case Messages.WM_RBUTTONUP:
                    isMouseButtonUp = true;
                    button = MouseButtons.Right;
                    clickCount = 1;
                    break;
                case Messages.WM_RBUTTONDBLCLK:
                    isMouseButtonDown = true;
                    button = MouseButtons.Right;
                    clickCount = 2;
                    break;
                case Messages.WM_MBUTTONDOWN:
                    isMouseButtonDown = true;
                    button = MouseButtons.Middle;
                    clickCount = 1;
                    break;
                case Messages.WM_MBUTTONUP:
                    isMouseButtonUp = true;
                    button = MouseButtons.Middle;
                    clickCount = 1;
                    break;
                case Messages.WM_MBUTTONDBLCLK:
                    isMouseButtonDown = true;
                    button = MouseButtons.Middle;
                    clickCount = 2;
                    break;
                case Messages.WM_MOUSEWHEEL:
                    isHorizontalWheel = false;
                    mouseDelta = mouseInfo.MouseData;
                    break;
                case Messages.WM_MOUSEHWHEEL:
                    isHorizontalWheel = true;
                    mouseDelta = mouseInfo.MouseData;
                    break;
                case Messages.WM_XBUTTONDOWN:
                    button = mouseInfo.MouseData == 1
                        ? MouseButtons.XButton1
                        : MouseButtons.XButton2;
                    isMouseButtonDown = true;
                    clickCount = 1;
                    break;
                case Messages.WM_XBUTTONUP:
                    button = mouseInfo.MouseData == 1
                        ? MouseButtons.XButton1
                        : MouseButtons.XButton2;
                    isMouseButtonUp = true;
                    clickCount = 1;
                    break;
                case Messages.WM_XBUTTONDBLCLK:
                    isMouseButtonDown = true;
                    button = mouseInfo.MouseData == 1
                        ? MouseButtons.XButton1
                        : MouseButtons.XButton2;
                    clickCount = 2;
                    break;
            }

            if (mSwapButton > 0)
            {
                button = button == MouseButtons.Left ? MouseButtons.Right : button == MouseButtons.Right ? MouseButtons.Left : button;
            }

            var e = new MouseEventExtArgs(
                button,
                clickCount,
                mouseInfo.Point,
                mouseDelta,
                mouseInfo.Timestamp,
                isMouseButtonDown,
                isMouseButtonUp,
                isHorizontalWheel);

            return e;
        }

        internal MouseEventExtArgs ToDoubleClickEventArgs()
        {
            return new MouseEventExtArgs(Button, 2, Point, Delta, Timestamp, IsMouseButtonDown, IsMouseButtonUp, IsHorizontalWheel);
        }
    }
}