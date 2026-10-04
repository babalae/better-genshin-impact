using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Fischless.WindowsInput;
using Microsoft.Win32;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>在 UI 消息线程安装低级输入钩子；不忽略其他程序注入的输入。</summary>
public sealed class PuloniaUserActivityMonitor : IPuloniaUserActivityMonitor, IDisposable
{
    /// <summary>保活键盘回调，防止非托管调用访问已回收的委托。</summary>
    private readonly HookProc _keyboardCallback;
    /// <summary>保活鼠标回调。</summary>
    private readonly HookProc _mouseCallback;
    /// <summary>键盘钩子句柄。</summary>
    private IntPtr _keyboard;
    /// <summary>鼠标钩子句柄。</summary>
    private IntPtr _mouse;
    /// <summary>最近用户活动的单调时钟读数，启动时从零信任开始累计空闲。</summary>
    private long _lastActivity = Environment.TickCount64;
    /// <summary>用户输入版本。</summary>
    private long _version;
    /// <summary>最近经钩子观察到的系统输入时间，用于发现钩子超时失效或漏报。</summary>
    private int _observedInputTime;

    /// <summary>构造保活回调；安装必须由宿主切换到 UI 线程。</summary>
    public PuloniaUserActivityMonitor()
    {
        _keyboardCallback = KeyboardHook;
        _mouseCallback = MouseHook;
    }

    /// <inheritdoc />
    public long ActivityVersion => Interlocked.Read(ref _version);
    /// <inheritdoc />
    public double IdleSeconds => _keyboard == IntPtr.Zero || _mouse == IntPtr.Zero
        ? 0 : Math.Max(0, Environment.TickCount64 - Interlocked.Read(ref _lastActivity)) / 1000d;
    /// <inheritdoc />
    public bool DesktopAvailable
    {
        get
        {
            if (_keyboard == IntPtr.Zero || _mouse == IntPtr.Zero) return false;
            var input = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
            if (!GetLastInputInfo(ref input)) return false;
            if (unchecked((int)input.Time) != Volatile.Read(ref _observedInputTime))
            {
                // GetLastInputInfo 只作保守否决，不单独用它授权空闲，避免把自身输入误当用户活动。
                RecordActivity();
                return false;
            }
            var desktop = OpenInputDesktop(0, false, 1);
            if (desktop == IntPtr.Zero) return false;
            try
            {
                var name = new StringBuilder(256);
                return GetUserObjectInformation(desktop, 2, name, name.Capacity * 2, out _)
                       && string.Equals(name.ToString(), "Default", StringComparison.OrdinalIgnoreCase);
            }
            finally { CloseDesktop(desktop); }
        }
    }

    /// <summary>安装钩子失败时保持保守准入，调用者可展示错误。</summary>
    public void Start()
    {
        if (_keyboard != IntPtr.Zero) return;
        var lastInput = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
        if (!GetLastInputInfo(ref lastInput)) throw new InvalidOperationException("无法读取桌面输入状态。");
        Volatile.Write(ref _observedInputTime, unchecked((int)lastInput.Time));
        _keyboard = SetWindowsHookEx(13, _keyboardCallback, GetModuleHandle(null), 0);
        _mouse = SetWindowsHookEx(14, _mouseCallback, GetModuleHandle(null), 0);
        if (_keyboard == IntPtr.Zero || _mouse == IntPtr.Zero)
        {
            Dispose();
            throw new InvalidOperationException("无法安装用户活动检测钩子，自动任务将保持等待。");
        }
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    /// <summary>会话锁定、解锁或切换都重新计算空闲，避免解锁立即执行。</summary>
    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e) => RecordActivity();
    /// <summary>只更新原子字段，钩子中不执行取消、文件访问或 UI 工作。</summary>
    private void RecordActivity()
    {
        Interlocked.Exchange(ref _lastActivity, Environment.TickCount64);
        Interlocked.Increment(ref _version);
    }
    /// <summary>统计未带本程序标记的键盘输入。</summary>
    private IntPtr KeyboardHook(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            var input = Marshal.PtrToStructure<KeyboardData>(data);
            Volatile.Write(ref _observedInputTime, unchecked((int)input.Time));
            if ((input.Flags & 0x10) == 0 || input.ExtraInfo != InputInjectionTag.Value) RecordActivity();
        }
        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }
    /// <summary>统计未带本程序标记的鼠标输入，包括移动。</summary>
    private IntPtr MouseHook(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            var input = Marshal.PtrToStructure<MouseData>(data);
            Volatile.Write(ref _observedInputTime, unchecked((int)input.Time));
            if ((input.Flags & 1) == 0 || input.ExtraInfo != InputInjectionTag.Value) RecordActivity();
        }
        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }
    /// <summary>在安装线程释放钩子和会话订阅。</summary>
    public void Dispose()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        if (_keyboard != IntPtr.Zero) UnhookWindowsHookEx(_keyboard);
        if (_mouse != IntPtr.Zero) UnhookWindowsHookEx(_mouse);
        _keyboard = _mouse = IntPtr.Zero;
    }

    /// <summary>低级钩子回调契约。</summary>
    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    /// <summary>只读系统最后输入信息。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInput
    {
        /// <summary>结构大小。</summary>
        public uint Size;
        /// <summary>系统输入时间。</summary>
        public uint Time;
    }
    /// <summary>系统键盘消息布局。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardData
    {
        /// <summary>虚拟键码。</summary>
        public uint Key;
        /// <summary>扫描码。</summary>
        public uint Scan;
        /// <summary>注入标志。</summary>
        public uint Flags;
        /// <summary>消息时间。</summary>
        public uint Time;
        /// <summary>输入来源标记。</summary>
        public IntPtr ExtraInfo;
    }
    /// <summary>系统鼠标消息布局。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseData
    {
        /// <summary>屏幕横坐标。</summary>
        public int X;
        /// <summary>屏幕纵坐标。</summary>
        public int Y;
        /// <summary>滚轮或按钮数据。</summary>
        public uint Data;
        /// <summary>注入标志。</summary>
        public uint Flags;
        /// <summary>消息时间。</summary>
        public uint Time;
        /// <summary>输入来源标记。</summary>
        public IntPtr ExtraInfo;
    }
    /// <summary>安装系统钩子。</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SetWindowsHookEx(int kind, HookProc proc, IntPtr module, uint thread);
    /// <summary>卸载系统钩子。</summary>
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    /// <summary>保持系统钩子链。</summary>
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    /// <summary>取得当前模块。</summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? module);
    /// <summary>只读打开交互桌面。</summary>
    [DllImport("user32.dll")] private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    /// <summary>读取桌面名称，识别锁屏和安全桌面。</summary>
    [DllImport("user32.dll", EntryPoint = "GetUserObjectInformationW", CharSet = CharSet.Unicode)] private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder value, int length, out int needed);
    /// <summary>关闭桌面句柄。</summary>
    [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
    /// <summary>检查低级钩子是否遗漏系统已收到的输入。</summary>
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInput input);
}
