using System;

namespace MicaSetup.Shell.Dialogs;

public static class EventHandlerExtensionMethods
{
    public static void SafeRaise(this EventHandler eventHandler, object sender)
    {
        eventHandler?.Invoke(sender, EventArgs.Empty);
    }

    public static void SafeRaise<T>(this EventHandler<T> eventHandler, object sender, T args) where T : EventArgs
    {
        eventHandler?.Invoke(sender, args);
    }

    public static void SafeRaise(this EventHandler<EventArgs> eventHandler, object sender) => SafeRaise(eventHandler, sender, EventArgs.Empty);
}
