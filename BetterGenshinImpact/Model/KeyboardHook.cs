using BetterGenshinImpact.GameTask;
using Fischless.HotkeyCapture;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Model;

public class KeyboardHook
{
    public static Dictionary<Keys, KeyboardHook> AllKeyboardHooks = [];

    public event EventHandler<KeyPressedEventArgs>? KeyPressedEvent = null;

    public event EventHandler<KeyPressedEventArgs>? KeyDownEvent = null;

    public event EventHandler<KeyPressedEventArgs>? KeyUpEvent = null;

    public bool IsHold { get; set; }

    public Keys BindKey { get; set; } = Keys.None;

    public bool IsPressed { get; set; }

    public string ConfigPropertyName { get; set; } = string.Empty;

    /// <summary>
    /// 注意长按的时候会一直触发KeyDown
    /// </summary>
    /// <param name="sender">事件源</param>
    /// <param name="e">按键事件参数</param>
    public void KeyDown(object? sender, KeyEventArgs e)
    {
        if (!SystemControl.IsGenshinImpactActive())
        {
            return;
        }

        if (e.KeyCode == BindKey)
        {
            if (ChatUiHotkeyGuard.ShouldBlockHotkey(ConfigPropertyName))
            {
                return;
            }

            IsPressed = true;
            KeyDownEvent?.Invoke(this, new KeyPressedEventArgs(User32.HotKeyModifiers.MOD_NONE, e.KeyCode));
            if (IsHold)
            {
                if (KeyPressedEvent != null)
                {
                    Task.Run(() => RunAction(e));
                }
            }
            else
            {
                KeyPressedEvent?.Invoke(this, new KeyPressedEventArgs(User32.HotKeyModifiers.MOD_NONE, e.KeyCode));
                IsPressed = false;
            }
        }
    }

    /// <summary>
    /// 长按循环的最小间隔（毫秒）。动作本身耗时不足时补齐，
    /// 避免动作快速返回时空转占满一个 CPU 核心。
    /// </summary>
    private const int MinActionIntervalMs = 10;

    /// <summary>
    /// 长按持续执行
    /// </summary>
    /// <param name="e"></param>
    private void RunAction(KeyEventArgs e)
    {
        lock (this)
        {
            while (IsPressed && KeyPressedEvent != null)
            {
                if (ChatUiHotkeyGuard.ShouldBlockHotkey(ConfigPropertyName))
                {
                    Thread.Sleep(10);
                    continue;
                }

                var startTicks = Environment.TickCount64;
                KeyPressedEvent?.Invoke(this, new KeyPressedEventArgs(User32.HotKeyModifiers.MOD_NONE, e.KeyCode));

                var elapsed = Environment.TickCount64 - startTicks;
                if (elapsed < MinActionIntervalMs)
                {
                    Thread.Sleep((int)(MinActionIntervalMs - elapsed));
                }
            }
        }
    }

    /// <summary>
    /// 按键抬起：结束长按状态并触发一次抬起回调
    /// </summary>
    /// <param name="sender">事件源</param>
    /// <param name="e">按键事件参数</param>
    public void KeyUp(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == BindKey)
        {
            IsPressed = false;
            if (SystemControl.IsGenshinImpactActive() && !ChatUiHotkeyGuard.ShouldBlockHotkey(ConfigPropertyName))
            {
                KeyUpEvent?.Invoke(this, new KeyPressedEventArgs(User32.HotKeyModifiers.MOD_NONE, e.KeyCode));
            }
        }
    }

    /// <summary>
    /// 注册为键鼠监听：绑定按键并加入全局按键分发表
    /// </summary>
    /// <param name="key">要监听的按键</param>
    public void RegisterHotKey(Keys key)
    {
        BindKey = key;
        AllKeyboardHooks.Add(key, this);
    }

    /// <summary>
    /// 注销键鼠监听，并复位长按状态
    /// </summary>
    public void UnregisterHotKey()
    {
        IsPressed = false;
        IsHold = false;
        AllKeyboardHooks.Remove(BindKey);
    }

    /// <summary>
    /// 释放该实例占用的按键注册
    /// </summary>
    public void Dispose()
    {
        UnregisterHotKey();
    }
}
