using Fischless.HotkeyCapture;
using Gma.System.MouseKeyHook;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Model;

public class MouseHook
{
    public static Dictionary<MouseButtons, MouseHook> AllMouseHooks = new();

    public event EventHandler<KeyPressedEventArgs>? MousePressed = null;

    public bool IsHold { get; set; }

    public MouseButtons BindMouse { get; set; } = MouseButtons.Left;

    public bool IsPressed { get; set; }

    public void MouseDown(object? sender, MouseEventExtArgs e)
    {
        if (e.Button != MouseButtons.Left && e.Button != MouseButtons.None && e.Button == BindMouse)
        {
            IsPressed = true;
            if (IsHold)
            {
                Task.Run(() => RunAction(e));
            }
            else
            {
                MousePressed?.Invoke(this, new KeyPressedEventArgs(User32.HotKeyModifiers.MOD_NONE, Keys.None));
                IsPressed = false;
            }
        }
    }

    /// <summary>
    /// 长按持续执行
    /// </summary>
    /// <param name="e"></param>
    private void RunAction(MouseEventExtArgs e)
    {
        lock (this)
        {
            while (IsPressed)
            {
                MousePressed?.Invoke(this, new KeyPressedEventArgs(User32.HotKeyModifiers.MOD_NONE, Keys.None));
            }
        }
    }

    public void MouseUp(object? sender, MouseEventExtArgs e)
    {
        if (e.Button != MouseButtons.Left && e.Button != MouseButtons.None && e.Button == BindMouse)
        {
            IsPressed = false;
        }
    }

    public void RegisterHotKey(MouseButtons mouseButton)
    {
        BindMouse = mouseButton;
        AllMouseHooks.Add(mouseButton, this);
    }

    public void UnregisterHotKey()
    {
        IsPressed = false;
        IsHold = false;
        AllMouseHooks.Remove(BindMouse);
    }

    public void Dispose()
    {
        UnregisterHotKey();
    }
}