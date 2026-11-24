using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Vanara.PInvoke;

namespace BetterGenshinImpact.View.Controls;

internal sealed class TrayIcon : IDisposable
{
    private const uint IconId = 1;
    private const int CallbackMessage = (int)User32.WindowMessage.WM_APP + 1;
    private const int MaxRegistrationRetries = 8;

    private readonly ILogger<TrayIcon> _logger = App.GetLogger<TrayIcon>();
    private readonly ContextMenu _menu;
    private readonly Action _onLeftDoubleClick;
    private readonly HwndSource _messageWindow;
    private NotifyIconData _iconData;
    private nint _iconHandle;
    private readonly uint _taskbarCreatedMessage;
    private readonly DispatcherTimer _registrationTimer;
    private int _registrationRetriesRemaining;
    private bool _disposed;

    /// <param name="menu">右键托盘图标时打开的菜单</param>
    /// <param name="onLeftDoubleClick">双击托盘图标时的回调</param>
    /// <param name="icon">托盘图标，多实例下随实例身份变化，之后通过 <see cref="Update"/> 更新</param>
    /// <param name="tooltip">托盘提示文本</param>
    public TrayIcon(ContextMenu menu, Action onLeftDoubleClick, ImageSource icon, string tooltip)
    {
        _menu = menu;
        _onLeftDoubleClick = onLeftDoubleClick;
        _iconHandle = CreateIconHandle(icon);

        // TaskbarCreated 广播给顶层窗口，主窗口收进托盘后仍需接收该消息。
        _messageWindow = new HwndSource(new HwndSourceParameters("BetterGI Tray Icon")
        {
            WindowStyle = unchecked((int)User32.WindowStyles.WS_POPUP),
            ExtendedWindowStyle = (int)User32.WindowStylesEx.WS_EX_TOOLWINDOW,
        });
        _iconData = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            Window = _messageWindow.Handle,
            Id = IconId,
            Flags = NotifyIconFlags.Message | NotifyIconFlags.Icon | NotifyIconFlags.Tip,
            CallbackMessage = CallbackMessage,
            Icon = _iconHandle,
            Tip = TrimTooltip(tooltip),
        };

        _taskbarCreatedMessage = User32.RegisterWindowMessage("TaskbarCreated");
        var filter = new User32.CHANGEFILTERSTRUCT { cbSize = (uint)Marshal.SizeOf<User32.CHANGEFILTERSTRUCT>() };
        // BetterGI 以管理员权限运行，只允许 Explorer 的任务栏重建消息通过此窗口的 UIPI 筛选。
        if (_taskbarCreatedMessage == 0 || !User32.ChangeWindowMessageFilterEx(
                _messageWindow.Handle, _taskbarCreatedMessage, User32.MessageFilterExAction.MSGFLT_ALLOW, ref filter))
        {
            _logger.LogWarning("无法为托盘窗口启用 TaskbarCreated 消息，Win32 错误码 {Error}", Marshal.GetLastWin32Error());
        }

        _registrationTimer = new DispatcherTimer(DispatcherPriority.Background, _messageWindow.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _registrationTimer.Tick += OnRegistrationTick;
        _messageWindow.AddHook(WindowProc);
        if (!TryRegisterIcon())
        {
            ScheduleRegistration();
        }
    }

    /// <summary>
    /// 更新托盘图标与提示文本。必须在 UI 线程调用。
    /// </summary>
    public void Update(ImageSource icon, string tooltip)
    {
        if (_disposed)
        {
            return;
        }

        var oldIconHandle = _iconHandle;
        _iconHandle = CreateIconHandle(icon);
        _iconData.Icon = _iconHandle;
        _iconData.Tip = TrimTooltip(tooltip);
        if (!Shell_NotifyIconW(NotifyIconCommand.Modify, in _iconData))
        {
            // 任务栏尚未就绪时交给重试逻辑重新注册
            ScheduleRegistration();
        }

        DestroyIconHandle(oldIconHandle);
    }

    private nint WindowProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (_taskbarCreatedMessage != 0 && message == _taskbarCreatedMessage)
        {
            // 等任务栏完成初始化后，使用原有窗口和图标 ID 重新注册。
            ScheduleRegistration();
        }
        else if (message == CallbackMessage && wParam == IconId)
        {
            switch ((User32.WindowMessage)(uint)lParam)
            {
                case User32.WindowMessage.WM_LBUTTONDBLCLK:
                    _onLeftDoubleClick();
                    break;
                case User32.WindowMessage.WM_RBUTTONDOWN:
                    _ = User32.SetForegroundWindow(hwnd);
                    _menu.UpdateDefaultStyle();
                    _menu.IsOpen = true;
                    break;
            }

            handled = true;
        }

        return 0;
    }

    private void ScheduleRegistration()
    {
        _registrationRetriesRemaining = MaxRegistrationRetries;
        _registrationTimer.Start();
    }

    private bool TryRegisterIcon()
    {
        // 任务栏也可能在 DPI 变化时广播消息，此时更新现有图标即可。
        return Shell_NotifyIconW(NotifyIconCommand.Modify, in _iconData)
               || Shell_NotifyIconW(NotifyIconCommand.Add, in _iconData);
    }

    private void OnRegistrationTick(object? sender, EventArgs e)
    {
        if (TryRegisterIcon())
        {
            _registrationTimer.Stop();
        }
        else if (--_registrationRetriesRemaining == 0)
        {
            _registrationTimer.Stop();
            _logger.LogWarning("任务栏未接受托盘图标注册");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _registrationTimer.Stop();
        _registrationTimer.Tick -= OnRegistrationTick;
        _messageWindow.RemoveHook(WindowProc);
        _menu.IsOpen = false;
        _ = Shell_NotifyIconW(NotifyIconCommand.Delete, in _iconData);
        _messageWindow.Dispose();
        DestroyIconHandle(_iconHandle);
        _iconHandle = 0;
    }

    /// <summary>
    /// 把 WPF 图像渲染为系统小图标尺寸的 HICON，调用方负责用 <see cref="DestroyIconHandle"/> 释放。
    /// </summary>
    private static nint CreateIconHandle(ImageSource source)
    {
        var size = System.Windows.Forms.SystemInformation.SmallIconSize;
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var drawingContext = visual.RenderOpen())
        {
            drawingContext.DrawImage(source, new Rect(0d, 0d, size.Width, size.Height));
        }

        var bitmap = new RenderTargetBitmap(size.Width, size.Height, 96d, 96d, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
        using var drawingBitmap = new System.Drawing.Bitmap(stream);
        return drawingBitmap.GetHicon();
    }

    private static void DestroyIconHandle(nint iconHandle)
    {
        if (iconHandle != 0)
        {
            _ = DestroyIcon(iconHandle);
        }
    }

    /// <summary>
    /// NOTIFYICONDATA.szTip 最多 128 个字符（含结尾的 \0）。
    /// </summary>
    private static string TrimTooltip(string tooltip)
    {
        return tooltip.Length > 127 ? tooltip[..127] : tooltip;
    }

    private enum NotifyIconCommand : uint
    {
        Add = 0,
        Modify = 1,
        Delete = 2,
    }

    [Flags]
    private enum NotifyIconFlags : uint
    {
        Message = 1,
        Icon = 2,
        Tip = 4,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public NotifyIconFlags Flags;
        public int CallbackMessage;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string? Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string? InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public nint BalloonIcon;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint iconHandle);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(NotifyIconCommand command, in NotifyIconData data);
}
