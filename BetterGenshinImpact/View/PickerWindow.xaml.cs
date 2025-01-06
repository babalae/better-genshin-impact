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
using System.Runtime.InteropServices;

namespace BetterGenshinImpact.View;

public partial class PickerWindow : Window
{
    private static readonly string[] _ignoreProcesses = ["applicationframehost", "shellexperiencehost", "systemsettings", "winstore.app", "searchui"];
    private bool _isSelected = false;
    public PickerWindow()
    {
        InitializeComponent();
        this.InitializeDpiAwareness();
        Loaded += OnLoaded;
    }
    public class CapturableWindow
    {
        public string Name { get; set; }
        public string ProcessName { get; set; }

        public IntPtr Handle { get; set; }
        public ImageSource Icon { get; set; }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        FindWindows();
    }

    public bool PickCaptureTarget(IntPtr hWnd,out IntPtr PickedWindow)
    {
        new WindowInteropHelper(this).Owner = hWnd;
        ShowDialog();
        if(!_isSelected)
        {
            PickedWindow = IntPtr.Zero;
            return false;
        }
        PickedWindow = ((CapturableWindow?)WindowList.SelectedItem)?.Handle ?? IntPtr.Zero;
        return true;
    }

    private unsafe void FindWindows()
    {
        var wih = new WindowInteropHelper(this);
        var windows = new List<CapturableWindow>();
        
        User32.EnumWindows((hWnd, lParam) =>
        {
            if (!User32.IsWindowVisible(hWnd) || wih.Handle == hWnd)
                return true;

            var title = new StringBuilder(1024);
            _ = User32.GetWindowText(hWnd, title, title.Capacity);
            if (string.IsNullOrWhiteSpace(title.ToString()))
                return true;

            _ = User32.GetWindowThreadProcessId(hWnd, out var processId);
            var process = Process.GetProcessById((int)processId);
            if (_ignoreProcesses.Contains(process.ProcessName.ToLower()))
                return true;

            // 获取窗口图标
            var icon = GetWindowIcon((IntPtr)hWnd);
            
            windows.Add(new CapturableWindow
            {
                Handle = (IntPtr)hWnd,
                Name = title.ToString(),
                ProcessName = process.ProcessName,
                Icon = icon
            });

            return true;
        }, IntPtr.Zero);

        WindowList.ItemsSource = windows;
    }
    private ImageSource GetWindowIcon(IntPtr hWnd)
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
    private bool IsGenshinWindow(string windowName)
    {
        // 判断是否包含原神相关的进程名 TODO：更加健壮的判断
        return windowName == "原神";
    }

    private bool AskIsThisGenshinImpact(string windowName)
    {
        var res = MessageBox.Question(
            $"""
            这看起来不像是原神，确定要选择这个窗口吗？
        
            当前选择的窗口：{windowName}
            """,
            "确认选择",
            MessageBoxButton.YesNo,
            MessageBoxResult.No
        );
        return res == MessageBoxResult.Yes;
    }

    private void WindowsOnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var selectedWindow = WindowList.SelectedItem as CapturableWindow;
        if (selectedWindow == null) return;

        // 如果不是原神窗口，询问用户是否确认
        if (!IsGenshinWindow(selectedWindow.Name))
        {
            if (!AskIsThisGenshinImpact(selectedWindow.Name))
            {
                return;
            }
        }
        _isSelected = true;
        Close();
    }
}

public struct CapturableWindow
{
    public string Name { get; set; }
    public IntPtr Handle { get; set; }
}
