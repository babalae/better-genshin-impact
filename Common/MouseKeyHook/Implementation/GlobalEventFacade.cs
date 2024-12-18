// This code is distributed under MIT license. 
// Copyright (c) 2015 George Mamaladze
// See license.txt or https://mit-license.org/

namespace Gma.System.MouseKeyHook.Implementation
{
    internal class GlobalEventFacade : EventFacade
    {
        protected override MouseListener CreateMouseListener()
        {
            return new GlobalMouseListener();
        }

        protected override KeyListener CreateKeyListener()
        {
            return new GlobalKeyListener();
        }
    }
}