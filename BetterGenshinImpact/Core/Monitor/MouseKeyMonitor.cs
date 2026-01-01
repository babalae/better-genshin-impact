using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recorder;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask;
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

public partial class  MouseKeyMonitor
{

    /// <summary>
    ///     长按F变F连发
    /// </summary>
    private readonly Timer _fTimer = new();

    private Keys _pickUpKey = Keys.F;

    private User32.VK _pickUpKeyCode = User32.VK.VK_F;

    //private readonly Random _random = new();

    /// <summary>
    ///     长按空格变空格连发
    /// </summary>
    private readonly Timer _spaceTimer = new();

    private Keys _releaseControlKey = Keys.Space;

    private User32.VK _releaseControlKeyCode = User32.VK.VK_SPACE;

    private DateTime _firstFKeyDownTime = DateTime.MaxValue;

    /// <summary>
    ///     DateTime.MaxValue 代表没有按下
    /// </summary>
    private DateTime _firstSpaceKeyDownTime = DateTime.MaxValue;

    private static IKeyboardMouseEvents? _globalHook;
    private static readonly object GlobalHookLock = new object();
    public static IKeyboardMouseEvents GlobalHook
    {
        get
        {
            if (_globalHook == null)
            {
                lock (GlobalHookLock)
                {
                    if (_globalHook == null)
                    {
                        _globalHook = Hook.GlobalEvents();
                    }
                }
            }
            return _globalHook;
        }
    }
    private nint _hWnd;

    public void Subscribe(nint gameHandle)
    {
        _hWnd = gameHandle;
        // Note: for the application hook, use the Hook.AppEvents() instead

        if (!WinePlatformAddon.IsRunningOnWine) {        
            GlobalHook.KeyDown += GlobalHookKeyDown;
            GlobalHook.KeyUp += GlobalHookKeyUp;
            GlobalHook.MouseDownExt += GlobalHookMouseDownExt;
            GlobalHook.MouseUpExt += GlobalHookMouseUpExt;
            GlobalHook.MouseMoveExt += GlobalHookMouseMoveExt;
            GlobalHook.MouseWheelExt += GlobalHookMouseWheelExt;
        }
        TrySubscribeWinePolling();
        //_globalHook.KeyPress += GlobalHookKeyPress;

        _pickUpKey = TaskContext.Instance().Config.KeyBindingsConfig.PickUpOrInteract.ToWinFormKeys();
        _pickUpKeyCode = TaskContext.Instance().Config.KeyBindingsConfig.PickUpOrInteract.ToVK();
        _releaseControlKey = TaskContext.Instance().Config.KeyBindingsConfig.Jump.ToWinFormKeys();
        _releaseControlKeyCode = TaskContext.Instance().Config.KeyBindingsConfig.Jump.ToVK();

        _firstSpaceKeyDownTime = DateTime.MaxValue;
        var si = TaskContext.Instance().Config.MacroConfig.SpaceFireInterval;
        _spaceTimer.Interval = si;
        _spaceTimer.Elapsed += (sender, args) => { Simulation.PostMessage(_hWnd).KeyPress(_releaseControlKeyCode); };

        var fi = TaskContext.Instance().Config.MacroConfig.FFireInterval;
        _fTimer.Interval = fi;
        _fTimer.Elapsed += (sender, args) => { Simulation.PostMessage(_hWnd).KeyPress(_pickUpKeyCode); };
    }

    private void GlobalHookKeyDown(object? sender, KeyEventArgs e)
    {
        // Debug.WriteLine("KeyDown: \t{0}", e.KeyCode);
        GlobalKeyMouseRecord.Instance.GlobalHookKeyDown(e, Kernel32.GetTickCount());

        // 热键按下事件
        HotKeyDown(sender, e);

        if (e.KeyCode == _releaseControlKey)
        {
            if (_firstSpaceKeyDownTime == DateTime.MaxValue)
            {
                _firstSpaceKeyDownTime = DateTime.Now;
            }
            else
            {
                var timeSpan = DateTime.Now - _firstSpaceKeyDownTime;
                if (timeSpan.TotalMilliseconds > 300 && TaskContext.Instance().Config.MacroConfig.SpacePressHoldToContinuationEnabled)
                    if (!_spaceTimer.Enabled)
                        _spaceTimer.Start();
            }
        }
        else if (e.KeyCode == _pickUpKey)
        {
            if (_firstFKeyDownTime == DateTime.MaxValue)
            {
                _firstFKeyDownTime = DateTime.Now;
            }
            else
            {
                var timeSpan = DateTime.Now - _firstFKeyDownTime;
                if (timeSpan.TotalMilliseconds > 200 && TaskContext.Instance().Config.MacroConfig.FPressHoldToContinuationEnabled)
                    if (!_fTimer.Enabled)
                        _fTimer.Start();
            }
        }
    }

    private void GlobalHookKeyUp(object? sender, KeyEventArgs e)
    {
        // Debug.WriteLine("KeyUp: \t{0}", e.KeyCode);
        GlobalKeyMouseRecord.Instance.GlobalHookKeyUp(e, Kernel32.GetTickCount());

        // 热键松开事件
        HotKeyUp(sender, e);

        if (e.KeyCode == _releaseControlKey)
        {
            if (_firstSpaceKeyDownTime != DateTime.MaxValue)
            {
                var timeSpan = DateTime.Now - _firstSpaceKeyDownTime;
                Debug.WriteLine($"Space按下时间：{timeSpan.TotalMilliseconds}ms");
                _firstSpaceKeyDownTime = DateTime.MaxValue;
                _spaceTimer.Stop();
            }
        }
        else if (e.KeyCode == _pickUpKey)
        {
            if (_firstFKeyDownTime != DateTime.MaxValue)
            {
                var timeSpan = DateTime.Now - _firstFKeyDownTime;
                Debug.WriteLine($"F按下时间：{timeSpan.TotalMilliseconds}ms");
                _firstFKeyDownTime = DateTime.MaxValue;
                _fTimer.Stop();
            }
        }
    }

    private void HotKeyDown(object? sender, KeyEventArgs e)
    {
        if (KeyboardHook.AllKeyboardHooks.TryGetValue(e.KeyCode, out var hook)) hook.KeyDown(sender, e);
    }

    private void HotKeyUp(object? sender, KeyEventArgs e)
    {
        if (KeyboardHook.AllKeyboardHooks.TryGetValue(e.KeyCode, out var hook)) hook.KeyUp(sender, e);
    }

    //private void GlobalHookKeyPress(object? sender, KeyPressEventArgs e)
    //{
    //    Debug.WriteLine("KeyPress: \t{0}", e.KeyChar);
    //}

    private void GlobalHookMouseDownExt(object? sender, MouseEventExtArgs e)
    {
        // Debug.WriteLine("MouseDown: {0}; \t Location: {1};\t System Timestamp: {2}", e.Button, e.Location, e.Timestamp);
        GlobalKeyMouseRecord.Instance.GlobalHookMouseDown(e);

        if (e.Button != MouseButtons.Left)
            if (MouseHook.AllMouseHooks.TryGetValue(e.Button, out var hook))
                hook.MouseDown(sender, e);
    }

    private void GlobalHookMouseUpExt(object? sender, MouseEventExtArgs e)
    {
        // Debug.WriteLine("MouseUp: {0}; \t Location: {1};\t System Timestamp: {2}", e.Button, e.Location, e.Timestamp);
        GlobalKeyMouseRecord.Instance.GlobalHookMouseUp(e);

        if (e.Button != MouseButtons.Left)
            if (MouseHook.AllMouseHooks.TryGetValue(e.Button, out var hook))
                hook.MouseUp(sender, e);
    }

    private void GlobalHookMouseMoveExt(object? sender, MouseEventExtArgs e)
    {
        // Debug.WriteLine("MouseMove: {0}; \t Location: {1};\t System Timestamp: {2}", e.Button, e.Location, e.Timestamp);
        GlobalKeyMouseRecord.Instance.GlobalHookMouseMoveTo(e);
    }
    
    private void GlobalHookMouseWheelExt(object? sender, MouseEventExtArgs e)
    {
        // Debug.WriteLine("MouseMove: {0}; \t Location: {1};\t Delta: {2};\t System Timestamp: {3}", e.Button, e.Location, e.Delta, e.Timestamp);
        GlobalKeyMouseRecord.Instance.GlobalHookMouseWheel(e);
    }

    public void Unsubscribe()
    {
        if (_globalHook != null && !WinePlatformAddon.IsRunningOnWine)
        {
            _globalHook.KeyDown -= GlobalHookKeyDown;
            _globalHook.KeyUp -= GlobalHookKeyUp;
            _globalHook.MouseDownExt -= GlobalHookMouseDownExt;
            _globalHook.MouseUpExt -= GlobalHookMouseUpExt;
            _globalHook.MouseMoveExt -= GlobalHookMouseMoveExt;
            _globalHook.MouseWheelExt -= GlobalHookMouseWheelExt;
            //_globalHook.KeyPress -= GlobalHookKeyPress;
            _globalHook.Dispose();
            _globalHook = null;
        }
        if (WinePlatformAddon.IsRunningOnWine){
          DisposeWineAddon();
        }
    }
}
