using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using static Windows.Win32.PInvoke;

namespace Vision.WindowCapture.Test;

public partial class PickerWindow : Window
{
    private readonly string[] _ignoreProcesses = { "applicationframehost", "shellexperiencehost", "systemsettings", "winstore.app", "searchui" };

    public PickerWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        FindWindows();
    }

    public IntPtr PickCaptureTarget(IntPtr hWnd)
    {
        new WindowInteropHelper(this).Owner = hWnd;
        ShowDialog();

        return ((CapturableWindow?)WindowList.SelectedItem)?.Handle ?? IntPtr.Zero;
    }

    private unsafe void FindWindows()
    {
        var wih = new WindowInteropHelper(this);
        EnumWindows((hWnd, lParam) =>
        {
            // ignore invisible windows
            if (!IsWindowVisible(hWnd))
                return true;

            // ignore untitled windows
            string title;
            int bufferSize = GetWindowTextLength(hWnd) + 1;
            fixed (char* windowNameChars = new char[bufferSize])
            {
                if (GetWindowText(hWnd, windowNameChars, bufferSize) == 0)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    if (errorCode != 0)
                    {
                        throw new Win32Exception(errorCode);
                    }

                    return true;
                }

                title = new string(windowNameChars);
                if (string.IsNullOrWhiteSpace(title))
                    return true;
            }

            // ignore me
            if (wih.Handle == hWnd)
                return true;

            uint processId;
            GetWindowThreadProcessId(hWnd, &processId);

            // ignore by process name
            var process = Process.GetProcessById((int)processId);
            if (_ignoreProcesses.Contains(process.ProcessName.ToLower()))
                return true;

            WindowList.Items.Add(new CapturableWindow
            {
                Handle = hWnd,
                Name = $"{title} ({process.ProcessName}.exe)"
            });

            return true;
        }, IntPtr.Zero);
    }

    private void WindowsOnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        Close();
    }
}

public struct CapturableWindow
{
    public string Name { get; set; }
    public IntPtr Handle { get; set; }
}
