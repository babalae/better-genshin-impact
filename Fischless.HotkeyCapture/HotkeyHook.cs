using System.Runtime.InteropServices;
using Vanara.PInvoke;

namespace Fischless.HotkeyCapture;

public sealed class HotkeyHook : IDisposable
{
    public event EventHandler<KeyPressedEventArgs>? KeyPressed = null;

    private readonly Window window = new();
    private int currentId;

    private class Window : NativeWindow, IDisposable
    {
        public event EventHandler<KeyPressedEventArgs>? KeyPressed = null;

        public Window()
        {
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg == (int)User32.WindowMessage.WM_HOTKEY)
            {
                Keys key = (Keys)(((int)m.LParam >> 16) & 0xFFFF);
                User32.HotKeyModifiers modifier = (User32.HotKeyModifiers)((int)m.LParam & 0xFFFF);

                KeyPressed?.Invoke(this, new KeyPressedEventArgs(modifier, key));
            }
        }

        public void Dispose()
        {
            DestroyHandle();
        }
    }

    public HotkeyHook()
    {
        window.KeyPressed += (sender, args) =>
        {
            KeyPressed?.Invoke(this, args);
        };
    }

    public void RegisterHotKey(User32.HotKeyModifiers modifier, Keys key)
    {
        currentId += 1;
        if (!User32.RegisterHotKey(window!.Handle, currentId, modifier, (uint)key))
        {
            if (Marshal.GetLastWin32Error() == SystemErrorCodes.ERROR_HOTKEY_ALREADY_REGISTERED)
            {
                throw new InvalidOperationException("Hotkey already registered");
            }
            else
            {
                throw new InvalidOperationException("Hotkey registration failed");
            }
        }
    }

    public void UnregisterHotKey()
    {
        for (int i = currentId; i > 0; i--)
        {
            User32.UnregisterHotKey(window!.Handle, i);
        }
    }

    public void Dispose()
    {
        UnregisterHotKey();
        window?.Dispose();
    }
}
