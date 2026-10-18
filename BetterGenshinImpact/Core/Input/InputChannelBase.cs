using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Simulator.Extensions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Input;

/// <summary>
/// 输入通道基类。
/// 一个实例同时实现通道、键盘、鼠标三个接口，<see cref="Keyboard"/> 与 <see cref="Mouse"/> 都返回自身。
/// 负责：按下状态记录、GIActions 默认映射、鼠标键 VK 转发、不支持操作的限频 warn。
/// 子类只需实现 On* 原子操作。
/// </summary>
public abstract class InputChannelBase : IInputChannel, IKeyboardInput, IMouseInput
{
    /// <summary>
    /// 同一种不支持的操作在此时间内只打一次 warn
    /// </summary>
    private const long WarnIntervalMs = 10_000;

    private static ILogger? _logger;

    private readonly object _pressedLock = new();
    private readonly HashSet<User32.VK> _pressed = [];
    private readonly ConcurrentDictionary<string, long> _lastWarnTicks = new();

    protected static ILogger Logger => _logger ??= App.GetLogger<InputChannelBase>();

    public IKeyboardInput Keyboard => this;

    public IMouseInput Mouse => this;

    #region 子类实现的原子操作

    protected abstract void OnKeyDown(User32.VK key);

    protected abstract void OnKeyUp(User32.VK key);

    /// <summary>
    /// 默认为按下 + 抬起，子类可覆写以保持各自的时序
    /// </summary>
    protected virtual void OnKeyPress(User32.VK key)
    {
        OnKeyDown(key);
        OnKeyUp(key);
    }

    protected abstract void OnMouseButton(InputMouseButton button, bool down);

    /// <summary>
    /// 默认为按下 + 抬起，子类可覆写以保持各自的时序
    /// </summary>
    protected virtual void OnMouseClick(InputMouseButton button)
    {
        OnMouseButton(button, true);
        OnMouseButton(button, false);
    }

    protected abstract void OnMoveBy(int dx, int dy);

    protected abstract void OnMoveTo(double absX, double absY);

    protected abstract void OnScroll(int clicks);

    #endregion

    #region IInputChannel

    public virtual IInputChannel SimulateAction(GIActions action, KeyType type = KeyType.KeyPress)
    {
        var key = action.ToActionKey();
        if (key is KeyId.None or KeyId.Unknown)
        {
            return this;
        }

        // KeyId 的鼠标键取值与 VK 一致，鼠标键由 Keyboard 接口转发到鼠标
        var vk = key.ToVK();
        switch (type)
        {
            case KeyType.KeyPress:
                KeyPress(vk);
                break;
            case KeyType.KeyDown:
                KeyDown(vk);
                break;
            case KeyType.KeyUp:
                KeyUp(vk);
                break;
            case KeyType.Hold:
                KeyDown(vk);
                Thread.Sleep(1000);
                KeyUp(vk);
                break;
        }

        return this;
    }

    public virtual bool IsKeyDown(User32.VK key)
    {
        lock (_pressedLock)
        {
            return _pressed.Contains(key);
        }
    }

    IInputChannel IInputChannel.Sleep(int ms)
    {
        Thread.Sleep(ms);
        return this;
    }

    /// <summary>
    /// 按记录逐个抬起。单个按键失败不影响其余按键
    /// </summary>
    public virtual void ReleaseAll()
    {
        foreach (var key in TakePressedKeys())
        {
            try
            {
                if (TryGetMouseButton(key, out var button))
                {
                    OnMouseButton(button, false);
                }
                else
                {
                    OnKeyUp(key);
                }
            }
            catch (Exception e)
            {
                Logger.LogDebug(e, "释放按键 {Key} 失败", key);
            }
        }
    }

    #endregion

    #region IKeyboardInput

    public IKeyboardInput KeyDown(User32.VK key)
    {
        if (TryGetMouseButton(key, out var button))
        {
            ButtonDown(button);
            return this;
        }

        OnKeyDown(key);
        SetPressed(key, true);
        return this;
    }

    public IKeyboardInput KeyUp(User32.VK key)
    {
        if (TryGetMouseButton(key, out var button))
        {
            ButtonUp(button);
            return this;
        }

        SetPressed(key, false);
        OnKeyUp(key);
        return this;
    }

    public IKeyboardInput KeyPress(User32.VK key)
    {
        if (TryGetMouseButton(key, out var button))
        {
            ButtonClick(button);
            return this;
        }

        SetPressed(key, false);
        OnKeyPress(key);
        return this;
    }

    IKeyboardInput IKeyboardInput.Sleep(int ms)
    {
        Thread.Sleep(ms);
        return this;
    }

    #endregion

    #region IMouseInput

    public IMouseInput MoveMouseBy(int dx, int dy)
    {
        OnMoveBy(dx, dy);
        return this;
    }

    public IMouseInput MoveMouseTo(double absX, double absY)
    {
        OnMoveTo(absX, absY);
        return this;
    }

    public IMouseInput LeftButtonDown() => ButtonDown(InputMouseButton.Left);

    public IMouseInput LeftButtonUp() => ButtonUp(InputMouseButton.Left);

    public IMouseInput LeftButtonClick() => ButtonClick(InputMouseButton.Left);

    public IMouseInput RightButtonDown() => ButtonDown(InputMouseButton.Right);

    public IMouseInput RightButtonUp() => ButtonUp(InputMouseButton.Right);

    public IMouseInput RightButtonClick() => ButtonClick(InputMouseButton.Right);

    public IMouseInput MiddleButtonDown() => ButtonDown(InputMouseButton.Middle);

    public IMouseInput MiddleButtonUp() => ButtonUp(InputMouseButton.Middle);

    public IMouseInput MiddleButtonClick() => ButtonClick(InputMouseButton.Middle);

    public IMouseInput XButtonDown(int buttonId) => TryGetXButton(buttonId, out var b) ? ButtonDown(b) : this;

    public IMouseInput XButtonUp(int buttonId) => TryGetXButton(buttonId, out var b) ? ButtonUp(b) : this;

    public IMouseInput XButtonClick(int buttonId) => TryGetXButton(buttonId, out var b) ? ButtonClick(b) : this;

    public IMouseInput VerticalScroll(int scrollAmountInClicks)
    {
        OnScroll(scrollAmountInClicks);
        return this;
    }

    IMouseInput IMouseInput.Sleep(int ms)
    {
        Thread.Sleep(ms);
        return this;
    }

    #endregion

    #region 辅助

    /// <summary>
    /// 不支持的操作：限频打 warn 后直接忽略
    /// </summary>
    protected void WarnUnsupported(string operation)
    {
        var now = Environment.TickCount64;
        var last = _lastWarnTicks.GetOrAdd(operation, long.MinValue);
        if (last != long.MinValue && now - last < WarnIntervalMs)
        {
            return;
        }

        _lastWarnTicks[operation] = now;
        Logger.LogWarning("[{Channel}] 不支持的输入操作已忽略：{Operation}", GetType().Name, operation);
    }

    /// <summary>
    /// 取出并清空按下记录
    /// </summary>
    protected IReadOnlyList<User32.VK> TakePressedKeys()
    {
        lock (_pressedLock)
        {
            var keys = _pressed.ToList();
            _pressed.Clear();
            return keys;
        }
    }

    protected static bool TryGetMouseButton(User32.VK key, out InputMouseButton button)
    {
        switch (key)
        {
            case User32.VK.VK_LBUTTON:
                button = InputMouseButton.Left;
                return true;
            case User32.VK.VK_RBUTTON:
                button = InputMouseButton.Right;
                return true;
            case User32.VK.VK_MBUTTON:
                button = InputMouseButton.Middle;
                return true;
            case User32.VK.VK_XBUTTON1:
                button = InputMouseButton.X1;
                return true;
            case User32.VK.VK_XBUTTON2:
                button = InputMouseButton.X2;
                return true;
            default:
                button = default;
                return false;
        }
    }

    protected static User32.VK ToVirtualKey(InputMouseButton button) => button switch
    {
        InputMouseButton.Left => User32.VK.VK_LBUTTON,
        InputMouseButton.Right => User32.VK.VK_RBUTTON,
        InputMouseButton.Middle => User32.VK.VK_MBUTTON,
        InputMouseButton.X1 => User32.VK.VK_XBUTTON1,
        InputMouseButton.X2 => User32.VK.VK_XBUTTON2,
        _ => throw new ArgumentOutOfRangeException(nameof(button), button, null),
    };

    private bool TryGetXButton(int buttonId, out InputMouseButton button)
    {
        switch (buttonId)
        {
            case 1:
                button = InputMouseButton.X1;
                return true;
            case 2:
                button = InputMouseButton.X2;
                return true;
            default:
                button = default;
                WarnUnsupported($"侧键编号 {buttonId}");
                return false;
        }
    }

    private InputChannelBase ButtonDown(InputMouseButton button)
    {
        OnMouseButton(button, true);
        SetPressed(ToVirtualKey(button), true);
        return this;
    }

    private InputChannelBase ButtonUp(InputMouseButton button)
    {
        SetPressed(ToVirtualKey(button), false);
        OnMouseButton(button, false);
        return this;
    }

    private InputChannelBase ButtonClick(InputMouseButton button)
    {
        SetPressed(ToVirtualKey(button), false);
        OnMouseClick(button);
        return this;
    }

    private void SetPressed(User32.VK key, bool pressed)
    {
        lock (_pressedLock)
        {
            if (pressed)
            {
                _pressed.Add(key);
            }
            else
            {
                _pressed.Remove(key);
            }
        }
    }

    #endregion
}
