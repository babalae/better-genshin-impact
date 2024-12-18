// This code is distributed under MIT license. 
// Copyright (c) 2015 George Mamaladze
// See license.txt or https://mit-license.org/

using System.Runtime.InteropServices;

namespace Gma.System.MouseKeyHook.WinApi
{
    /// <summary>
    ///     The KeyboardHookStruct structure contains information about a low-level keyboard input event.
    /// </summary>
    /// <remarks>
    ///     http://msdn.microsoft.com/library/default.asp?url=/library/en-us/winui/winui/windowsuserinterface/windowing/hooks/hookreference/hookstructures/cwpstruct.asp
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardHookStruct
    {
        /// <summary>
        ///     Specifies a virtual-key code. The code must be a value in the range 1 to 254.
        /// </summary>
        public int VirtualKeyCode;

        /// <summary>
        ///     Specifies a hardware scan code for the key.
        /// </summary>
        public int ScanCode;

        /// <summary>
        ///     Specifies the extended-key flag, event-injected flag, context code, and transition-state flag.
        /// </summary>
        public int Flags;

        /// <summary>
        ///     Specifies the Time stamp for this message.
        /// </summary>
        public int Time;

        /// <summary>
        ///     Specifies extra information associated with the message.
        /// </summary>
        public int ExtraInfo;
    }
}