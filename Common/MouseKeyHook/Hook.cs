// This code is distributed under MIT license. 
// Copyright (c) 2015 George Mamaladze
// See license.txt or https://mit-license.org/

using Gma.System.MouseKeyHook.Implementation;

namespace Gma.System.MouseKeyHook
{
    /// <summary>
    ///     This is the class to start with.
    /// </summary>
    public static class Hook
    {
        /// <summary>
        ///     Here you find all application wide events. Both mouse and keyboard.
        /// </summary>
        /// <returns>
        ///     Returned instance is used for event subscriptions.
        ///     You can refetch it (you will get the same instance anyway).
        /// </returns>
        public static IKeyboardMouseEvents AppEvents()
        {
            return new AppEventFacade();
        }

        /// <summary>
        ///     Here you find all application wide events. Both mouse and keyboard.
        /// </summary>
        /// <returns>
        ///     Returned instance is used for event subscriptions.
        ///     You can refetch it (you will get the same instance anyway).
        /// </returns>
        public static IKeyboardMouseEvents GlobalEvents()
        {
            return new GlobalEventFacade();
        }
    }
}