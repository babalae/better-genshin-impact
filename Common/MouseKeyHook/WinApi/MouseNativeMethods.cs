// This code is distributed under MIT license. 
// Copyright (c) 2015 George Mamaladze
// See license.txt or https://mit-license.org/

using System.Runtime.InteropServices;

namespace Gma.System.MouseKeyHook.WinApi
{
    internal static class MouseNativeMethods
    {
        /// <summary>
        ///     The GetDoubleClickTime function retrieves the current double-click time for the mouse. A double-click is a series
        ///     of two clicks of the
        ///     mouse button, the second occurring within a specified time after the first. The double-click time is the maximum
        ///     number of
        ///     milliseconds that may occur between the first and second click of a double-click.
        /// </summary>
        /// <returns>
        ///     The return value specifies the current double-click time, in milliseconds.
        /// </returns>
        /// <remarks>
        ///     http://msdn.microsoft.com/en-us/library/ms646258(VS.85).aspx
        /// </remarks>
        [DllImport("user32")]
        internal static extern int GetDoubleClickTime();
    }
}