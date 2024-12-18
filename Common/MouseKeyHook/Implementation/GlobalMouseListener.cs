// This code is distributed under MIT license. 
// Copyright (c) 2015 George Mamaladze
// See license.txt or https://mit-license.org/

using System;
using System.Windows.Forms;
using Gma.System.MouseKeyHook.WinApi;

namespace Gma.System.MouseKeyHook.Implementation
{
    internal class GlobalMouseListener : MouseListener
    {
        private readonly int m_SystemDoubleClickTime;
        private readonly int m_xDoubleClickThreshold;
        private readonly int m_yDoubleClickThreshold;
        private MouseButtons m_PreviousClicked;
        private Point m_PreviousClickedPosition;
        private int m_PreviousClickedTime;

        public GlobalMouseListener()
            : base(HookHelper.HookGlobalMouse)
        {
            m_SystemDoubleClickTime = MouseNativeMethods.GetDoubleClickTime();
            m_xDoubleClickThreshold = NativeMethods.GetXDoubleClickThreshold();
            m_yDoubleClickThreshold = NativeMethods.GetYDoubleClickThreshold();
        }

        protected override void ProcessDown(ref MouseEventExtArgs e)
        {
            if (IsDoubleClick(e))
                e = e.ToDoubleClickEventArgs();
            else
                StartDoubleClickWaiting(e);
            base.ProcessDown(ref e);
        }

        protected override void ProcessUp(ref MouseEventExtArgs e)
        {
            base.ProcessUp(ref e);
            if (e.Clicks == 2)
                StopDoubleClickWaiting();
        }

        private void StartDoubleClickWaiting(MouseEventExtArgs e)
        {
            m_PreviousClicked = e.Button;
            m_PreviousClickedTime = e.Timestamp;
            m_PreviousClickedPosition = e.Point;
        }

        private void StopDoubleClickWaiting()
        {
            m_PreviousClicked = MouseButtons.None;
            m_PreviousClickedTime = 0;
            m_PreviousClickedPosition = m_UninitialisedPoint;
        }

        private bool IsDoubleClick(MouseEventExtArgs e)
        {
            var isXMoving = Math.Abs(e.Point.X - m_PreviousClickedPosition.X) > m_xDoubleClickThreshold;
            var isYMoving = Math.Abs(e.Point.Y - m_PreviousClickedPosition.Y) > m_yDoubleClickThreshold;

            return
                e.Button == m_PreviousClicked &&
                !isXMoving &&
                !isYMoving &&
                e.Timestamp - m_PreviousClickedTime <= m_SystemDoubleClickTime;
        }

        protected override MouseEventExtArgs GetEventArgs(CallbackData data)
        {
            return MouseEventExtArgs.FromRawDataGlobal(data);
        }
    }
}
