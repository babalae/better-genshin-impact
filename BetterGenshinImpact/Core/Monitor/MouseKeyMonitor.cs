using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recorder;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Model;
using Gma.System.MouseKeyHook;
using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using Vanara.PInvoke;
using Timer = System.Timers.Timer;

// Wine 平台适配
using BetterGenshinImpact.Platform.Wine;

namespace BetterGenshinImpact.Core.Monitor;

public partial class MouseKeyMonitor : IDisposable
{
    private static IKeyboardMouseEvents? _globalHook;
    private static readonly object GlobalHookLock = new();

    public static IKeyboardMouseEvents GlobalHook
    {
        get
        {
            if (_globalHook == null)
            {
                lock (GlobalHookLock)
                {
                    _globalHook ??= Hook.GlobalEvents();
                }
            }

            return _globalHook;
        }
    }

    private readonly Timer _fTimer = new();
    private readonly Timer _spaceTimer = new();
    private readonly RawInputMonitor _rawInputMonitor =
        App.GetService<RawInputMonitor>() ?? throw new InvalidOperationException("RawInputMonitor 服务未注册");

    private IDisposable? _keyboardSubscription;
    private IDisposable? _mouseButtonSubscription;
    private IDisposable? _mouseWheelSubscription;
    private bool _isSubscribed;
    private bool _disposed;
    private bool _usingRawInput;
    private nint _hWnd;

    private Keys _pickUpKey = Keys.F;
    private User32.VK _pickUpKeyCode = User32.VK.VK_F;
    private Keys _releaseControlKey = Keys.Space;
    private User32.VK _releaseControlKeyCode = User32.VK.VK_SPACE;
    private DateTime _firstFKeyDownTime = DateTime.MaxValue;
    private DateTime _firstSpaceKeyDownTime = DateTime.MaxValue;

    public MouseKeyMonitor()
    {
        _spaceTimer.Elapsed += OnSpaceTimerElapsed;
        _fTimer.Elapsed += OnFTimerElapsed;
    }

    public void Subscribe(nint gameHandle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _hWnd = gameHandle;

        _pickUpKey = TaskContext.Instance().Config.KeyBindingsConfig.PickUpOrInteract.ToWinFormKeys();
        _pickUpKeyCode = TaskContext.Instance().Config.KeyBindingsConfig.PickUpOrInteract.ToVK();
        _releaseControlKey = TaskContext.Instance().Config.KeyBindingsConfig.Jump.ToWinFormKeys();
        _releaseControlKeyCode = TaskContext.Instance().Config.KeyBindingsConfig.Jump.ToVK();
        _firstSpaceKeyDownTime = DateTime.MaxValue;
        _firstFKeyDownTime = DateTime.MaxValue;
        _spaceTimer.Interval = TaskContext.Instance().Config.MacroConfig.SpaceFireInterval;
        _fTimer.Interval = TaskContext.Instance().Config.MacroConfig.FFireInterval;

        if (_isSubscribed)
        {
            return;
        }

        _usingRawInput = TaskContext.Instance().Config.UseRawInput;

        if (!WinePlatformAddon.IsRunningOnWine)
        {
            if (_usingRawInput)
            {
                _keyboardSubscription = _rawInputMonitor.SubscribeKeyboard(GlobalHookKeyDown);
                _mouseButtonSubscription = _rawInputMonitor.SubscribeMouseButton(GlobalHookMouseButton);
                _mouseWheelSubscription = _rawInputMonitor.SubscribeMouseWheel(GlobalHookMouseWheel);
            }
            else
            {
                _globalHook = GlobalHook;
                _globalHook.KeyDown += GlobalHookKeyDown;
                _globalHook.KeyUp += GlobalHookKeyUp;
                _globalHook.MouseDownExt += GlobalHookMouseDownExt;
                _globalHook.MouseUpExt += GlobalHookMouseUpExt;
                _globalHook.MouseMoveExt += GlobalHookMouseMoveExt;
                _globalHook.MouseWheelExt += GlobalHookMouseWheelExt;
            }
        }

        TrySubscribeWinePolling();
        _isSubscribed = true;
        Debug.WriteLine($"[Input] 后端={(_usingRawInput ? "RawInput" : "GlobalHook")}");
    }

    private void OnSpaceTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        Simulation.PostMessage(_hWnd).KeyPress(_releaseControlKeyCode);
    }

    private void OnFTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        Simulation.PostMessage(_hWnd).KeyPress(_pickUpKeyCode);
    }

    private void GlobalHookKeyDown(object? sender, RawKeyboardInputEventArgs e)
    {
        if (e.IsKeyDown)
        {
            HandleKeyDown(sender, (Keys)e.VirtualKey, e.Timestamp);
        }
        else
        {
            HandleKeyUp(sender, (Keys)e.VirtualKey, e.Timestamp);
        }
    }

    private void GlobalHookKeyDown(object? sender, KeyEventArgs e)
    {
        HandleKeyDown(sender, e.KeyCode, DateTime.UtcNow);
    }

    private void GlobalHookKeyUp(object? sender, KeyEventArgs e)
    {
        HandleKeyUp(sender, e.KeyCode, DateTime.UtcNow);
    }

    private void HandleKeyDown(object? sender, Keys key, DateTime timestamp)
    {
        GlobalKeyMouseRecord.Instance.GlobalHookKeyDown(key, timestamp);
        if (SystemControl.IsGenshinImpactActive())
        {
            ChatUiHotkeyGuard.PrimeFromChatKey(key);
        }

        HotKeyDown(sender, key);
        if (key == _releaseControlKey)
        {
            if (_firstSpaceKeyDownTime == DateTime.MaxValue)
            {
                _firstSpaceKeyDownTime = DateTime.Now;
            }
            else if ((DateTime.Now - _firstSpaceKeyDownTime).TotalMilliseconds > 300
                     && TaskContext.Instance().Config.MacroConfig.SpacePressHoldToContinuationEnabled
                     && !_spaceTimer.Enabled)
            {
                _spaceTimer.Start();
            }
        }
        else if (key == _pickUpKey)
        {
            if (_firstFKeyDownTime == DateTime.MaxValue)
            {
                _firstFKeyDownTime = DateTime.Now;
            }
            else if ((DateTime.Now - _firstFKeyDownTime).TotalMilliseconds > 200
                     && TaskContext.Instance().Config.MacroConfig.FPressHoldToContinuationEnabled
                     && !_fTimer.Enabled)
            {
                _fTimer.Start();
            }
        }
    }

    private void HandleKeyUp(object? sender, Keys key, DateTime timestamp)
    {
        GlobalKeyMouseRecord.Instance.GlobalHookKeyUp(key, timestamp);
        HotKeyUp(sender, key);
        if (key == _releaseControlKey)
        {
            _firstSpaceKeyDownTime = DateTime.MaxValue;
            _spaceTimer.Stop();
        }
        else if (key == _pickUpKey)
        {
            _firstFKeyDownTime = DateTime.MaxValue;
            _fTimer.Stop();
        }
    }

    private void HotKeyDown(object? sender, Keys key)
    {
        if (KeyboardHook.AllKeyboardHooks.TryGetValue(key, out var hook)) hook.KeyDown(sender, key);
    }

    private void HotKeyUp(object? sender, Keys key)
    {
        if (KeyboardHook.AllKeyboardHooks.TryGetValue(key, out var hook)) hook.KeyUp(sender, key);
    }

    private void GlobalHookMouseButton(object? sender, RawMouseButtonEventArgs e)
    {
        HandleMouseButton(sender, e.Button, e.IsDown, DateTime.UtcNow);
    }

    private void GlobalHookMouseDownExt(object? sender, MouseEventExtArgs e)
    {
        var timestamp = DateTime.UtcNow;
        GlobalKeyMouseRecord.Instance.GlobalHookMouseDown(e, timestamp);
        HandleMouseButton(sender, e.Button, true, timestamp, record: false);
    }

    private void GlobalHookMouseUpExt(object? sender, MouseEventExtArgs e)
    {
        var timestamp = DateTime.UtcNow;
        GlobalKeyMouseRecord.Instance.GlobalHookMouseUp(e, timestamp);
        HandleMouseButton(sender, e.Button, false, timestamp, record: false);
    }

    private void HandleMouseButton(object? sender, MouseButtons button, bool isDown, DateTime timestamp, bool record = true)
    {
        if (record && isDown)
        {
            GlobalKeyMouseRecord.Instance.GlobalHookMouseDown(button, timestamp);
        }
        else if (record)
        {
            GlobalKeyMouseRecord.Instance.GlobalHookMouseUp(button, timestamp);
        }

        if (button != MouseButtons.Left
            && MouseHook.AllMouseHooks.TryGetValue(button, out var hook))
        {
            hook.MouseDown(sender, button, isDown);
        }
    }

    private void GlobalHookMouseMoveExt(object? sender, MouseEventExtArgs e)
    {
        GlobalKeyMouseRecord.Instance.GlobalHookMouseMoveTo(e, DateTime.UtcNow);
    }

    private void GlobalHookMouseWheel(object? sender, RawMouseWheelEventArgs e)
    {
        GlobalKeyMouseRecord.Instance.GlobalHookMouseWheel(e.Delta, DateTime.UtcNow);
    }

    private void GlobalHookMouseWheelExt(object? sender, MouseEventExtArgs e)
    {
        GlobalKeyMouseRecord.Instance.GlobalHookMouseWheel(e, DateTime.UtcNow);
    }

    public void Unsubscribe()
    {
        _spaceTimer.Stop();
        _fTimer.Stop();
        _firstSpaceKeyDownTime = DateTime.MaxValue;
        _firstFKeyDownTime = DateTime.MaxValue;

        if (_usingRawInput)
        {
            _keyboardSubscription?.Dispose();
            _keyboardSubscription = null;
            _mouseButtonSubscription?.Dispose();
            _mouseButtonSubscription = null;
            _mouseWheelSubscription?.Dispose();
            _mouseWheelSubscription = null;
        }
        else if (_globalHook != null)
        {
            _globalHook.KeyDown -= GlobalHookKeyDown;
            _globalHook.KeyUp -= GlobalHookKeyUp;
            _globalHook.MouseDownExt -= GlobalHookMouseDownExt;
            _globalHook.MouseUpExt -= GlobalHookMouseUpExt;
            _globalHook.MouseMoveExt -= GlobalHookMouseMoveExt;
            _globalHook.MouseWheelExt -= GlobalHookMouseWheelExt;
            _globalHook.Dispose();
            _globalHook = null;
        }

        if (WinePlatformAddon.IsRunningOnWine)
        {
            DisposeWineAddon();
        }

        _isSubscribed = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Unsubscribe();
        _disposed = true;
        _spaceTimer.Elapsed -= OnSpaceTimerElapsed;
        _fTimer.Elapsed -= OnFTimerElapsed;
        _spaceTimer.Dispose();
        _fTimer.Dispose();
        GC.SuppressFinalize(this);
    }
}
