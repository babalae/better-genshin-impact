using BetterGenshinImpact.Helpers.DpiAwareness;
using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Vanara.PInvoke;
using System.Windows.Media;
using System.Collections.Generic;
using System.Windows.Media.Imaging;

namespace BetterGenshinImpact.View;

public partial class PickerWindow : Window
{
    private bool _isSelected;
    private readonly bool _captureTest;

    private const User32.WindowStylesEx IgnoreExStyle = User32.WindowStylesEx.WS_EX_TOOLWINDOW |
                                                        User32.WindowStylesEx.WS_EX_NOREDIRECTIONBITMAP |
                                                        User32.WindowStylesEx.WS_EX_LAYERED;

    public PickerWindow(bool captureTest = false)
    {
        InitializeComponent();
        this.InitializeDpiAwareness();
        Loaded += OnLoaded;
        _captureTest = captureTest;
    }

    public class CapturableWindow(IntPtr handle, string name, string processName, ImageSource? icon)
    {
        public IntPtr Handle { get; } = handle;
        public string Name { get; } = name;
        public string ProcessName { get; } = processName;
        public ImageSource? Icon { get; } = icon;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        FindWindows();
    }

    public bool PickCaptureTarget(IntPtr hWnd, out IntPtr pickedWindow)
    {
        new WindowInteropHelper(this).Owner = hWnd;
        ShowDialog();
        if(!_isSelected)
        {
            pickedWindow = IntPtr.Zero;
            return false;
        }
        pickedWindow = ((CapturableWindow?)WindowList.SelectedItem)?.Handle ?? IntPtr.Zero;
        return true;
    }

    private void FindWindows()
    {
        var wih = new WindowInteropHelper(this);
        var windows = new List<CapturableWindow>();

        User32.EnumWindows((hWnd, lParam) =>
        {
            if (!User32.IsWindowVisible(hWnd) || wih.Handle == hWnd)
                return true;

            var exStyle = User32.GetWindowLong<User32.WindowStylesEx>(hWnd, User32.WindowLongFlags.GWL_EXSTYLE);
            if ((exStyle & IgnoreExStyle) != 0)
                return true;

            var title = new StringBuilder(1024);
            _ = User32.GetWindowText(hWnd, title, title.Capacity);
            if (string.IsNullOrWhiteSpace(title.ToString()))
                return true;

            _ = User32.GetWindowThreadProcessId(hWnd, out var processId);
            var process = Process.GetProcessById((int)processId);

            // 获取窗口图标
            var icon = GetWindowIcon((IntPtr)hWnd);

            windows.Add(new CapturableWindow((IntPtr)hWnd, title.ToString(), process.ProcessName, icon));

            return true;
        }, IntPtr.Zero);

        var sortedWindows = windows.OrderByDescending(IsGenshinWindow)
            .ThenByDescending(x => x.Handle).ToList();

        WindowList.ItemsSource = sortedWindows;
    }

    private ImageSource? GetWindowIcon(IntPtr hWnd)
    {
        try
        {
            const int ICON_BIG = 1;    // WM_GETICON large icon constant
            const int ICON_SMALL = 0;   // WM_GETICON small icon constant
            const int GCL_HICON = -14;  // GetClassLong index for icon

            // 尝试获取窗口大图标
            var iconHandle = User32.SendMessage(hWnd, User32.WindowMessage.WM_GETICON, (IntPtr)ICON_BIG, IntPtr.Zero);

            if (iconHandle == IntPtr.Zero)
            {
                // 尝试获取窗口小图标
                iconHandle = User32.SendMessage(hWnd, User32.WindowMessage.WM_GETICON, (IntPtr)ICON_SMALL, IntPtr.Zero);
            }

            if (iconHandle == IntPtr.Zero)
            {
                // 尝试获取窗口类图标
                iconHandle = User32.GetClassLong(hWnd, GCL_HICON);
            }

            if (iconHandle != IntPtr.Zero)
            {
                return Imaging.CreateBitmapSourceFromHIcon(
                    iconHandle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"获取窗口图标失败: {ex.Message}");
        }

        // 如果获取失败，返回一个默认图标或null
        return null;
    }

    private static bool IsGenshinWindow(CapturableWindow window)
    {
        return window is
            {Name: "原神", ProcessName: "YuanShen"} or
            {Name: "云·原神", ProcessName: "Genshin Impact Cloud Game"} or
            {Name: "Genshin Impact", ProcessName: "GenshinImpact"} or
            {Name: "Genshin Impact · Cloud", ProcessName: "Genshin Impact Cloud"};
    }

    private static bool AskIsThisGenshinImpact(CapturableWindow window)
    {
        var res = MessageBox.Question(
            $"""
            这看起来不像是原神，确定要选择这个窗口吗？
        
            当前选择的窗口：{window.Name} ({window.ProcessName})
            """,
            "确认选择",
            MessageBoxButton.YesNo,
            MessageBoxResult.No
        );
        return res == MessageBoxResult.Yes;
    }

    private void WindowsOnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (WindowList.SelectedItem is not CapturableWindow selectedWindow)
            return;

        // 如果不是原神窗口，询问用户是否确认
        if (!_captureTest && !IsGenshinWindow(selectedWindow))
        {
            if (!AskIsThisGenshinImpact(selectedWindow))
            {
                return;
            }
        }
        _isSelected = true;
        Close();
    }
}
