using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BetterGenshinImpact.GameTask.Music.Service;

/// <summary>
/// 引导式跟弹用的键盘输入捕获。
///
/// 为什么不能复用 <see cref="BetterGenshinImpact.Model.KeyboardHook"/>：
/// 它是热键用的「每个按键只能注册一个实例」的字典（AllKeyboardHooks），
/// 21 个琴键里只要有一个已经被热键占用，注册就会冲突，
/// 而且它会检查 ChatUiHotkeyGuard、只关心 BindKey 一个键，无法用来监听整块琴键。
///
/// 本类使用 WH_KEYBOARD_LL 全局低级键盘钩子：
/// - 纯粹的旁观者：回调里永远不写入 DLL、不返回 1，绝不吞掉或改写玩家的按键，
///   所以不会和游戏输入、BetterGI 自身的模拟按键（PostMessage/SendInput）互相干扰；
/// - 忽略注入事件（LLKHF_INJECTED），避免 BetterGI 自己自动演奏时被误判成玩家按键；
/// - 只在按下沿触发一次（自己维护按下集合，屏蔽系统自动重复）。
///
/// 钩子的 P/Invoke 自己声明（而不是用 Vanara 的 SetWindowsHookEx），
/// 是为了让回调委托的类型在本项目里确定可见，避免依赖第三方库的委托名称。
/// </summary>
public sealed class MusicPlayerInputHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const uint LlkhfInjected = 0x00000010;

    private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    private readonly LowLevelKeyboardProc _proc;
    private readonly object _sync = new();
    private readonly HashSet<Keys> _pressed = [];

    private nint _hook;
    private bool _disposed;

    /// <summary>玩家按下了一个键（已经过滤掉自动重复与注入事件）</summary>
    public event Action<Keys>? KeyPressed;

    /// <summary>玩家松开了一个键</summary>
    public event Action<Keys>? KeyReleased;

    /// <summary>钩子是否已经安装成功</summary>
    public bool IsRunning => _hook != 0;

    public MusicPlayerInputHook()
    {
        _proc = HookCallback;
    }

    /// <summary>
    /// 安装钩子。必须在有消息循环的线程（WPF 里就是主线程）上调用。
    /// 重复调用是安全的。
    /// </summary>
    public bool Start()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MusicPlayerInputHook));
        }

        lock (_sync)
        {
            if (_hook != 0)
            {
                return true;
            }

            _pressed.Clear();
            _hook = SetWindowsHookEx(WhKeyboardLl, _proc, nint.Zero, 0);
            return _hook != 0;
        }
    }

    /// <summary>
    /// 卸载钩子。必须在安装钩子的同一个线程上调用。
    /// </summary>
    public void Stop()
    {
        lock (_sync)
        {
            if (_hook != 0)
            {
                _ = UnhookWindowsHookEx(_hook);
                _hook = 0;
            }

            _pressed.Clear();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode < 0)
        {
            return CallNextHookEx(nint.Zero, nCode, wParam, lParam);
        }

        var message = (int)wParam;
        if (message is not (WmKeyDown or WmKeyUp or WmSysKeyDown or WmSysKeyUp))
        {
            return CallNextHookEx(nint.Zero, nCode, wParam, lParam);
        }

        // KBDLLHOOKSTRUCT: vkCode(0) scanCode(4) flags(8) time(12) dwExtraInfo(16)
        var vkCode = Marshal.ReadInt32(lParam);
        if (vkCode <= 0)
        {
            return CallNextHookEx(nint.Zero, nCode, wParam, lParam);
        }

        var flags = (uint)Marshal.ReadInt32(lParam, 8);
        if ((flags & LlkhfInjected) != 0)
        {
            return CallNextHookEx(nint.Zero, nCode, wParam, lParam);
        }

        var key = (Keys)vkCode;
        if (message is WmKeyDown or WmSysKeyDown)
        {
            var isFirstPress = false;
            lock (_sync)
            {
                isFirstPress = _pressed.Add(key);
            }

            if (isFirstPress)
            {
                RaiseSafely(KeyPressed, key);
            }
        }
        else
        {
            var wasPressed = false;
            lock (_sync)
            {
                wasPressed = _pressed.Remove(key);
            }

            if (wasPressed)
            {
                RaiseSafely(KeyReleased, key);
            }
        }

        // 永远是旁观者：不吞按键，保证游戏和 BetterGI 的输入行为完全不变。
        return CallNextHookEx(nint.Zero, nCode, wParam, lParam);
    }

    private static void RaiseSafely(Action<Keys>? handler, Keys key)
    {
        if (handler == null)
        {
            return;
        }

        foreach (var callback in handler.GetInvocationList())
        {
            try
            {
                ((Action<Keys>)callback)(key);
            }
            catch
            {
                // 钩子回调里绝不能抛出异常，否则会拖垮整个系统输入。
            }
        }
    }
}
