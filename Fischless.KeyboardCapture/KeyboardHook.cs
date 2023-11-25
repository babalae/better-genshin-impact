using System.Diagnostics;
using System.Runtime.InteropServices;
using Vanara.PInvoke;

namespace Fischless.KeyboardCapture;

public sealed class KeyboardHook : IDisposable
{
    public event KeyEventHandler KeyDown = null!;

    public event KeyPressEventHandler KeyPress = null!;

    public event KeyEventHandler KeyUp = null!;

    private User32.SafeHHOOK hook = new(IntPtr.Zero);
    private User32.HookProc? hookProc;

    ~KeyboardHook()
    {
        Dispose();
    }

    public void Dispose()
    {
        Stop();
    }

    public void Start()
    {
        if (hook.IsNull)
        {
            hookProc = KeyboardHookProc;
            hook = User32.SetWindowsHookEx(User32.HookType.WH_KEYBOARD_LL, hookProc, Kernel32.GetModuleHandle(Process.GetCurrentProcess().MainModule.ModuleName), 0);

            User32.SetWindowsHookEx(User32.HookType.WH_KEYBOARD_LL, hookProc, IntPtr.Zero, (int)Kernel32.GetCurrentThreadId());

            if (hook.IsNull)
            {
                Stop();
                throw new SystemException("Failed to install keyboard hook");
            }
        }
    }

    public void Stop()
    {
        bool retKeyboard = true;

        if (!hook.IsNull)
        {
            retKeyboard = User32.UnhookWindowsHookEx(hook);
            hook = new(IntPtr.Zero);
        }

        if (!retKeyboard)
        {
            throw new SystemException("Failed to uninstall hook");
        }
    }

    private nint KeyboardHookProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            if (KeyDown != null || KeyUp != null || KeyPress != null)
            {
                User32.KBDLLHOOKSTRUCT hookStruct = (User32.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(User32.KBDLLHOOKSTRUCT))!;

                if (KeyDown != null && (wParam == (nint)User32.WindowMessage.WM_KEYDOWN || wParam == (nint)User32.WindowMessage.WM_SYSKEYDOWN))
                {
                    Keys keyData = (Keys)hookStruct.vkCode;
                    KeyEventArgs e = new(keyData);
                    KeyDown(this, e);
                }

                if (KeyPress != null && wParam == (nint)User32.WindowMessage.WM_KEYDOWN)
                {
                    byte[] keyState = new byte[256];
                    _ = User32.GetKeyboardState(keyState);

                    if (User32.ToAscii(hookStruct.vkCode, hookStruct.scanCode, keyState, out ushort lpChar, hookStruct.flags) == 1)
                    {
                        KeyPressEventArgs e = new((char)lpChar);
                        KeyPress(this, e);
                    }
                }

                if (KeyUp != null && (wParam == (nint)User32.WindowMessage.WM_KEYUP || wParam == (nint)User32.WindowMessage.WM_SYSKEYUP))
                {
                    Keys keyData = (Keys)hookStruct.vkCode;
                    KeyEventArgs e = new(keyData);
                    KeyUp(this, e);
                }
            }
        }
        return User32.CallNextHookEx(hook, nCode, wParam, lParam);
    }
}
