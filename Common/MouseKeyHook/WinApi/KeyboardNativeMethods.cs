// This code is distributed under MIT license. 
// Copyright (c) 2015 George Mamaladze
// See license.txt or https://mit-license.org/

using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Gma.System.MouseKeyHook.Implementation;

namespace Gma.System.MouseKeyHook.WinApi
{
    internal static class KeyboardNativeMethods
    {
        //values from Winuser.h in Microsoft SDK.
        public const byte VK_SHIFT = 0x10;

        public const byte VK_CAPITAL = 0x14;
        public const byte VK_NUMLOCK = 0x90;
        public const byte VK_LSHIFT = 0xA0;
        public const byte VK_RSHIFT = 0xA1;
        public const byte VK_LCONTROL = 0xA2;
        public const byte VK_RCONTROL = 0xA3;
        public const byte VK_LMENU = 0xA4;
        public const byte VK_RMENU = 0xA5;
        public const byte VK_LWIN = 0x5B;
        public const byte VK_RWIN = 0x5C;
        public const byte VK_SCROLL = 0x91;

        public const byte VK_INSERT = 0x2D;

        //may be possible to use these aggregates instead of L and R separately (untested)
        public const byte VK_CONTROL = 0x11;

        public const byte VK_MENU = 0x12;

        public const byte VK_PACKET = 0xE7;

        //Used to pass Unicode characters as if they were keystrokes. The VK_PACKET key is the low word of a 32-bit Virtual Key value used for non-keyboard input methods
        private static int lastVirtualKeyCode;

        private static int lastScanCode;
        private static byte[] lastKeyState = new byte[255];
        private static bool lastIsDead;

        /// <summary>
        ///     Translates a virtual key to its character equivalent using the current keyboard layout without knowing the
        ///     scancode in advance.
        /// </summary>
        /// <param name="virtualKeyCode"></param>
        /// <param name="fuState"></param>
        /// <param name="chars"></param>
        /// <returns></returns>
        internal static void TryGetCharFromKeyboardState(int virtualKeyCode, int fuState, out char[] chars)
        {
            var dwhkl = GetActiveKeyboard();
            var scanCode = MapVirtualKeyEx(virtualKeyCode, (int) MapType.MAPVK_VK_TO_VSC, dwhkl);
            TryGetCharFromKeyboardState(virtualKeyCode, scanCode, fuState, dwhkl, out chars);
        }

        /// <summary>
        ///     Translates a virtual key to its character equivalent using the current keyboard layout
        /// </summary>
        /// <param name="virtualKeyCode"></param>
        /// <param name="scanCode"></param>
        /// <param name="fuState"></param>
        /// <param name="chars"></param>
        /// <returns></returns>
        internal static void TryGetCharFromKeyboardState(int virtualKeyCode, int scanCode, int fuState,
            out char[] chars)
        {
            var dwhkl = GetActiveKeyboard(); //get the active keyboard layout
            TryGetCharFromKeyboardState(virtualKeyCode, scanCode, fuState, dwhkl, out chars);
        }

        /// <summary>
        ///     Translates a virtual key to its character equivalent using a specified keyboard layout
        /// </summary>
        /// <param name="virtualKeyCode"></param>
        /// <param name="scanCode"></param>
        /// <param name="fuState"></param>
        /// <param name="dwhkl"></param>
        /// <param name="chars"></param>
        /// <returns></returns>
        internal static void TryGetCharFromKeyboardState(int virtualKeyCode, int scanCode, int fuState, IntPtr dwhkl,
            out char[] chars)
        {
            var pwszBuff = new StringBuilder(64);
            var keyboardState = KeyboardState.GetCurrent();
            var currentKeyboardState = keyboardState.GetNativeState();
            var isDead = false;

            if (keyboardState.IsDown(Keys.ShiftKey))
                currentKeyboardState[(byte) Keys.ShiftKey] = 0x80;

            if (keyboardState.IsToggled(Keys.CapsLock))
                currentKeyboardState[(byte) Keys.CapsLock] = 0x01;

            var relevantChars = ToUnicodeEx(virtualKeyCode, scanCode, currentKeyboardState, pwszBuff, pwszBuff.Capacity,
                fuState, dwhkl);

            switch (relevantChars)
            {
                case -1:
                    isDead = true;
                    ClearKeyboardBuffer(virtualKeyCode, scanCode, dwhkl);
                    chars = null;
                    break;

                case 0:
                    chars = null;
                    break;

                case 1:
                    if (pwszBuff.Length > 0) chars = new[] {pwszBuff[0]};
                    else chars = null;
                    break;

                // Two or more (only two of them is relevant)
                default:
                    if (pwszBuff.Length > 1) chars = new[] {pwszBuff[0], pwszBuff[1]};
                    else chars = new[] {pwszBuff[0]};
                    break;
            }

            if (lastVirtualKeyCode != 0 && lastIsDead)
            {
                if (chars != null)
                {
                    var sbTemp = new StringBuilder(5);
                    ToUnicodeEx(lastVirtualKeyCode, lastScanCode, lastKeyState, sbTemp, sbTemp.Capacity, 0, dwhkl);
                    lastIsDead = false;
                    lastVirtualKeyCode = 0;
                }

                return;
            }

            lastScanCode = scanCode;
            lastVirtualKeyCode = virtualKeyCode;
            lastIsDead = isDead;
            lastKeyState = (byte[]) currentKeyboardState.Clone();
        }


        private static void ClearKeyboardBuffer(int vk, int sc, IntPtr hkl)
        {
            var sb = new StringBuilder(10);

            int rc;
            do
            {
                var lpKeyStateNull = new byte[255];
                rc = ToUnicodeEx(vk, sc, lpKeyStateNull, sb, sb.Capacity, 0, hkl);
            } while (rc < 0);
        }

        /// <summary>
        ///     Gets the input locale identifier for the active application's thread.  Using this combined with the ToUnicodeEx and
        ///     MapVirtualKeyEx enables Windows to properly translate keys based on the keyboard layout designated for the
        ///     application.
        /// </summary>
        /// <returns>HKL</returns>
        private static IntPtr GetActiveKeyboard()
        {
            var hActiveWnd = ThreadNativeMethods.GetForegroundWindow(); //handle to focused window
            int dwProcessId;
            var hCurrentWnd = ThreadNativeMethods.GetWindowThreadProcessId(hActiveWnd, out dwProcessId);
            //thread of focused window
            return GetKeyboardLayout(hCurrentWnd); //get the layout identifier for the thread whose window is focused
        }

        /// <summary>
        ///     The ToAscii function translates the specified virtual-key code and keyboard
        ///     state to the corresponding character or characters. The function translates the code
        ///     using the input language and physical keyboard layout identified by the keyboard layout handle.
        /// </summary>
        /// <param name="uVirtKey">
        ///     [in] Specifies the virtual-key code to be translated.
        /// </param>
        /// <param name="uScanCode">
        ///     [in] Specifies the hardware scan code of the key to be translated.
        ///     The high-order bit of this value is set if the key is up (not pressed).
        /// </param>
        /// <param name="lpbKeyState">
        ///     [in] Pointer to a 256-byte array that contains the current keyboard state.
        ///     Each element (byte) in the array contains the state of one key.
        ///     If the high-order bit of a byte is set, the key is down (pressed).
        ///     The low bit, if set, indicates that the key is toggled on. In this function,
        ///     only the toggle bit of the CAPS LOCK key is relevant. The toggle state
        ///     of the NUM LOCK and SCROLL LOCK keys is ignored.
        /// </param>
        /// <param name="lpwTransKey">
        ///     [out] Pointer to the buffer that receives the translated character or characters.
        /// </param>
        /// <param name="fuState">
        ///     [in] Specifies whether a menu is active. This parameter must be 1 if a menu is active, or 0 otherwise.
        /// </param>
        /// <returns>
        ///     If the specified key is a dead key, the return value is negative. Otherwise, it is one of the following values.
        ///     Value Meaning
        ///     0 The specified virtual key has no translation for the current state of the keyboard.
        ///     1 One character was copied to the buffer.
        ///     2 Two characters were copied to the buffer. This usually happens when a dead-key character
        ///     (accent or diacritic) stored in the keyboard layout cannot be composed with the specified
        ///     virtual key to form a single character.
        /// </returns>
        /// <remarks>
        ///     http://msdn.microsoft.com/library/default.asp?url=/library/en-us/winui/winui/windowsuserinterface/userinput/keyboardinput/keyboardinputreference/keyboardinputfunctions/toascii.asp
        /// </remarks>
        [Obsolete("Use ToUnicodeEx instead")]
        [DllImport("user32.dll")]
        public static extern int ToAscii(
            int uVirtKey,
            int uScanCode,
            byte[] lpbKeyState,
            byte[] lpwTransKey,
            int fuState);

        /// <summary>
        ///     Translates the specified virtual-key code and keyboard state to the corresponding Unicode character or characters.
        /// </summary>
        /// <param name="wVirtKey">[in] The virtual-key code to be translated.</param>
        /// <param name="wScanCode">
        ///     [in] The hardware scan code of the key to be translated. The high-order bit of this value is
        ///     set if the key is up.
        /// </param>
        /// <param name="lpKeyState">
        ///     [in, optional] A pointer to a 256-byte array that contains the current keyboard state. Each
        ///     element (byte) in the array contains the state of one key. If the high-order bit of a byte is set, the key is down.
        /// </param>
        /// <param name="pwszBuff">
        ///     [out] The buffer that receives the translated Unicode character or characters. However, this
        ///     buffer may be returned without being null-terminated even though the variable name suggests that it is
        ///     null-terminated.
        /// </param>
        /// <param name="cchBuff">[in] The size, in characters, of the buffer pointed to by the pwszBuff parameter.</param>
        /// <param name="wFlags">
        ///     [in] The behavior of the function. If bit 0 is set, a menu is active. Bits 1 through 31 are
        ///     reserved.
        /// </param>
        /// <param name="dwhkl">The input locale identifier used to translate the specified code.</param>
        /// <returns>
        ///     -1 &lt;= return &lt;= n
        ///     <list type="bullet">
        ///         <item>
        ///             -1    = The specified virtual key is a dead-key character (accent or diacritic). This value is returned
        ///             regardless of the keyboard layout, even if several characters have been typed and are stored in the
        ///             keyboard state. If possible, even with Unicode keyboard layouts, the function has written a spacing version
        ///             of the dead-key character to the buffer specified by pwszBuff. For example, the function writes the
        ///             character SPACING ACUTE (0x00B4), rather than the character NON_SPACING ACUTE (0x0301).
        ///         </item>
        ///         <item>
        ///             0    = The specified virtual key has no translation for the current state of the keyboard. Nothing was
        ///             written to the buffer specified by pwszBuff.
        ///         </item>
        ///         <item> 1    = One character was written to the buffer specified by pwszBuff.</item>
        ///         <item>
        ///             n    = Two or more characters were written to the buffer specified by pwszBuff. The most common cause
        ///             for this is that a dead-key character (accent or diacritic) stored in the keyboard layout could not be
        ///             combined with the specified virtual key to form a single character. However, the buffer may contain more
        ///             characters than the return value specifies. When this happens, any extra characters are invalid and should
        ///             be ignored.
        ///         </item>
        ///     </list>
        /// </returns>
        [DllImport("user32.dll")]
        public static extern int ToUnicodeEx(int wVirtKey,
            int wScanCode,
            byte[] lpKeyState,
            [Out] [MarshalAs(UnmanagedType.LPWStr, SizeConst = 64)] StringBuilder pwszBuff,
            int cchBuff,
            int wFlags,
            IntPtr dwhkl);

        /// <summary>
        ///     The GetKeyboardState function copies the status of the 256 virtual keys to the
        ///     specified buffer.
        /// </summary>
        /// <param name="pbKeyState">
        ///     [in] Pointer to a 256-byte array that contains keyboard key states.
        /// </param>
        /// <returns>
        ///     If the function succeeds, the return value is nonzero.
        ///     If the function fails, the return value is zero. To get extended error information, call GetLastError.
        /// </returns>
        /// <remarks>
        ///     http://msdn.microsoft.com/library/default.asp?url=/library/en-us/winui/winui/windowsuserinterface/userinput/keyboardinput/keyboardinputreference/keyboardinputfunctions/toascii.asp
        /// </remarks>
        [DllImport("user32.dll")]
        public static extern int GetKeyboardState(byte[] pbKeyState);

        /// <summary>
        ///     The GetKeyState function retrieves the status of the specified virtual key. The status specifies whether the key is
        ///     up, down, or toggled
        ///     (on, off—alternating each time the key is pressed).
        /// </summary>
        /// <param name="vKey">
        ///     [in] Specifies a virtual key. If the desired virtual key is a letter or digit (A through Z, a through z, or 0
        ///     through 9), nVirtKey must be set to the ASCII value of that character. For other keys, it must be a virtual-key
        ///     code.
        /// </param>
        /// <returns>
        ///     The return value specifies the status of the specified virtual key, as follows:
        ///     If the high-order bit is 1, the key is down; otherwise, it is up.
        ///     If the low-order bit is 1, the key is toggled. A key, such as the CAPS LOCK key, is toggled if it is turned on. The
        ///     key is off and untoggled if the low-order bit is 0. A toggle key's indicator light (if any) on the keyboard will be
        ///     on when the key is toggled, and off when the key is untoggled.
        /// </returns>
        /// <remarks>http://msdn.microsoft.com/en-us/library/ms646301.aspx</remarks>
        [DllImport("user32.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.StdCall)]
        public static extern short GetKeyState(int vKey);

        /// <summary>
        ///     Translates (maps) a virtual-key code into a scan code or character value, or translates a scan code into a
        ///     virtual-key code.
        /// </summary>
        /// <param name="uCode">
        ///     [in] The virtual key code or scan code for a key. How this value is interpreted depends on the
        ///     value of the uMapType parameter.
        /// </param>
        /// <param name="uMapType">
        ///     [in] The translation to be performed. The value of this parameter depends on the value of the
        ///     uCode parameter.
        /// </param>
        /// <param name="dwhkl">[in] The input locale identifier used to translate the specified code.</param>
        /// <returns></returns>
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        internal static extern int MapVirtualKeyEx(int uCode, int uMapType, IntPtr dwhkl);

        /// <summary>
        ///     Retrieves the active input locale identifier (formerly called the keyboard layout) for the specified thread.
        ///     If the idThread parameter is zero, the input locale identifier for the active thread is returned.
        /// </summary>
        /// <param name="dwLayout">[in] The identifier of the thread to query, or 0 for the current thread. </param>
        /// <returns>
        ///     The return value is the input locale identifier for the thread. The low word contains a Language Identifier for the
        ///     input
        ///     language and the high word contains a device handle to the physical layout of the keyboard.
        /// </returns>
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        internal static extern IntPtr GetKeyboardLayout(int dwLayout);

        /// <summary>
        ///     MapVirtualKeys uMapType
        /// </summary>
        internal enum MapType
        {
            /// <summary>
            ///     uCode is a virtual-key code and is translated into an unshifted character value in the low-order word of the return
            ///     value. Dead keys (diacritics) are indicated by setting the top bit of the return value. If there is no translation,
            ///     the function returns 0.
            /// </summary>
            MAPVK_VK_TO_VSC,

            /// <summary>
            ///     uCode is a virtual-key code and is translated into a scan code. If it is a virtual-key code that does not
            ///     distinguish between left- and right-hand keys, the left-hand scan code is returned. If there is no translation, the
            ///     function returns 0.
            /// </summary>
            MAPVK_VSC_TO_VK,

            /// <summary>
            ///     uCode is a scan code and is translated into a virtual-key code that does not distinguish between left- and
            ///     right-hand keys. If there is no translation, the function returns 0.
            /// </summary>
            MAPVK_VK_TO_CHAR,

            /// <summary>
            ///     uCode is a scan code and is translated into a virtual-key code that distinguishes between left- and right-hand
            ///     keys. If there is no translation, the function returns 0.
            /// </summary>
            MAPVK_VSC_TO_VK_EX
        }
    }
}