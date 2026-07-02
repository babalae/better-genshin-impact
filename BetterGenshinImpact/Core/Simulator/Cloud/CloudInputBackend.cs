using Fischless.WindowsInput;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Simulator.Cloud;

/// <summary>
/// 将 BetterGI 的键鼠模拟接口适配为云原神页面 JavaScript 输入命令。
/// 键盘、鼠标共享同一个有序队列；断线、导航或停止任务时可以统一清理状态。
/// </summary>
public sealed class CloudInputBackend : IGameInputBackend
{
    // 键盘与鼠标共享的单消费者命令队列。
    private readonly CloudInputCommandQueue _queue;

    // 云端逻辑按键状态，不读取真实系统键盘。
    private readonly CloudInputDeviceStateAdaptor _inputDeviceState;

    // IInputSimulator 暴露的云键盘实现。
    private readonly CloudKeyboardSimulator _keyboard;

    // IInputSimulator 暴露的云鼠标实现。
    private readonly CloudMouseSimulator _mouse;

    // 队列最终调用的页面 JavaScript 桥。
    private readonly ICloudJsBridge _bridge;

    /// <summary>
    /// 创建共享同一命令队列的云键盘和云鼠标后端。
    /// </summary>
    /// <param name="bridge">当前 WebView2 页面对应的 JavaScript 桥。</param>
    public CloudInputBackend(ICloudJsBridge bridge)
    {
        _bridge = bridge;
        _queue = new CloudInputCommandQueue(bridge);
        _inputDeviceState = new CloudInputDeviceStateAdaptor();
        _keyboard = new CloudKeyboardSimulator(this, _queue, _inputDeviceState);
        _mouse = new CloudMouseSimulator(this, _queue);
    }

    /// <inheritdoc />
    public IKeyboardSimulator Keyboard => _keyboard;

    /// <inheritdoc />
    public IMouseSimulator Mouse => _mouse;

    /// <inheritdoc />
    public IInputDeviceStateAdaptor InputDeviceState => _inputDeviceState;

    /// <summary>
    /// 页面命令分发失败时触发，由会话管理器据此取消当前任务。
    /// </summary>
    public event EventHandler<Exception>? DispatchFailed
    {
        add => _queue.DispatchFailed += value;
        remove => _queue.DispatchFailed -= value;
    }

    /// <inheritdoc />
    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        return _queue.FlushAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task ReleaseAllAsync(CancellationToken cancellationToken = default)
    {
        // 先丢弃未执行命令，防止 releaseAll 后旧按键命令再次生效。
        _queue.Clear();
        _inputDeviceState.Clear();
        await _bridge.ReleaseAllAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        // C# 与页面两端状态必须同时重置。
        _queue.Clear();
        _inputDeviceState.Clear();
        await _bridge.ResetAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void ClearPending()
    {
        _queue.Clear();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            await ReleaseAllAsync();
        }
        catch
        {
            // WebView 可能已经关闭，释放输入只做尽力清理。
        }
        // 最后停止消费者，确保不会再访问已经释放的页面桥。
        await _queue.DisposeAsync();
    }
}

/// <summary>
/// 维护云输入后端自身的按键状态，避免读取真实系统键盘状态。
/// </summary>
internal sealed class CloudInputDeviceStateAdaptor : IInputDeviceStateAdaptor
{
    // 并发集合允许任务线程和停止清理线程同时更新按键状态。
    private readonly ConcurrentDictionary<User32.VK, byte> _pressedKeys = new();

    /// <inheritdoc />
    public bool IsKeyDown(User32.VK keyCode) => _pressedKeys.ContainsKey(keyCode);

    /// <inheritdoc />
    public bool IsKeyUp(User32.VK keyCode) => !IsKeyDown(keyCode);

    /// <summary>
    /// 云会话没有独立硬件状态，硬件查询等价于逻辑状态查询。
    /// </summary>
    public bool IsHardwareKeyDown(User32.VK keyCode) => IsKeyDown(keyCode);

    /// <summary>
    /// 云会话没有独立硬件状态，硬件查询等价于逻辑状态查询。
    /// </summary>
    public bool IsHardwareKeyUp(User32.VK keyCode) => IsKeyUp(keyCode);

    /// <summary>
    /// 页面脚本当前不维护 CapsLock 等系统切换键状态。
    /// </summary>
    public bool IsTogglingKeyInEffect(User32.VK keyCode) => false;

    /// <summary>
    /// 将虚拟键记录为已按下。
    /// </summary>
    public void SetKeyDown(User32.VK keyCode) => _pressedKeys[keyCode] = 0;

    /// <summary>
    /// 从已按下集合移除虚拟键。
    /// </summary>
    public void SetKeyUp(User32.VK keyCode) => _pressedKeys.TryRemove(keyCode, out _);

    /// <summary>
    /// 清空全部逻辑按键状态。
    /// </summary>
    public void Clear() => _pressedKeys.Clear();
}

/// <summary>
/// 将 Windows 虚拟键操作转换为页面脚本使用的 KeyboardEvent code。
/// </summary>
/// <param name="inputSimulator">用于从键盘访问同一后端的鼠标模拟器。</param>
/// <param name="queue">键盘和鼠标共享的有序命令队列。</param>
/// <param name="inputDeviceState">当前会话独立的逻辑按键状态。</param>
internal sealed class CloudKeyboardSimulator(
    IInputSimulator inputSimulator,
    CloudInputCommandQueue queue,
    CloudInputDeviceStateAdaptor inputDeviceState) : IKeyboardSimulator
{
    /// <inheritdoc />
    public IMouseSimulator Mouse => inputSimulator.Mouse;

    /// <inheritdoc />
    public IKeyboardSimulator KeyDown(User32.VK keyCode)
    {
        // 命令入队成功后再更新本地状态，使状态查询与待执行命令保持一致。
        queue.Enqueue(new CloudInputCommand { Type = "keyDown", Code = CloudVirtualKeyMapper.ToCode(keyCode) });
        inputDeviceState.SetKeyDown(keyCode);
        return this;
    }

    /// <inheritdoc />
    public IKeyboardSimulator KeyDown(bool? isExtendedKey, User32.VK keyCode) => KeyDown(keyCode);

    /// <inheritdoc />
    public IKeyboardSimulator KeyPress(User32.VK keyCode)
    {
        queue.Enqueue(new CloudInputCommand
        {
            Type = "tapKey",
            Code = CloudVirtualKeyMapper.ToCode(keyCode),
            HoldMilliseconds = 60
        });
        return this;
    }

    /// <inheritdoc />
    public IKeyboardSimulator KeyPress(bool? isExtendedKey, User32.VK keyCode) => KeyPress(keyCode);

    /// <inheritdoc />
    public IKeyboardSimulator KeyPress(params User32.VK[] keyCodes)
    {
        foreach (var keyCode in keyCodes)
        {
            KeyPress(keyCode);
        }
        return this;
    }

    /// <inheritdoc />
    public IKeyboardSimulator KeyPress(bool? isExtendedKey, params User32.VK[] keyCodes) => KeyPress(keyCodes);

    /// <inheritdoc />
    public IKeyboardSimulator KeyUp(User32.VK keyCode)
    {
        queue.Enqueue(new CloudInputCommand { Type = "keyUp", Code = CloudVirtualKeyMapper.ToCode(keyCode) });
        inputDeviceState.SetKeyUp(keyCode);
        return this;
    }

    /// <inheritdoc />
    public IKeyboardSimulator KeyUp(bool? isExtendedKey, User32.VK keyCode) => KeyUp(keyCode);

    /// <inheritdoc />
    public IKeyboardSimulator ModifiedKeyStroke(IEnumerable<User32.VK> modifierKeyCodes, IEnumerable<User32.VK> keyCodes)
    {
        // 先物化修饰键集合，后续需要以相反顺序释放。
        var modifiers = modifierKeyCodes.ToArray();
        foreach (var modifier in modifiers)
        {
            KeyDown(modifier);
        }
        foreach (var keyCode in keyCodes)
        {
            KeyPress(keyCode);
        }
        // 逆序释放修饰键，行为与真实组合键一致。
        foreach (var modifier in modifiers.Reverse())
        {
            KeyUp(modifier);
        }
        return this;
    }

    /// <inheritdoc />
    public IKeyboardSimulator ModifiedKeyStroke(IEnumerable<User32.VK> modifierKeyCodes, User32.VK keyCode) =>
        ModifiedKeyStroke(modifierKeyCodes, [keyCode]);

    /// <inheritdoc />
    public IKeyboardSimulator ModifiedKeyStroke(User32.VK modifierKey, IEnumerable<User32.VK> keyCodes) =>
        ModifiedKeyStroke([modifierKey], keyCodes);

    /// <inheritdoc />
    public IKeyboardSimulator ModifiedKeyStroke(User32.VK modifierKeyCode, User32.VK keyCode) =>
        ModifiedKeyStroke([modifierKeyCode], [keyCode]);

    /// <inheritdoc />
    public IKeyboardSimulator TextEntry(string text)
    {
        queue.Enqueue(new CloudInputCommand { Type = "text", Text = text });
        return this;
    }

    /// <inheritdoc />
    public IKeyboardSimulator TextEntry(char character) => TextEntry(character.ToString());

    /// <inheritdoc />
    public IKeyboardSimulator Sleep(int millsecondsTimeout)
    {
        Thread.Sleep(millsecondsTimeout);
        return this;
    }

    /// <inheritdoc />
    public IKeyboardSimulator Sleep(TimeSpan timeout)
    {
        Thread.Sleep(timeout);
        return this;
    }
}

/// <summary>
/// 云端鼠标模拟器。
/// 绝对坐标接收 IInputSimulator 约定的 0～65535 数值并转换为 0～1；
/// 相对移动直接映射为 RTC 鼠标位移，用于游戏视角旋转。
/// </summary>
/// <param name="inputSimulator">用于从鼠标访问同一后端的键盘模拟器。</param>
/// <param name="queue">键盘和鼠标共享的有序命令队列。</param>
internal sealed class CloudMouseSimulator(
    IInputSimulator inputSimulator,
    CloudInputCommandQueue queue) : IMouseSimulator
{
    // 最近一次绝对横坐标，按钮按下和释放命令必须显式携带该位置。
    private double _currentX = 0.5;

    // 最近一次绝对纵坐标；未移动鼠标前默认位于画面中心。
    private double _currentY = 0.5;

    /// <inheritdoc />
    public IKeyboardSimulator Keyboard => inputSimulator.Keyboard;

    /// <inheritdoc />
    public IMouseSimulator MoveMouseBy(int pixelDeltaX, int pixelDeltaY)
    {
        // 相对移动不重新计算绝对位置，附件脚本会将 dx/dy 直接发送给 RTC。
        queue.Enqueue(new CloudInputCommand
        {
            Type = "move",
            DeltaX = pixelDeltaX,
            DeltaY = pixelDeltaY,
            X = _currentX,
            Y = _currentY
        });
        return this;
    }

    /// <inheritdoc />
    public IMouseSimulator MoveMouseTo(double absoluteX, double absoluteY)
    {
        // IInputSimulator 的绝对坐标为 0～65535，页面脚本使用 0～1。
        _currentX = Math.Clamp(absoluteX / 65535d, 0d, 1d);
        _currentY = Math.Clamp(absoluteY / 65535d, 0d, 1d);
        queue.Enqueue(new CloudInputCommand
        {
            Type = "move",
            DeltaX = 0,
            DeltaY = 0,
            X = _currentX,
            Y = _currentY
        });
        return this;
    }

    /// <inheritdoc />
    public IMouseSimulator MoveMouseToPositionOnVirtualDesktop(double absoluteX, double absoluteY) =>
        MoveMouseTo(absoluteX, absoluteY);

    /// <inheritdoc />
    public IMouseSimulator LeftButtonDown() => ButtonDown("left");

    /// <inheritdoc />
    public IMouseSimulator LeftButtonUp() => ButtonUp("left");

    /// <inheritdoc />
    public IMouseSimulator LeftButtonClick() => ButtonClick("left");

    /// <inheritdoc />
    public IMouseSimulator LeftButtonDoubleClick() => ButtonDoubleClick("left");

    /// <inheritdoc />
    public IMouseSimulator MiddleButtonDown() => ButtonDown("middle");

    /// <inheritdoc />
    public IMouseSimulator MiddleButtonUp() => ButtonUp("middle");

    /// <inheritdoc />
    public IMouseSimulator MiddleButtonClick() => ButtonClick("middle");

    /// <inheritdoc />
    public IMouseSimulator MiddleButtonDoubleClick() => ButtonDoubleClick("middle");

    /// <inheritdoc />
    public IMouseSimulator RightButtonDown() => ButtonDown("right");

    /// <inheritdoc />
    public IMouseSimulator RightButtonUp() => ButtonUp("right");

    /// <inheritdoc />
    public IMouseSimulator RightButtonClick() => ButtonClick("right");

    /// <inheritdoc />
    public IMouseSimulator RightButtonDoubleClick() => ButtonDoubleClick("right");

    /// <summary>
    /// 页面附件脚本不支持鼠标侧键，调用时明确抛出异常。
    /// </summary>
    public IMouseSimulator XButtonDown(int buttonId) => throw UnsupportedXButton();

    /// <summary>
    /// 页面附件脚本不支持鼠标侧键，调用时明确抛出异常。
    /// </summary>
    public IMouseSimulator XButtonUp(int buttonId) => throw UnsupportedXButton();

    /// <summary>
    /// 页面附件脚本不支持鼠标侧键，调用时明确抛出异常。
    /// </summary>
    public IMouseSimulator XButtonClick(int buttonId) => throw UnsupportedXButton();

    /// <summary>
    /// 页面附件脚本不支持鼠标侧键，调用时明确抛出异常。
    /// </summary>
    public IMouseSimulator XButtonDoubleClick(int buttonId) => throw UnsupportedXButton();

    /// <inheritdoc />
    public IMouseSimulator VerticalScroll(int scrollAmountInClicks)
    {
        // WindowsInput 以滚轮格数为单位，页面协议使用标准 120 增量。
        queue.Enqueue(new CloudInputCommand { Type = "scroll", Delta = scrollAmountInClicks * 120d });
        return this;
    }

    /// <summary>
    /// 页面附件脚本不支持水平滚轮，调用时明确抛出异常。
    /// </summary>
    public IMouseSimulator HorizontalScroll(int scrollAmountInClicks) =>
        throw new NotSupportedException("云原神网页版当前不支持水平滚轮输入");

    /// <inheritdoc />
    public IMouseSimulator Sleep(int millsecondsTimeout)
    {
        Thread.Sleep(millsecondsTimeout);
        return this;
    }

    /// <inheritdoc />
    public IMouseSimulator Sleep(TimeSpan timeout)
    {
        Thread.Sleep(timeout);
        return this;
    }

    /// <summary>
    /// 将指定按钮的按下命令加入队列。
    /// </summary>
    private IMouseSimulator ButtonDown(string button)
    {
        queue.Enqueue(CreateButtonCommand("mouseDown", button));
        return this;
    }

    /// <summary>
    /// 将指定按钮的释放命令加入队列。
    /// </summary>
    private IMouseSimulator ButtonUp(string button)
    {
        queue.Enqueue(CreateButtonCommand("mouseUp", button));
        return this;
    }

    /// <summary>
    /// 将包含默认保持时间的单击命令加入队列。
    /// </summary>
    private IMouseSimulator ButtonClick(string button)
    {
        queue.Enqueue(CreateButtonCommand("click", button, 35));
        return this;
    }

    /// <summary>
    /// 将包含默认保持时间的双击命令加入队列。
    /// </summary>
    private IMouseSimulator ButtonDoubleClick(string button)
    {
        queue.Enqueue(CreateButtonCommand("doubleClick", button, 35));
        return this;
    }

    /// <summary>
    /// 创建携带当前绝对坐标的鼠标按钮命令。
    /// </summary>
    private CloudInputCommand CreateButtonCommand(string type, string button, int? holdMilliseconds = null)
    {
        return new CloudInputCommand
        {
            Type = type,
            Button = button,
            X = _currentX,
            Y = _currentY,
            HoldMilliseconds = holdMilliseconds
        };
    }

    /// <summary>
    /// 创建统一的鼠标侧键不支持异常。
    /// </summary>
    private static NotSupportedException UnsupportedXButton() =>
        new("云原神网页版当前不支持鼠标侧键输入");
}

/// <summary>
/// Windows 虚拟键到浏览器 KeyboardEvent code 的映射表。
/// 未明确支持的按键会抛出异常，避免静默发送错误输入。
/// </summary>
internal static class CloudVirtualKeyMapper
{
    // 无法通过连续字符范围推导的虚拟键到 KeyboardEvent code 映射。
    private static readonly Dictionary<User32.VK, string> KeyMap = new()
    {
        [User32.VK.VK_ESCAPE] = "Escape",
        [User32.VK.VK_TAB] = "Tab",
        [User32.VK.VK_CAPITAL] = "CapsLock",
        [User32.VK.VK_SHIFT] = "ShiftLeft",
        [User32.VK.VK_LSHIFT] = "ShiftLeft",
        [User32.VK.VK_RSHIFT] = "ShiftRight",
        [User32.VK.VK_CONTROL] = "ControlLeft",
        [User32.VK.VK_LCONTROL] = "ControlLeft",
        [User32.VK.VK_RCONTROL] = "ControlRight",
        [User32.VK.VK_MENU] = "AltLeft",
        [User32.VK.VK_LMENU] = "AltLeft",
        [User32.VK.VK_RMENU] = "AltRight",
        [User32.VK.VK_SPACE] = "Space",
        [User32.VK.VK_RETURN] = "Enter",
        [User32.VK.VK_BACK] = "Backspace",
        [User32.VK.VK_DELETE] = "Delete",
        [User32.VK.VK_INSERT] = "Insert",
        [User32.VK.VK_HOME] = "Home",
        [User32.VK.VK_END] = "End",
        [User32.VK.VK_PRIOR] = "PageUp",
        [User32.VK.VK_NEXT] = "PageDown",
        [User32.VK.VK_UP] = "ArrowUp",
        [User32.VK.VK_DOWN] = "ArrowDown",
        [User32.VK.VK_LEFT] = "ArrowLeft",
        [User32.VK.VK_RIGHT] = "ArrowRight",
        [User32.VK.VK_OEM_MINUS] = "Minus",
        [User32.VK.VK_OEM_PLUS] = "Equal",
        [User32.VK.VK_OEM_4] = "BracketLeft",
        [User32.VK.VK_OEM_6] = "BracketRight",
        [User32.VK.VK_OEM_5] = "Backslash",
        [User32.VK.VK_OEM_1] = "Semicolon",
        [User32.VK.VK_OEM_7] = "Quote",
        [User32.VK.VK_OEM_3] = "Backquote",
        [User32.VK.VK_OEM_COMMA] = "Comma",
        [User32.VK.VK_OEM_PERIOD] = "Period",
        [User32.VK.VK_OEM_2] = "Slash"
    };

    /// <summary>
    /// 将 Windows 虚拟键转换为页面键盘事件 code。
    /// </summary>
    /// <param name="keyCode">任务代码使用的 Windows 虚拟键。</param>
    /// <returns>浏览器 KeyboardEvent code。</returns>
    /// <exception cref="NotSupportedException">当前键位没有安全映射时抛出。</exception>
    public static string ToCode(User32.VK keyCode)
    {
        if (KeyMap.TryGetValue(keyCode, out var code))
        {
            return code;
        }

        // 字母、数字、功能键和数字键盘均可按虚拟键连续区间生成 code。
        var value = (int)keyCode;
        if (value is >= 0x41 and <= 0x5A)
        {
            return $"Key{(char)value}";
        }
        if (value is >= 0x30 and <= 0x39)
        {
            return $"Digit{(char)value}";
        }
        if (value is >= 0x70 and <= 0x7B)
        {
            return $"F{value - 0x6F}";
        }
        if (value is >= 0x60 and <= 0x69)
        {
            return $"Numpad{value - 0x60}";
        }

        throw new NotSupportedException($"云原神网页版不支持按键 {keyCode} (0x{value:X2})");
    }
}
