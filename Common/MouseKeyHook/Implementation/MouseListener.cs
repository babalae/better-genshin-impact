// This code is distributed under MIT license.
// Copyright (c) 2015 George Mamaladze
// See license.txt or https://mit-license.org/

using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Gma.System.MouseKeyHook.WinApi;

namespace Gma.System.MouseKeyHook.Implementation
{
    // Because it is a P/Invoke method, 'GetSystemMetrics(int)'
    // should be defined in a class named NativeMethods, SafeNativeMethods,
    // or UnsafeNativeMethods.
    // https://msdn.microsoft.com/en-us/library/windows/desktop/ms724385(v=vs.85).aspx
    internal static class NativeMethods
    {
        private const int SM_SWAPBUTTON = 23;
        private const int SM_CXDRAG = 68;
        private const int SM_CYDRAG = 69;
        private const int SM_CXDOUBLECLK = 36;
        private const int SM_CYDOUBLECLK = 37;

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        public static int GetSwapButtonThreshold()
        {
            return GetSystemMetrics(SM_SWAPBUTTON);
        }

        public static int GetXDragThreshold()
        {
            return GetSystemMetrics(SM_CXDRAG) / 2 + 1;
        }

        public static int GetYDragThreshold()
        {
            return GetSystemMetrics(SM_CYDRAG) / 2 + 1;
        }

        public static int GetXDoubleClickThreshold()
        {
            return GetSystemMetrics(SM_CXDOUBLECLK) / 2 + 1;
        }

        public static int GetYDoubleClickThreshold()
        {
            return GetSystemMetrics(SM_CYDOUBLECLK) / 2 + 1;
        }
    }

    internal abstract class MouseListener : BaseListener, IMouseEvents
    {
        private readonly ButtonSet m_DoubleDown;
        private readonly ButtonSet m_SingleDown;
        protected readonly Point m_UninitialisedPoint = new Point(-99999, -99999);
        private readonly int m_SwapButtonThreshold;
        private readonly int m_xDragThreshold;
        private readonly int m_yDragThreshold;
        private Point m_DragStartPosition;

        private bool m_IsDragging;

        private Point m_PreviousPosition;

        protected MouseListener(Subscribe subscribe)
            : base(subscribe)
        {
            m_SwapButtonThreshold = NativeMethods.GetSwapButtonThreshold();
            m_xDragThreshold = NativeMethods.GetXDragThreshold();
            m_yDragThreshold = NativeMethods.GetYDragThreshold();
            m_IsDragging = false;

            m_PreviousPosition = m_UninitialisedPoint;
            m_DragStartPosition = m_UninitialisedPoint;

            m_DoubleDown = new ButtonSet();
            m_SingleDown = new ButtonSet();
        }

        public event MouseEventHandler MouseMove;
        public event EventHandler<MouseEventExtArgs> MouseMoveExt;
        public event MouseEventHandler MouseClick;
        public event MouseEventHandler MouseDown;
        public event EventHandler<MouseEventExtArgs> MouseDownExt;
        public event MouseEventHandler MouseUp;
        public event EventHandler<MouseEventExtArgs> MouseUpExt;
        public event MouseEventHandler MouseWheel;
        public event EventHandler<MouseEventExtArgs> MouseWheelExt;
        public event MouseEventHandler MouseHWheel;
        public event EventHandler<MouseEventExtArgs> MouseHWheelExt;
        public event MouseEventHandler MouseDoubleClick;
        public event MouseEventHandler MouseDragStarted;
        public event EventHandler<MouseEventExtArgs> MouseDragStartedExt;
        public event MouseEventHandler MouseDragFinished;
        public event EventHandler<MouseEventExtArgs> MouseDragFinishedExt;

        protected override bool Callback(CallbackData data)
        {
            data.MSwapButton = m_SwapButtonThreshold;

            var e = GetEventArgs(data);

            if (e.IsMouseButtonDown)
                ProcessDown(ref e);

            if (e.IsMouseButtonUp)
                ProcessUp(ref e);

            if (e.WheelScrolled)
            {
                if (e.IsHorizontalWheel)
                    ProcessHWheel(ref e);
                else
                    ProcessWheel(ref e);
            }

            if (HasMoved(e.Point))
                ProcessMove(ref e);

            ProcessDrag(ref e);

            return !e.Handled;
        }

        protected abstract MouseEventExtArgs GetEventArgs(CallbackData data);

        protected virtual void ProcessWheel(ref MouseEventExtArgs e)
        {
            OnWheel(e);
            OnWheelExt(e);
        }

        protected virtual void ProcessHWheel(ref MouseEventExtArgs e)
        {
            OnHWheel(e);
            OnHWheelExt(e);
        }

        protected virtual void ProcessDown(ref MouseEventExtArgs e)
        {
            OnDown(e);
            OnDownExt(e);
            if (e.Handled)
                return;

            if (e.Clicks == 2)
                m_DoubleDown.Add(e.Button);

            if (e.Clicks == 1)
                m_SingleDown.Add(e.Button);
        }

        protected virtual void ProcessUp(ref MouseEventExtArgs e)
        {
            OnUp(e);
            OnUpExt(e);
            if (e.Handled)
                return;

            if (m_SingleDown.Contains(e.Button))
            {
                OnClick(e);
                m_SingleDown.Remove(e.Button);
            }

            if (m_DoubleDown.Contains(e.Button))
            {
                e = e.ToDoubleClickEventArgs();
                OnDoubleClick(e);
                m_DoubleDown.Remove(e.Button);
            }
        }

        private void ProcessMove(ref MouseEventExtArgs e)
        {
            m_PreviousPosition = e.Point;

            OnMove(e);
            OnMoveExt(e);
        }

        private void ProcessDrag(ref MouseEventExtArgs e)
        {
            if (m_SingleDown.Contains(MouseButtons.Left))
            {
                if (m_DragStartPosition.Equals(m_UninitialisedPoint))
                    m_DragStartPosition = e.Point;

                ProcessDragStarted(ref e);
            }
            else
            {
                m_DragStartPosition = m_UninitialisedPoint;
                ProcessDragFinished(ref e);
            }
        }

        private void ProcessDragStarted(ref MouseEventExtArgs e)
        {
            if (!m_IsDragging)
            {
                var isXDragging = Math.Abs(e.Point.X - m_DragStartPosition.X) > m_xDragThreshold;
                var isYDragging = Math.Abs(e.Point.Y - m_DragStartPosition.Y) > m_yDragThreshold;
                m_IsDragging = isXDragging || isYDragging;

                if (m_IsDragging)
                {
                    var dragArgs = new MouseEventExtArgs(e.Button, e.Clicks, m_DragStartPosition, e.Delta, e.Timestamp, e.IsMouseButtonDown, e.IsMouseButtonUp, e.IsHorizontalWheel);
                    OnDragStarted(dragArgs);
                    OnDragStartedExt(dragArgs);
                }
            }
        }

        private void ProcessDragFinished(ref MouseEventExtArgs e)
        {
            if (m_IsDragging)
            {
                OnDragFinished(e);
                OnDragFinishedExt(e);
                m_IsDragging = false;
            }
        }

        private bool HasMoved(Point actualPoint)
        {
            return m_PreviousPosition != actualPoint;
        }

        protected virtual void OnMove(MouseEventArgs e)
        {
            var handler = MouseMove;
            if (handler != null) handler(this, e);
        }

        protected virtual void OnMoveExt(MouseEventExtArgs e)
        {
            var handler = MouseMoveExt;
            if (handler != null) handler(this, e);
        }

        protected virtual void OnClick(MouseEventArgs e)
        {
            var handler = MouseClick;
            if (handler != null) handler(this, e);
        }

        protected virtual void OnDown(MouseEventArgs e)
        {
            var handler = MouseDown;
            if (handler != null) handler(this, e);
        }

        protected virtual void OnDownExt(MouseEventExtArgs e)
        {
            var handler = MouseDownExt;
            if (handler != null) handler(this, e);
        }

        protected virtual void OnUp(MouseEventArgs e)
        {
            var handler = MouseUp;
            if (handler != null) handler(this, e);
        }

        protected virtual void OnUpExt(MouseEventExtArgs e)
        {
            var handler = MouseUpExt;
            if (handler != null) handler(this, e);
        }

        protected virtual void OnWheel(MouseEventArgs e)
        {
            var handler = MouseWheel;
            if (handler != null) handler(this, e);
        }

        protected virtual void OnWheelExt(MouseEventExtArgs e)
        {
            var handler = MouseWheelExt;
            if (handler != null) handler(this, e);
        }

        protected virtual void OnHWheel(MouseEventArgs e)
        {
            var handler = MouseHWheel;
            if (handler != null) handler(this, e);
        }

        protected virtual void OnHWheelExt(MouseEventExtArgs e)
        {
            var handler = MouseHWheelExt;
            if (handler != null) handler(this, e);
        }

        protected virtual void OnDoubleClick(MouseEventArgs e)
        {
            var handler = MouseDoubleClick;
            if (handler != null) handler(this, e);
        }

        protected virtual void OnDragStarted(MouseEventArgs e)
        {
            var handler = MouseDragStarted;
            if (handler != null) handler(this, e);
        }

        protected virtual void OnDragStartedExt(MouseEventExtArgs e)
        {
            var handler = MouseDragStartedExt;
            if (handler != null) handler(this, e);
        }

        protected virtual void OnDragFinished(MouseEventArgs e)
        {
            var handler = MouseDragFinished;
            if (handler != null) handler(this, e);
        }

        protected virtual void OnDragFinishedExt(MouseEventExtArgs e)
        {
            var handler = MouseDragFinishedExt;
            if (handler != null) handler(this, e);
        }
    }
}
