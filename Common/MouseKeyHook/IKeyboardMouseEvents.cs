// This code is distributed under MIT license. 
// Copyright (c) 2015 George Mamaladze
// See license.txt or https://mit-license.org/

using System;

namespace Gma.System.MouseKeyHook
{
    /// <summary>
    ///     Provides keyboard and mouse events.
    /// </summary>
    public interface IKeyboardMouseEvents : IKeyboardEvents, IMouseEvents, IDisposable
    {
    }
}