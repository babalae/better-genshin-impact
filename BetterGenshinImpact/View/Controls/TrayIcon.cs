using Microsoft.Extensions.Logging;
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
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
    private readonly Icon _icon;
    private readonly HwndSource _messageWindow;
    private readonly NotifyIconData _iconData;
    private readonly uint _taskbarCreatedMessage;
    private readonly DispatcherTimer _registrationTimer;
    private int _registrationRetriesRemaining;
    private bool _disposed;

    public TrayIcon(ContextMenu menu, Action onLeftDoubleClick)
    {
        _menu = menu;
        _onLeftDoubleClick = onLeftDoubleClick;

        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Resources/Images/logo.ico")).Stream;
        _icon = new Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);

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
            Icon = _icon.Handle,
            Tip = "BetterGI",
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
        _icon.Dispose();
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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(NotifyIconCommand command, in NotifyIconData data);
}
