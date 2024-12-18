// This code is distributed under MIT license. 
// Copyright (c) 2015 George Mamaladze
// See license.txt or https://mit-license.org/

using System.Runtime.InteropServices;

namespace Gma.System.MouseKeyHook.WinApi
{
    /// <summary>
    ///     The <see cref="MouseStruct" /> structure contains information about a mouse input event.
    /// </summary>
    /// <remarks>
    ///     See full documentation at http://globalmousekeyhook.codeplex.com/wikipage?title=MouseStruct
    /// </remarks>
    [StructLayout(LayoutKind.Explicit)]
    internal struct MouseStruct
    {
        /// <summary>
        ///     Specifies a Point structure that contains the X- and Y-coordinates of the cursor, in screen coordinates.
        /// </summary>
        [FieldOffset(0x00)] public Point Point;

        /// <summary>
        ///     Specifies information associated with the message.
        /// </summary>
        /// <remarks>
        ///     The possible values are:
        ///     <list type="bullet">
        ///         <item>
        ///             <description>0 - No Information</description>
        ///         </item>
        ///         <item>
        ///             <description>1 - X-Button1 Click</description>
        ///         </item>
        ///         <item>
        ///             <description>2 - X-Button2 Click</description>
        ///         </item>
        ///         <item>
        ///             <description>120 - Mouse Scroll Away from User</description>
        ///         </item>
        ///         <item>
        ///             <description>-120 - Mouse Scroll Toward User</description>
        ///         </item>
        ///     </list>
        /// </remarks>
        [FieldOffset(0x0A)] public short MouseData;

        /// <summary>
        ///     Returns a Timestamp associated with the input, in System Ticks.
        /// </summary>
        [FieldOffset(0x10)] public int Timestamp;
    }
}