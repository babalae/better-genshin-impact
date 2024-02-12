using BetterGenshinImpact.GameTask;
using Fischless.HotkeyCapture;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Model;

public class KeyboardHook
{
    public static Dictionary<Keys, KeyboardHook> AllKeyboardHooks = new();

    public event EventHandler<KeyPressedEventArgs>? KeyPressed = null;

    public bool IsHold { get; set; }

    public Keys BindKey { get; set; } = Keys.None;

    public bool IsPressed { get; set; }

    public void KeyDown(object? sender, KeyEventArgs e)
    {
        if (!SystemControl.IsGenshinImpactActive())
        {
            return;
        }

        if (e.KeyCode == BindKey)
        {
            IsPressed = true;
            if (IsHold)
            {
                Task.Run(() => RunAction(e));
            }
            else
            {
                KeyPressed?.Invoke(this, new KeyPressedEventArgs(User32.HotKeyModifiers.MOD_NONE, e.KeyCode));
                IsPressed = false;
            }
        }
    }

    /// <summary>
    /// 长按持续执行
    /// </summary>
    /// <param name="e"></param>
    private void RunAction(KeyEventArgs e)
    {
        lock (this)
        {
            while (IsPressed)
            {
                KeyPressed?.Invoke(this, new KeyPressedEventArgs(User32.HotKeyModifiers.MOD_NONE, e.KeyCode));
            }
        }
    }

    public void KeyUp(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == BindKey)
        {
            IsPressed = false;
        }
    }

    public void RegisterHotKey(Keys key)
    {
        BindKey = key;
        AllKeyboardHooks.Add(key, this);
    }

    public void UnregisterHotKey()
    {
        IsPressed = false;
        IsHold = false;
        AllKeyboardHooks.Remove(BindKey);
    }

    public void Dispose()
    {
        UnregisterHotKey();
    }
}