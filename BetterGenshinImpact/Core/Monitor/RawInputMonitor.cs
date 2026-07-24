using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Threading;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Monitor;

public sealed class RawInputMonitor(ILogger<RawInputMonitor> logger)
    : RelativeMouseInputMonitorBase(logger)
{
    private const ushort GenericDesktopUsagePage = 0x01;
    private const ushort MouseUsage = 0x02;
    private const ushort KeyBreakFlag = 0x01;
    private const ushort InvalidVirtualKey = 0x00FF;
    private static readonly nint HwndMessage = new(-3);

    // 键盘输入不再复用相对鼠标监听器的采集生命周期，避免覆盖 RDP ActiveX 的进程级注册。
    private readonly object _sourceLock = new();
    private RawInputThreadContext? _context;
    private long _lifecycleVersion;

    protected override void StartCore()
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
            _context = context;
        }

        context.Thread.Start();
        context.Initialized.Wait();
        context.Initialized.Dispose();

        if (context.InitializationException == null)
        {
            return;
        }

        lock (_sourceLock)
        {
            if (ReferenceEquals(_context, context))
            {
                _context = null;
            }
        }

        throw new InvalidOperationException("Raw Input 初始化失败", context.InitializationException);
    }

    protected override void StopCore()
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
            Logger.LogError(ex, "读取 Raw Input 鼠标数据失败");
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
            if (TryGetRelativeMovementFromBuffer(buffer, readSize, out int deltaX, out int deltaY))
            {
                Publish(new RelativeMouseMoveEventArgs(deltaX, deltaY, timestamp));
                return;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
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
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct RawMouseData
    {
        public ushort Flags { get; init; }

        public ushort Reserved { get; init; }

        public ushort ButtonFlags { get; init; }

        public short ButtonData { get; init; }

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
