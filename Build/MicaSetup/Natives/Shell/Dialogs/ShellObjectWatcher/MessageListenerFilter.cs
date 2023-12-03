using System;
using System.Collections.Generic;
using System.Linq;

namespace MicaSetup.Shell.Dialogs;

internal static class MessageListenerFilter
{
    private static readonly object _registerLock = new();
    private static readonly List<RegisteredListener> _packages = new();

    public static MessageListenerFilterRegistrationResult Register(Action<WindowMessageEventArgs> callback)
    {
        lock (_registerLock)
        {
            uint message = 0;
            var package = _packages.FirstOrDefault(x => x.TryRegister(callback, out message));
            if (package == null)
            {
                package = new RegisteredListener();
                if (!package.TryRegister(callback, out message))
                {
                    throw new ShellException(LocalizedMessages.MessageListenerFilterUnableToRegister);
                }
                _packages.Add(package);
            }

            return new MessageListenerFilterRegistrationResult(
                package.Listener.WindowHandle,
                message);
        }
    }

    public static void Unregister(nint listenerHandle, uint message)
    {
        lock (_registerLock)
        {
            var package = _packages.FirstOrDefault(x => x.Listener.WindowHandle == listenerHandle);
            if (package == null || !package.Callbacks.Remove(message))
            {
                throw new ArgumentException(LocalizedMessages.MessageListenerFilterUnknownListenerHandle);
            }

            if (package.Callbacks.Count == 0)
            {
                package.Listener.Dispose();
                _packages.Remove(package);
            }
        }
    }

    private class RegisteredListener
    {
        public Dictionary<uint, Action<WindowMessageEventArgs>> Callbacks { get; private set; }

        public MessageListener Listener { get; private set; }

        public RegisteredListener()
        {
            Callbacks = new Dictionary<uint, Action<WindowMessageEventArgs>>();
            Listener = new MessageListener();
            Listener.MessageReceived += MessageReceived;
        }

        private void MessageReceived(object sender, WindowMessageEventArgs e)
        {
            if (Callbacks.TryGetValue(e.Message.Msg, out var action))
            {
                action(e);
            }
        }

        private uint _lastMessage = MessageListener.BaseUserMessage;

        public bool TryRegister(Action<WindowMessageEventArgs> callback, out uint message)
        {
            message = 0;
            if (Callbacks.Count < ushort.MaxValue - MessageListener.BaseUserMessage)
            {
                var i = _lastMessage + 1;
                while (i != _lastMessage)
                {
                    if (i > ushort.MaxValue) { i = MessageListener.BaseUserMessage; }

                    if (!Callbacks.ContainsKey(i))
                    {
                        _lastMessage = message = i;
                        Callbacks.Add(i, callback);
                        return true;
                    }
                    i++;
                }
            }
            return false;
        }
    }
}

internal class MessageListenerFilterRegistrationResult
{
    internal MessageListenerFilterRegistrationResult(nint handle, uint msg)
    {
        WindowHandle = handle;
        Message = msg;
    }

    public nint WindowHandle { get; private set; }

    public uint Message { get; private set; }
}
