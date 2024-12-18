// This code is distributed under MIT license. 
// Copyright (c) 2015 George Mamaladze
// See license.txt or https://mit-license.org/

using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

namespace Gma.System.MouseKeyHook.WinApi
{
    internal class HookProcedureHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        //private static bool _closing;

        static HookProcedureHandle()
        {
            //Application.ApplicationExit += (sender, e) => { HookProcedureHandle._closing = true; };
        }

        public HookProcedureHandle()
            : base(true)
        {
        }

        protected override bool ReleaseHandle()
        {
            //NOTE Calling Unhook during processexit causes deley
            var ret = HookNativeMethods.UnhookWindowsHookEx(handle);
            if (ret != 0)
            {
                base.Dispose();
                return true;
            }
            else
                return true;
        }
    }
}