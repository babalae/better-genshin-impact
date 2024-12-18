// This code is distributed under MIT license. 
// Copyright (c) 2015 George Mamaladze
// See license.txt or https://mit-license.org/

using System;

namespace Gma.System.MouseKeyHook.WinApi
{
    internal class HookResult : IDisposable
    {
        public HookResult(HookProcedureHandle handle, HookProcedure procedure)
        {
            Handle = handle;
            Procedure = procedure;
        }

        public HookProcedureHandle Handle { get; }

        public HookProcedure Procedure { get; }

        public void Dispose()
        {
            Handle.Dispose();
        }
    }
}