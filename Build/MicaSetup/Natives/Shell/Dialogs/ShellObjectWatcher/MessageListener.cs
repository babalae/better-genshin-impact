using MicaSetup.Natives;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using static MicaSetup.Natives.User32;

namespace MicaSetup.Shell.Dialogs;

#pragma warning disable CS8618

internal class MessageListener : IDisposable
{
    public const uint CreateWindowMessage = (uint)WindowMessage.WM_USER + 1;
    public const uint DestroyWindowMessage = (uint)WindowMessage.WM_USER + 2;
    public const uint BaseUserMessage = (uint)WindowMessage.WM_USER + 5;

    private const string MessageWindowClassName = "MessageListenerClass";

    private static readonly object _threadlock = new();
    private static uint _atom;
    private static Thread _windowThread = null!;
    private static volatile bool _running = false;

    private static readonly ShellObjectWatcherNativeMethods.WndProcDelegate wndProc = WndProc;
    private static readonly Dictionary<IntPtr, MessageListener> _listeners = new();
    private static nint _firstWindowHandle = 0;

    private static readonly object _crossThreadWindowLock = new();
    private static nint _tempHandle = 0;

    public event EventHandler<WindowMessageEventArgs> MessageReceived;

    public MessageListener()
    {
        lock (_threadlock)
        {
            if (_windowThread == null)
            {
                _windowThread = new Thread(ThreadMethod);
                _windowThread.SetApartmentState(ApartmentState.STA);
                _windowThread.Name = "ShellObjectWatcherMessageListenerHelperThread";

                lock (_crossThreadWindowLock)
                {
                    _windowThread.Start();
                    Monitor.Wait(_crossThreadWindowLock);
                }

                _firstWindowHandle = WindowHandle;
            }
            else
            {
                CrossThreadCreateWindow();
            }

            if (WindowHandle == 0)
            {
                throw new ShellException(LocalizedMessages.MessageListenerCannotCreateWindow,
                    Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
            }

            _listeners.Add(WindowHandle, this);
        }
    }

    private void CrossThreadCreateWindow()
    {
        if (_firstWindowHandle == 0)
        {
            throw new InvalidOperationException(LocalizedMessages.MessageListenerNoWindowHandle);
        }

        lock (_crossThreadWindowLock)
        {
            User32.PostMessage(_firstWindowHandle, (int)CreateWindowMessage, 0, 0);
            Monitor.Wait(_crossThreadWindowLock);
        }

        WindowHandle = _tempHandle;
    }

    private static void RegisterWindowClass()
    {
        WindowClassEx classEx = new()
        {
            ClassName = MessageWindowClassName,
            WndProc = wndProc,
            Size = (uint)Marshal.SizeOf(typeof(WindowClassEx)),
        };

        var atom = ShellObjectWatcherNativeMethods.RegisterClassEx(ref classEx);
        if (atom == 0)
        {
            throw new ShellException(LocalizedMessages.MessageListenerClassNotRegistered,
                Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
        }
        _atom = atom;
    }

    private static nint CreateWindow()
    {
        nint handle = ShellObjectWatcherNativeMethods.CreateWindowEx(
            0,
            MessageWindowClassName,
            "MessageListenerWindow",
            0,
            0, 0, 0, 0,
            new IntPtr(-3),
            0, 0, 0);

        return handle;
    }

    private void ThreadMethod()
    {
        lock (_crossThreadWindowLock)
        {
            _running = true;
            if (_atom == 0)
            {
                RegisterWindowClass();
            }
            WindowHandle = CreateWindow();

            Monitor.Pulse(_crossThreadWindowLock);
        }

        while (_running)
        {
            if (ShellObjectWatcherNativeMethods.GetMessage(out Message msg, 0, 0, 0))
            {
                ShellObjectWatcherNativeMethods.DispatchMessage(ref msg);
            }
        }
    }

    private static int WndProc(nint hwnd, uint msg, nint wparam, nint lparam)
    {
        switch (msg)
        {
            case CreateWindowMessage:
                lock (_crossThreadWindowLock)
                {
                    _tempHandle = CreateWindow();
                    Monitor.Pulse(_crossThreadWindowLock);
                }
                break;

            case (uint)WindowMessage.WM_DESTROY:
                _running = false;
                break;

            default:
                MessageListener listener;
                if (_listeners.TryGetValue(hwnd, out listener))
                {
                    Message message = new(hwnd, msg, wparam, lparam, 0, new POINT());
                    listener.MessageReceived.SafeRaise(listener, new WindowMessageEventArgs(message));
                }
                break;
        }

        return ShellObjectWatcherNativeMethods.DefWindowProc(hwnd, msg, wparam, lparam);
    }

    public nint WindowHandle { get; private set; }

    public static bool Running
    { get { return _running; } }

    ~MessageListener()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_threadlock)
            {
                _listeners.Remove(WindowHandle);
                if (_listeners.Count == 0)
                {
                    User32.PostMessage(WindowHandle, (int)WindowMessage.WM_DESTROY, 0, 0);
                }
            }
        }
    }
}

public class WindowMessageEventArgs : EventArgs
{
    public Message Message { get; private set; }

    internal WindowMessageEventArgs(Message msg) => Message = msg;
}
