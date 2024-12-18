// This code is distributed under MIT license. 
// Copyright (c) 2015 George Mamaladze
// See license.txt or https://mit-license.org/

namespace Gma.System.MouseKeyHook.WinApi
{
    internal static class Messages
    {
        //values from Winuser.h in Microsoft SDK.

        /// <summary>
        ///     The WM_MOUSEMOVE message is posted to a window when the cursor moves.
        /// </summary>
        public const int WM_MOUSEMOVE = 0x200;

        /// <summary>
        ///     The WM_LBUTTONDOWN message is posted when the user presses the left mouse button
        /// </summary>
        public const int WM_LBUTTONDOWN = 0x201;

        /// <summary>
        ///     The WM_RBUTTONDOWN message is posted when the user presses the right mouse button
        /// </summary>
        public const int WM_RBUTTONDOWN = 0x204;

        /// <summary>
        ///     The WM_MBUTTONDOWN message is posted when the user presses the middle mouse button
        /// </summary>
        public const int WM_MBUTTONDOWN = 0x207;

        /// <summary>
        ///     The WM_LBUTTONUP message is posted when the user releases the left mouse button
        /// </summary>
        public const int WM_LBUTTONUP = 0x202;

        /// <summary>
        ///     The WM_RBUTTONUP message is posted when the user releases the right mouse button
        /// </summary>
        public const int WM_RBUTTONUP = 0x205;

        /// <summary>
        ///     The WM_MBUTTONUP message is posted when the user releases the middle mouse button
        /// </summary>
        public const int WM_MBUTTONUP = 0x208;

        /// <summary>
        ///     The WM_LBUTTONDBLCLK message is posted when the user double-clicks the left mouse button
        /// </summary>
        public const int WM_LBUTTONDBLCLK = 0x203;

        /// <summary>
        ///     The WM_RBUTTONDBLCLK message is posted when the user double-clicks the right mouse button
        /// </summary>
        public const int WM_RBUTTONDBLCLK = 0x206;

        /// <summary>
        ///     The WM_RBUTTONDOWN message is posted when the user presses the right mouse button
        /// </summary>
        public const int WM_MBUTTONDBLCLK = 0x209;

        /// <summary>
        ///     The WM_MOUSEWHEEL message is posted when the user presses the mouse wheel.
        /// </summary>
        public const int WM_MOUSEWHEEL = 0x020A;

        /// <summary>
        ///     The WM_XBUTTONDOWN message is posted when the user presses the first or second X mouse
        ///     button.
        /// </summary>
        public const int WM_XBUTTONDOWN = 0x20B;

        /// <summary>
        ///     The WM_XBUTTONUP message is posted when the user releases the first or second X  mouse
        ///     button.
        /// </summary>
        public const int WM_XBUTTONUP = 0x20C;

        /// <summary>
        ///     The WM_XBUTTONDBLCLK message is posted when the user double-clicks the first or second
        ///     X mouse button.
        /// </summary>
        /// <remarks>Only windows that have the CS_DBLCLKS style can receive WM_XBUTTONDBLCLK messages.</remarks>
        public const int WM_XBUTTONDBLCLK = 0x20D;

        /// <summary>
        ///     The WM_MOUSEHWHEEL message Sent to the active window when the mouse's horizontal scroll
        ///     wheel is tilted or rotated.
        /// </summary>
        public const int WM_MOUSEHWHEEL = 0x20E;

        /// <summary>
        ///     The WM_KEYDOWN message is posted to the window with the keyboard focus when a non-system
        ///     key is pressed. A non-system key is a key that is pressed when the ALT key is not pressed.
        /// </summary>
        public const int WM_KEYDOWN = 0x100;

        /// <summary>
        ///     The WM_KEYUP message is posted to the window with the keyboard focus when a non-system
        ///     key is released. A non-system key is a key that is pressed when the ALT key is not pressed,
        ///     or a keyboard key that is pressed when a window has the keyboard focus.
        /// </summary>
        public const int WM_KEYUP = 0x101;

        /// <summary>
        ///     The WM_SYSKEYDOWN message is posted to the window with the keyboard focus when the user
        ///     presses the F10 key (which activates the menu bar) or holds down the ALT key and then
        ///     presses another key. It also occurs when no window currently has the keyboard focus;
        ///     in this case, the WM_SYSKEYDOWN message is sent to the active window. The window that
        ///     receives the message can distinguish between these two contexts by checking the context
        ///     code in the lParam parameter.
        /// </summary>
        public const int WM_SYSKEYDOWN = 0x104;

        /// <summary>
        ///     The WM_SYSKEYUP message is posted to the window with the keyboard focus when the user
        ///     releases a key that was pressed while the ALT key was held down. It also occurs when no
        ///     window currently has the keyboard focus; in this case, the WM_SYSKEYUP message is sent
        ///     to the active window. The window that receives the message can distinguish between
        ///     these two contexts by checking the context code in the lParam parameter.
        /// </summary>
        public const int WM_SYSKEYUP = 0x105;
    }
}