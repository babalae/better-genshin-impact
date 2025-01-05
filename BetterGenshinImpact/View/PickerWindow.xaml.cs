using BetterGenshinImpact.Helpers.DpiAwareness;
using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Vanara.PInvoke;

namespace BetterGenshinImpact.View;

public partial class PickerWindow : Window
{
    private static readonly string[] _ignoreProcesses = ["applicationframehost", "shellexperiencehost", "systemsettings", "winstore.app", "searchui"];

    public PickerWindow()
    {
        InitializeComponent();
        this.InitializeDpiAwareness();
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
        User32.EnumWindows((hWnd, lParam) =>
        {
            // ignore invisible windows
            if (!User32.IsWindowVisible(hWnd))
                return true;

            // ignore untitled windows
            var title = new StringBuilder(1024);
            _ = User32.GetWindowText(hWnd, title, title.Capacity);
            if (string.IsNullOrWhiteSpace(title.ToString()))
                return true;

            // ignore me
            if (wih.Handle == hWnd)
                return true;

            _ = User32.GetWindowThreadProcessId(hWnd, out var processId);

            // ignore by process name
            var process = Process.GetProcessById((int)processId);
            if (_ignoreProcesses.Contains(process.ProcessName.ToLower()))
                return true;

            WindowList.Items.Add(new CapturableWindow
            {
                Handle = (IntPtr)hWnd,
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
