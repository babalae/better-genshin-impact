using Fischless.HotkeyCapture;
using Gma.System.MouseKeyHook;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using BetterGenshinImpact.GameTask;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Model;

public class MouseHook
{
    public static Dictionary<MouseButtons, MouseHook> AllMouseHooks = new();

    public event EventHandler<KeyPressedEventArgs>? MousePressed = null;

    public event EventHandler<KeyPressedEventArgs>? MouseDownEvent = null;

    public event EventHandler<KeyPressedEventArgs>? MouseUpEvent = null;

    public bool IsHold { get; set; }

    public MouseButtons BindMouse { get; set; } = MouseButtons.Left;

    public bool IsPressed { get; set; }

    public void MouseDown(object? sender, MouseEventExtArgs e)
    {
        if (!SystemControl.IsGenshinImpactActive())
        {
            return;
        }

        if (e.Button != MouseButtons.Left && e.Button != MouseButtons.None && e.Button == BindMouse)
        {
            IsPressed = true;
            MouseDownEvent?.Invoke(this, new KeyPressedEventArgs(User32.HotKeyModifiers.MOD_NONE, Keys.None));
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
            if (SystemControl.IsGenshinImpactActive())
            {
                MouseUpEvent?.Invoke(this, new KeyPressedEventArgs(User32.HotKeyModifiers.MOD_NONE, Keys.None));
            }
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
