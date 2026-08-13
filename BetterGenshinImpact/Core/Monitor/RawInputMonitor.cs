using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Threading;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Monitor;

/// <summary>
/// 基于 Raw Input 的鼠标相对移动、键盘按键、鼠标按钮与滚轮监听。
/// 相对鼠标移动订阅与键盘/按钮/滚轮订阅统一共享采集生命周期：
/// 首个订阅启动采集线程，最后一个订阅释放后停止。
/// </summary>
public sealed class RawInputMonitor(
    ILogger<RawInputMonitor> logger) : IRelativeMouseInputMonitor, IRawKeyboardInputMonitor, IDisposable
{
    private static readonly TimeSpan InitializationTimeout = TimeSpan.FromSeconds(10);
    private const ushort GenericDesktopUsagePage = 0x01;
    private const ushort MouseUsage = 0x02;
    private const ushort KeyboardUsage = 0x06;
    private const ushort KeyBreakFlag = 0x01;
    private const ushort InvalidVirtualKey = 0x00FF;
    private static readonly nint HwndMessage = new(-3);

    // Raw Input 鼠标按钮/滚轮标志（RI_MOUSE_*）
    private const ushort MouseButton1Down = 0x0001;
    private const ushort MouseButton1Up = 0x0002;
    private const ushort MouseButton2Down = 0x0004;
    private const ushort MouseButton2Up = 0x0008;
    private const ushort MouseButton3Down = 0x0010;
    private const ushort MouseButton3Up = 0x0020;
    private const ushort MouseButton4Down = 0x0040;
    private const ushort MouseButton4Up = 0x0080;
    private const ushort MouseButton5Down = 0x0100;
    private const ushort MouseButton5Up = 0x0200;
    private const ushort MouseWheel = 0x0400;

    private readonly object _sourceLock = new();
    private readonly Dictionary<long, EventHandler<RelativeMouseMoveEventArgs>> _moveHandlers = [];
    private readonly Dictionary<long, EventHandler<RawKeyboardInputEventArgs>> _keyboardHandlers = [];
    private readonly Dictionary<long, EventHandler<RawMouseButtonEventArgs>> _mouseButtonHandlers = [];
    private readonly Dictionary<long, EventHandler<RawMouseWheelEventArgs>> _mouseWheelHandlers = [];
    private long _nextSubscriptionId;
    private bool _isStarted;
    private bool _isStopping;
    private bool _isDisposed;

    private RawInputThreadContext? _context;
    private long _lifecycleVersion;

    private ILogger Logger { get; } = logger;

    public IDisposable Subscribe(EventHandler<RelativeMouseMoveEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return AddSubscription(_moveHandlers, handler);
    }

    public IDisposable SubscribeKeyboard(EventHandler<RawKeyboardInputEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return AddSubscription(_keyboardHandlers, handler);
    }

    public IDisposable SubscribeMouseButton(EventHandler<RawMouseButtonEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return AddSubscription(_mouseButtonHandlers, handler);
    }

    public IDisposable SubscribeMouseWheel(EventHandler<RawMouseWheelEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return AddSubscription(_mouseWheelHandlers, handler);
    }

    private IDisposable AddSubscription<TEventArgs>(Dictionary<long, EventHandler<TEventArgs>> handlers,
        EventHandler<TEventArgs> handler)
        where TEventArgs : EventArgs
    {
        long subscriptionId;
        lock (_sourceLock)
        {
            while (_isStopping)
            {
                System.Threading.Monitor.Wait(_sourceLock);
            }

            ObjectDisposedException.ThrowIf(_isDisposed, this);

            subscriptionId = ++_nextSubscriptionId;
            handlers.Add(subscriptionId, handler);

            if (!_isStarted)
            {
                try
                {
                    StartCore();
                    _isStarted = true;
                }
                catch
                {
                    handlers.Remove(subscriptionId);
                    throw;
                }
            }
            else if (ReferenceEquals(handlers, _keyboardHandlers) && !_context!.KeyboardRegistered)
            {
                // 采集线程已运行（可能仅因鼠标订阅启动），补充注册键盘设备
                try
                {
                    RegisterKeyboardRawInput(_context!);
                    _context!.KeyboardRegistered = true;
                }
                catch
                {
                    handlers.Remove(subscriptionId);
                    throw;
                }
            }
        }

        return new Subscription<TEventArgs>(this, subscriptionId, handlers);
    }

    private void StartCore()
    {
        RawInputThreadContext context;
        lock (_sourceLock)
        {
            _lifecycleVersion++;
            if (_context != null)
            {
                return;
            }

            context = new RawInputThreadContext();
            context.Thread = new Thread(() => RunMessageLoop(context))
            {
                IsBackground = true,
                Name = "BetterGI RawInput Monitor"
            };
            context.Thread.SetApartmentState(ApartmentState.STA);
            context.RequireKeyboard = _keyboardHandlers.Count > 0;
            _context = context;
        }

        try
        {
            context.Thread.Start();
        }
        catch
        {
            ClearContext(context);
            context.Initialized.Dispose();
            throw;
        }

        if (!context.Initialized.Wait(InitializationTimeout))
        {
            ClearContext(context);
            RequestStop(context);
            throw new TimeoutException(
                $"Raw Input 采集线程初始化超过 {InitializationTimeout.TotalSeconds:0} 秒。");
        }
        context.Initialized.Dispose();

        if (context.InitializationException == null)
        {
            return;
        }

        ClearContext(context);

        throw new InvalidOperationException("Raw Input 初始化失败", context.InitializationException);
    }

    private void StopCore()
    {
        RawInputThreadContext? context;
        long stopVersion;
        lock (_sourceLock)
        {
            stopVersion = ++_lifecycleVersion;
            context = _context;
        }

        if (context?.Dispatcher == null || context.Thread == null)
        {
            return;
        }

        if (Thread.CurrentThread == context.Thread)
        {
            _ = context.Dispatcher.BeginInvoke(
                DispatcherPriority.Send,
                new Action(() => StopOnMessageThread(context, stopVersion)));
            return;
        }

        bool stopped;
        try
        {
            stopped = context.Dispatcher.Invoke(
                () => StopOnMessageThread(context, stopVersion),
                DispatcherPriority.Send);
        }
        catch (TaskCanceledException)
        {
            return;
        }
        catch (InvalidOperationException)
        {
            return;
        }

        if (stopped)
        {
            context.Thread.Join();
        }
    }

    internal static bool TryGetRelativeMovement(
        in User32.RAWINPUT rawInput,
        out int deltaX,
        out int deltaY)
    {
        deltaX = 0;
        deltaY = 0;

        if (rawInput.header.dwType != User32.RIM_TYPE.RIM_TYPEMOUSE)
        {
            return false;
        }

        var mouse = rawInput.data.mouse;
        if ((mouse.usFlags & User32.MouseState.MOUSE_MOVE_ABSOLUTE) != 0)
        {
            return false;
        }

        deltaX = mouse.lLastX;
        deltaY = mouse.lLastY;
        return deltaX != 0 || deltaY != 0;
    }

    internal static bool TryGetRelativeMovementFromBuffer(
        nint rawInputBuffer,
        uint rawInputSize,
        out int deltaX,
        out int deltaY)
    {
        deltaX = 0;
        deltaY = 0;

        var headerSize = Marshal.SizeOf<User32.RAWINPUTHEADER>();
        var mouseSize = Marshal.SizeOf<RawMouseData>();
        if (rawInputSize < headerSize + mouseSize)
        {
            return false;
        }

        var header = Marshal.PtrToStructure<User32.RAWINPUTHEADER>(rawInputBuffer);
        if (header.dwType != User32.RIM_TYPE.RIM_TYPEMOUSE)
        {
            return false;
        }

        var mouse = Marshal.PtrToStructure<RawMouseData>(IntPtr.Add(rawInputBuffer, headerSize));
        if ((mouse.Flags & (ushort)User32.MouseState.MOUSE_MOVE_ABSOLUTE) != 0)
        {
            return false;
        }

        deltaX = mouse.LastX;
        deltaY = mouse.LastY;
        return deltaX != 0 || deltaY != 0;
    }

    internal static bool TryGetKeyboardInputFromBuffer(
        nint rawInputBuffer,
        uint rawInputSize,
        out ushort virtualKey,
        out ushort scanCode,
        out ushort flags,
        out bool isKeyDown)
    {
        virtualKey = 0;
        scanCode = 0;
        flags = 0;
        isKeyDown = false;

        var headerSize = Marshal.SizeOf<User32.RAWINPUTHEADER>();
        var keyboardSize = Marshal.SizeOf<RawKeyboardData>();
        if (rawInputSize < headerSize + keyboardSize)
        {
            return false;
        }

        var header = Marshal.PtrToStructure<User32.RAWINPUTHEADER>(rawInputBuffer);
        if (header.dwType != User32.RIM_TYPE.RIM_TYPEKEYBOARD)
        {
            return false;
        }

        var keyboard = Marshal.PtrToStructure<RawKeyboardData>(
            IntPtr.Add(rawInputBuffer, headerSize));
        if (keyboard.VirtualKey == InvalidVirtualKey)
        {
            return false;
        }

        virtualKey = keyboard.VirtualKey;
        scanCode = keyboard.MakeCode;
        flags = keyboard.Flags;
        isKeyDown = (keyboard.Flags & KeyBreakFlag) == 0;
        return true;
    }

    /// <summary>
    /// 从 Raw Input 鼠标数据解析按钮与滚轮事件，按出现顺序发布到 out 集合。
    /// </summary>
    private static void ParseMouseButtons(
        RawMouseData mouse,
        List<(MouseButtons button, bool isDown)>? buttonEvents,
        out int wheelDelta)
    {
        wheelDelta = 0;

        if (buttonEvents != null)
        {
            if ((mouse.ButtonFlags & MouseButton1Down) != 0)
            {
                buttonEvents.Add((MouseButtons.Left, true));
            }
            else if ((mouse.ButtonFlags & MouseButton1Up) != 0)
            {
                buttonEvents.Add((MouseButtons.Left, false));
            }

            if ((mouse.ButtonFlags & MouseButton2Down) != 0)
            {
                buttonEvents.Add((MouseButtons.Right, true));
            }
            else if ((mouse.ButtonFlags & MouseButton2Up) != 0)
            {
                buttonEvents.Add((MouseButtons.Right, false));
            }

            if ((mouse.ButtonFlags & MouseButton3Down) != 0)
            {
                buttonEvents.Add((MouseButtons.Middle, true));
            }
            else if ((mouse.ButtonFlags & MouseButton3Up) != 0)
            {
                buttonEvents.Add((MouseButtons.Middle, false));
            }

            if ((mouse.ButtonFlags & MouseButton4Down) != 0)
            {
                buttonEvents.Add((MouseButtons.XButton1, true));
            }
            else if ((mouse.ButtonFlags & MouseButton4Up) != 0)
            {
                buttonEvents.Add((MouseButtons.XButton1, false));
            }

            if ((mouse.ButtonFlags & MouseButton5Down) != 0)
            {
                buttonEvents.Add((MouseButtons.XButton2, true));
            }
            else if ((mouse.ButtonFlags & MouseButton5Up) != 0)
            {
                buttonEvents.Add((MouseButtons.XButton2, false));
            }
        }

        if ((mouse.ButtonFlags & MouseWheel) != 0)
        {
            wheelDelta = (short)mouse.ButtonData;
        }
    }

    private void RunMessageLoop(RawInputThreadContext context)
    {
        try
        {
            context.Dispatcher = Dispatcher.CurrentDispatcher;
            context.HwndSource = new HwndSource(new HwndSourceParameters("BetterGI RawInput Monitor")
            {
                ParentWindow = HwndMessage,
                WindowStyle = 0
            });
            context.HwndSource.AddHook(WindowProc);

            RegisterRawInput(context);
            context.Registered = true;
            if (context.RequireKeyboard)
            {
                RegisterKeyboardRawInput(context);
                context.KeyboardRegistered = true;
            }

            if (Volatile.Read(ref context.StopRequested) != 0)
            {
                throw new OperationCanceledException("Raw Input 初始化已取消。");
            }

            context.InitializationSucceeded = true;
            context.Initialized.Set();

            Dispatcher.Run();
        }
        catch (Exception ex)
        {
            if (!context.InitializationSucceeded)
            {
                context.InitializationException = ex;
                context.Initialized.Set();
            }
            Logger.LogError(ex, "Raw Input 消息窗口异常终止");
        }
        finally
        {
            CleanupContext(context);

            lock (_sourceLock)
            {
                if (ReferenceEquals(_context, context))
                {
                    _context = null;
                    _lifecycleVersion++;
                }
            }
        }
    }

    private bool StopOnMessageThread(RawInputThreadContext context, long stopVersion)
    {
        lock (_sourceLock)
        {
            if (stopVersion != _lifecycleVersion || !ReferenceEquals(_context, context))
            {
                return false;
            }

            CleanupContext(context);
            _context = null;
            context.Dispatcher?.BeginInvokeShutdown(DispatcherPriority.Send);
            return true;
        }
    }

    private void ClearContext(RawInputThreadContext context)
    {
        lock (_sourceLock)
        {
            if (ReferenceEquals(_context, context))
            {
                _context = null;
                _lifecycleVersion++;
            }
        }
    }

    private static void RequestStop(RawInputThreadContext context)
    {
        Interlocked.Exchange(ref context.StopRequested, 1);
        try
        {
            context.Dispatcher?.BeginInvokeShutdown(DispatcherPriority.Send);
        }
        catch (InvalidOperationException)
        {
            // Dispatcher 已关闭，无需再次请求退出。
        }
    }

    private void RegisterRawInput(RawInputThreadContext context)
    {
        var devices = new[]
        {
            new User32.RAWINPUTDEVICE
            {
                usUsagePage = GenericDesktopUsagePage,
                usUsage = MouseUsage,
                dwFlags = User32.RIDEV.RIDEV_INPUTSINK,
                hwndTarget = context.HwndSource!.Handle
            }
        };

        if (!User32.RegisterRawInputDevices(
                devices,
                (uint)devices.Length,
                (uint)Marshal.SizeOf<User32.RAWINPUTDEVICE>()))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "注册 Raw Input 鼠标设备失败");
        }
    }

    private void RegisterKeyboardRawInput(RawInputThreadContext context)
    {
        var devices = new[]
        {
            new User32.RAWINPUTDEVICE
            {
                usUsagePage = GenericDesktopUsagePage,
                usUsage = KeyboardUsage,
                dwFlags = User32.RIDEV.RIDEV_INPUTSINK,
                hwndTarget = context.HwndSource!.Handle
            }
        };

        if (!User32.RegisterRawInputDevices(
                devices,
                (uint)devices.Length,
                (uint)Marshal.SizeOf<User32.RAWINPUTDEVICE>()))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "注册 Raw Input 键盘设备失败");
        }
    }

    private void UnregisterKeyboardRawInput()
    {
        var devices = new[]
        {
            new User32.RAWINPUTDEVICE
            {
                usUsagePage = GenericDesktopUsagePage,
                usUsage = KeyboardUsage,
                dwFlags = User32.RIDEV.RIDEV_REMOVE,
                hwndTarget = HWND.NULL
            }
        };

        if (!User32.RegisterRawInputDevices(
                devices,
                (uint)devices.Length,
                (uint)Marshal.SizeOf<User32.RAWINPUTDEVICE>()))
        {
            Logger.LogWarning(
                "注销 Raw Input 键盘设备失败，Win32Error: {Win32Error}",
                Marshal.GetLastWin32Error());
        }
    }

    private void CleanupContext(RawInputThreadContext context)
    {
        if (context.Registered)
        {
            var devices = new[]
            {
                new User32.RAWINPUTDEVICE
                {
                    usUsagePage = GenericDesktopUsagePage,
                    usUsage = MouseUsage,
                    dwFlags = User32.RIDEV.RIDEV_REMOVE,
                    hwndTarget = HWND.NULL
                }
            };

            if (!User32.RegisterRawInputDevices(
                    devices,
                    (uint)devices.Length,
                    (uint)Marshal.SizeOf<User32.RAWINPUTDEVICE>()))
            {
                Logger.LogWarning(
                    "注销 Raw Input 鼠标设备失败，Win32Error: {Win32Error}",
                    Marshal.GetLastWin32Error());
            }

            context.Registered = false;
        }

        if (context.KeyboardRegistered)
        {
            UnregisterKeyboardRawInput();
            context.KeyboardRegistered = false;
        }

        if (context.HwndSource != null)
        {
            context.HwndSource.RemoveHook(WindowProc);
            context.HwndSource.Dispose();
            context.HwndSource = null;
        }
    }

    private nint WindowProc(
        nint hwnd,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (message != (int)User32.WindowMessage.WM_INPUT)
        {
            return IntPtr.Zero;
        }

        try
        {
            ProcessRawInput(lParam);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "读取 Raw Input 数据失败");
        }

        // 保持 handled = false，让默认窗口过程完成 WM_INPUT 的必要清理。
        handled = false;
        return IntPtr.Zero;
    }

    private void ProcessRawInput(nint rawInputHandle)
    {
        uint size = 0;
        uint headerSize = (uint)Marshal.SizeOf<User32.RAWINPUTHEADER>();
        var handle = new User32.HRAWINPUT(rawInputHandle);

        if (User32.GetRawInputData(
                handle,
                User32.RID.RID_INPUT,
                IntPtr.Zero,
                ref size,
                headerSize) == uint.MaxValue)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        if (size == 0)
        {
            return;
        }

        var buffer = Marshal.AllocHGlobal(checked((int)size));
        try
        {
            var readSize = size;
            uint result = User32.GetRawInputData(
                handle,
                User32.RID.RID_INPUT,
                buffer,
                ref readSize,
                headerSize);
            if (result == uint.MaxValue)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var timestamp = DateTime.UtcNow;
            var header = Marshal.PtrToStructure<User32.RAWINPUTHEADER>(buffer);
            if (header.dwType == User32.RIM_TYPE.RIM_TYPEMOUSE)
            {
                ProcessMouseInput(buffer, readSize, timestamp);
            }
            else if (header.dwType == User32.RIM_TYPE.RIM_TYPEKEYBOARD)
            {
                ProcessKeyboardInput(buffer, readSize, timestamp);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void ProcessMouseInput(nint buffer, uint readSize, DateTime timestamp)
    {
        var headerSize = Marshal.SizeOf<User32.RAWINPUTHEADER>();
        var mouseSize = Marshal.SizeOf<RawMouseData>();
        if (readSize < headerSize + mouseSize)
        {
            // 缓冲区不足以容纳 RAWINPUTHEADER + RawMouseData，跳过本次解析。
            return;
        }

        if (TryGetRelativeMovementFromBuffer(buffer, readSize, out int deltaX, out int deltaY))
        {
            Publish(_moveHandlers, new RelativeMouseMoveEventArgs(deltaX, deltaY, timestamp));
        }

        var mouse = Marshal.PtrToStructure<RawMouseData>(IntPtr.Add(buffer, headerSize));

        var buttonEvents = new List<(MouseButtons button, bool isDown)>();
        ParseMouseButtons(mouse, buttonEvents, out int wheelDelta);
        foreach (var (button, isDown) in buttonEvents)
        {
            Publish(_mouseButtonHandlers, new RawMouseButtonEventArgs(button, isDown));
        }

        if (wheelDelta != 0)
        {
            Publish(_mouseWheelHandlers, new RawMouseWheelEventArgs(wheelDelta));
        }
    }

    private void ProcessKeyboardInput(nint buffer, uint readSize, DateTime timestamp)
    {
        if (!TryGetKeyboardInputFromBuffer(buffer, readSize, out ushort virtualKey, out ushort scanCode,
                out ushort flags, out bool isKeyDown))
        {
            return;
        }

        Publish(_keyboardHandlers, new RawKeyboardInputEventArgs(virtualKey, scanCode, flags, isKeyDown, timestamp));
    }

    private void Publish<TEventArgs>(Dictionary<long, EventHandler<TEventArgs>> handlers,
        TEventArgs eventArgs)
        where TEventArgs : EventArgs
    {
        EventHandler<TEventArgs>[] snapshot;
        lock (_sourceLock)
        {
            if (_isDisposed || handlers.Count == 0)
            {
                return;
            }

            snapshot = [.. handlers.Values];
        }

        foreach (var handler in snapshot)
        {
            try
            {
                handler(this, eventArgs);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Raw Input 事件订阅方执行失败");
            }
        }
    }

    public void Dispose()
    {
        bool shouldStop;
        lock (_sourceLock)
        {
            while (_isStopping)
            {
                System.Threading.Monitor.Wait(_sourceLock);
            }

            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _moveHandlers.Clear();
            _keyboardHandlers.Clear();
            _mouseButtonHandlers.Clear();
            _mouseWheelHandlers.Clear();
            shouldStop = _isStarted;
            _isStarted = false;
            _isStopping = shouldStop;
        }

        if (shouldStop)
        {
            try
            {
                StopCore();
            }
            finally
            {
                CompleteStop();
            }
        }

        GC.SuppressFinalize(this);
    }

    private void Unsubscribe<TEventArgs>(long subscriptionId, Dictionary<long, EventHandler<TEventArgs>> handlers)
        where TEventArgs : EventArgs
    {
        bool shouldStop;
        bool shouldUnregisterKeyboard;
        lock (_sourceLock)
        {
            if (_isDisposed || !handlers.Remove(subscriptionId))
            {
                return;
            }

            shouldStop = handlers.Count == 0
                         && _moveHandlers.Count == 0
                         && _keyboardHandlers.Count == 0
                         && _mouseButtonHandlers.Count == 0
                         && _mouseWheelHandlers.Count == 0
                         && _isStarted;

            // 键盘订阅全部释放但采集线程仍因鼠标订阅存活时，注销键盘设备，
            // 避免覆盖 RDP ActiveX 等依赖进程级键盘 Raw Input 的目标。
            shouldUnregisterKeyboard = !shouldStop
                                       && ReferenceEquals(handlers, _keyboardHandlers)
                                       && _keyboardHandlers.Count == 0
                                       && _context?.KeyboardRegistered == true;

            if (shouldStop)
            {
                _isStarted = false;
                _isStopping = true;
            }
        }

        if (shouldUnregisterKeyboard)
        {
            // 注销键盘设备并复位标志需在同一锁内完成，防止与并发的新键盘订阅竞态：
            // 若先注销设备、后清标志，期间新的键盘订阅可能误判 KeyboardRegistered==true 而跳过注册。
            lock (_sourceLock)
            {
                if (_keyboardHandlers.Count == 0 && _context?.KeyboardRegistered == true)
                {
                    UnregisterKeyboardRawInput();
                    _context!.KeyboardRegistered = false;
                }
            }
        }

        if (shouldStop)
        {
            try
            {
                StopCore();
            }
            finally
            {
                CompleteStop();
            }
        }
    }

    private void CompleteStop()
    {
        lock (_sourceLock)
        {
            _isStopping = false;
            System.Threading.Monitor.PulseAll(_sourceLock);
        }
    }

    private sealed class Subscription<TEventArgs>(
        RawInputMonitor owner,
        long subscriptionId,
        Dictionary<long, EventHandler<TEventArgs>> handlers) : IDisposable
        where TEventArgs : EventArgs
    {
        private RawInputMonitor? _owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.Unsubscribe(subscriptionId, handlers);
        }
    }

    private sealed class RawInputThreadContext
    {
        public Thread? Thread { get; set; }

        public Dispatcher? Dispatcher { get; set; }

        public HwndSource? HwndSource { get; set; }

        public ManualResetEventSlim Initialized { get; } = new();

        public Exception? InitializationException { get; set; }

        public bool InitializationSucceeded { get; set; }

        public bool Registered { get; set; }

        /// <summary>初始化时是否需要注册键盘设备（在锁内快照，消息线程只读，避免初始化路径拿锁）。</summary>
        public bool RequireKeyboard { get; set; }

        /// <summary>键盘 Raw Input 设备是否已注册（独立于整体生命周期，跟随键盘订阅）。</summary>
        public bool KeyboardRegistered { get; set; }

        public int StopRequested;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct RawMouseData
    {
        public ushort Flags { get; init; }

        public ushort Reserved { get; init; }

        public ushort ButtonFlags { get; init; }

        public ushort ButtonData { get; init; }

        public uint RawButtons { get; init; }

        public int LastX { get; init; }

        public int LastY { get; init; }

        public uint ExtraInformation { get; init; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct RawKeyboardData
    {
        public ushort MakeCode { get; init; }

        public ushort Flags { get; init; }

        public ushort Reserved { get; init; }

        public ushort VirtualKey { get; init; }

        public uint Message { get; init; }

        public uint ExtraInformation { get; init; }
    }
}
