// This code is distributed under MIT license. 
// Copyright (c) 2015 George Mamaladze
// See license.txt or https://mit-license.org/

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Gma.System.MouseKeyHook.Implementation;

namespace Gma.System.MouseKeyHook.WinApi
{
    internal static class HookHelper
    {
        private static HookProcedure _appHookProc;
        private static HookProcedure _globalHookProc;

        public static HookResult HookAppMouse(Callback callback)
        {
            return HookApp(HookIds.WH_MOUSE, callback);
        }

        public static HookResult HookAppKeyboard(Callback callback)
        {
            return HookApp(HookIds.WH_KEYBOARD, callback);
        }

        public static HookResult HookGlobalMouse(Callback callback)
        {
            return HookGlobal(HookIds.WH_MOUSE_LL, callback);
        }

        public static HookResult HookGlobalKeyboard(Callback callback)
        {
            return HookGlobal(HookIds.WH_KEYBOARD_LL, callback);
        }

        private static HookResult HookApp(int hookId, Callback callback)
        {
            _appHookProc = (code, param, lParam) => HookProcedure(code, param, lParam, callback);

            var hookHandle = HookNativeMethods.SetWindowsHookEx(
                hookId,
                _appHookProc,
                IntPtr.Zero,
                ThreadNativeMethods.GetCurrentThreadId());

            if (hookHandle.IsInvalid)
                ThrowLastUnmanagedErrorAsException();

            return new HookResult(hookHandle, _appHookProc);
        }

        private static HookResult HookGlobal(int hookId, Callback callback)
        {
            _globalHookProc = (code, param, lParam) => HookProcedure(code, param, lParam, callback);

            var hookHandle = HookNativeMethods.SetWindowsHookEx(
                hookId,
                _globalHookProc,
                Process.GetCurrentProcess().MainModule.BaseAddress,
                0);

            if (hookHandle.IsInvalid)
                ThrowLastUnmanagedErrorAsException();

            return new HookResult(hookHandle, _globalHookProc);
        }

        private static IntPtr HookProcedure(int nCode, IntPtr wParam, IntPtr lParam, Callback callback)
        {
            var passThrough = nCode != 0;
            if (passThrough)
                return CallNextHookEx(nCode, wParam, lParam);

            var callbackData = new CallbackData(wParam, lParam);
            var continueProcessing = callback(callbackData);

            if (!continueProcessing)
                return new IntPtr(-1);

            return CallNextHookEx(nCode, wParam, lParam);
        }

        private static IntPtr CallNextHookEx(int nCode, IntPtr wParam, IntPtr lParam)
        {
            return HookNativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        private static void ThrowLastUnmanagedErrorAsException()
        {
            var errorCode = Marshal.GetLastWin32Error();
            throw new Win32Exception(errorCode);
        }
    }
}