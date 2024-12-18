// This code is distributed under MIT license. 
// Copyright (c) 2015 George Mamaladze
// See license.txt or https://mit-license.org/

using System;

namespace Gma.System.MouseKeyHook.WinApi
{
    internal struct CallbackData
    {
        public CallbackData(IntPtr wParam, IntPtr lParam, int mSwapButton = 0)
        {
            WParam = wParam;
            LParam = lParam;
            MSwapButton = mSwapButton;
        }

        public IntPtr WParam { get; }

        public IntPtr LParam { get; }

        public int MSwapButton{ get; set; }
    }
}