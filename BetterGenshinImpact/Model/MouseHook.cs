using BetterGenshinImpact.GameTask;
using Fischless.HotkeyCapture;
using Gma.System.MouseKeyHook;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Model;

public class MouseHook
{
    public static Dictionary<MouseButtons, MouseHook> AllMouseHooks = [];

    public event EventHandler<KeyPressedEventArgs>? MousePressed = null;

    public event EventHandler<KeyPressedEventArgs>? MouseDownEvent = null;

    public event EventHandler<KeyPressedEventArgs>? MouseUpEvent = null;

    public bool IsHold { get; set; }

    public MouseButtons BindMouse { get; set; } = MouseButtons.Left;

    public bool IsPressed { get; set; }

    public string ConfigPropertyName { get; set; } = string.Empty;

    public void MouseDown(object? sender, MouseEventExtArgs e)
    {
        if (!SystemControl.IsGenshinImpactActive())
        {
            return;
        }

        if (e.Button != MouseButtons.Left && e.Button != MouseButtons.None && e.Button == BindMouse)
        {
            if (ChatUiHotkeyGuard.ShouldBlockHotkey(ConfigPropertyName))
            {
                return;
            }

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
                if (ChatUiHotkeyGuard.ShouldBlockHotkey(ConfigPropertyName))
                {
                    Thread.Sleep(10);
                    continue;
                }

                MousePressed?.Invoke(this, new KeyPressedEventArgs(User32.HotKeyModifiers.MOD_NONE, Keys.None));
            }
        }
    }

    public void MouseUp(object? sender, MouseEventExtArgs e)
    {
        if (e.Button != MouseButtons.Left && e.Button != MouseButtons.None && e.Button == BindMouse)
        {
            IsPressed = false;
            if (SystemControl.IsGenshinImpactActive() && !ChatUiHotkeyGuard.ShouldBlockHotkey(ConfigPropertyName))
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
        // 只有实际登记的对象可以注销；失败的候选与重复清理不能误删原有侧键监听。
        if (AllMouseHooks.TryGetValue(BindMouse, out var owner) && ReferenceEquals(owner, this))
            AllMouseHooks.Remove(BindMouse);
    }

    public void Dispose()
    {
        UnregisterHotKey();
    }
}
