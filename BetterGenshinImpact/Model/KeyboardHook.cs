using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Runtime;
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
    /// 判断游戏是否位于前台：有运行环境时看绑定窗口，没有时按前台进程名判断。
    /// 键鼠钩子由静态字典分发、由 HotKeySettingModel 直接 new，没有可以注入服务的创建入口，
    /// 因此从容器获取 <see cref="GameRuntimeService"/>（L5 类，见 task-context-removal.md 第 4.6 节），每次读取 Current，不缓存运行环境
    /// </summary>
    /// <returns>游戏位于前台时返回 true</returns>
    private static bool IsGameForeground()
    {
        return SystemControl.IsGameForeground(App.GetService<GameRuntimeService>()?.Current?.Window);
    }

    /// <summary>
    /// 注意长按的时候会一直触发KeyDown
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    public void KeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsGameForeground())
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

                KeyPressedEvent?.Invoke(this, new KeyPressedEventArgs(User32.HotKeyModifiers.MOD_NONE, e.KeyCode));
            }
        }
    }

    public void KeyUp(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == BindKey)
        {
            IsPressed = false;
            if (IsGameForeground() && !ChatUiHotkeyGuard.ShouldBlockHotkey(ConfigPropertyName))
            {
                KeyUpEvent?.Invoke(this, new KeyPressedEventArgs(User32.HotKeyModifiers.MOD_NONE, e.KeyCode));
            }
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
        // 重复键注册失败的对象并不拥有该键，不能在清理时移除原有功能的监听。
        if (AllKeyboardHooks.TryGetValue(BindKey, out var owner) && ReferenceEquals(owner, this))
            AllKeyboardHooks.Remove(BindKey);
    }

    public void Dispose()
    {
        UnregisterHotKey();
    }
}
