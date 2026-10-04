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

    /// <summary>
    /// 鼠标侧键按下：长按功能启动持续触发循环，其余功能触发一次
    /// </summary>
    /// <param name="sender">事件源</param>
    /// <param name="e">鼠标事件参数</param>
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
                // 与 KeyboardHook 保持一致：没有持续触发的回调时不必启动循环
                if (MousePressed != null)
                {
                    Task.Run(() => RunAction(e));
                }
            }
            else
            {
                MousePressed?.Invoke(this, new KeyPressedEventArgs(User32.HotKeyModifiers.MOD_NONE, Keys.None));
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
    private void RunAction(MouseEventExtArgs e)
    {
        lock (this)
        {
            while (IsPressed && MousePressed != null)
            {
                if (ChatUiHotkeyGuard.ShouldBlockHotkey(ConfigPropertyName))
                {
                    Thread.Sleep(10);
                    continue;
                }

                var startTicks = Environment.TickCount64;
                MousePressed?.Invoke(this, new KeyPressedEventArgs(User32.HotKeyModifiers.MOD_NONE, Keys.None));

                var elapsed = Environment.TickCount64 - startTicks;
                if (elapsed < MinActionIntervalMs)
                {
                    Thread.Sleep((int)(MinActionIntervalMs - elapsed));
                }
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
        AllMouseHooks.Remove(BindMouse);
    }

    public void Dispose()
    {
        UnregisterHotKey();
    }
}
