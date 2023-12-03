using MicaSetup.Helper;
using MicaSetup.Natives;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace MicaSetup.Design.Controls;

/// <summary>
/// https://learn.microsoft.com/zh-cn/windows/apps/desktop/modernize/apply-snap-layout-menu
/// </summary>
public sealed class SnapLayout
{
    public bool IsRegistered { get; private set; } = false;
    public bool IsSupported => Environment.OSVersion.Platform == PlatformID.Win32NT && Environment.OSVersion.Version.Build > 20000;
    public static bool IsEnabled { get; } = IsSnapLayoutEnabled();

    private Button? button;
    private bool isButtonFocused;

    private Window? window;

    public void Register(Button button)
    {
        isButtonFocused = false;
        this.button = button;
        IsRegistered = true;
    }

    public nint WndProc(nint hWnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (!IsRegistered) return 0;
        switch ((User32.WindowMessage)msg)
        {
            case User32.WindowMessage.WM_NCLBUTTONDOWN:
                if (IsOverButton(lParam))
                {
                    window ??= Window.GetWindow(button);
                    if (window != null)
                    {
                        WindowSystemCommands.MaximizeOrRestoreWindow(window);
                    }
                    else
                    {
                        if (new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke) is IInvokeProvider invokeProv)
                        {
                            invokeProv?.Invoke();
                        }
                    }
                    handled = true;
                }
                break;

            case User32.WindowMessage.WM_NCMOUSELEAVE:
                DefocusButton();
                break;

            case User32.WindowMessage.WM_NCHITTEST:
                if (IsEnabled)
                {
                    if (IsOverButton(lParam))
                    {
                        FocusButton();
                        handled = true;
                    }
                    else
                    {
                        DefocusButton();
                    }
                }
                return (int)User32.HitTestValues.HTMAXBUTTON;

            case User32.WindowMessage.WM_SETCURSOR:
                if (isButtonFocused)
                {
                    handled = true;
                }
                break;

            default:
                handled = false;
                break;
        }
        return (int)User32.HitTestValues.HTCLIENT;
    }

    private void FocusButton()
    {
        if (isButtonFocused) return;

        if (button != null)
        {
            button.Background = (Brush)Application.Current.FindResource("ButtonBackgroundPointerOver");
            button.BorderBrush = (Brush)Application.Current.FindResource("ButtonBorderBrushPointerOver");
            button.Foreground = (Brush)Application.Current.FindResource("ButtonForegroundPointerOver");
        }
        isButtonFocused = true;
    }

    private void DefocusButton()
    {
        if (!isButtonFocused) return;

        button?.ClearValue(Control.BackgroundProperty);
        button?.ClearValue(Control.BorderBrushProperty);
        button?.ClearValue(Control.ForegroundProperty);
        isButtonFocused = false;
    }

    private bool IsOverButton(nint lParam)
    {
        if (button == null)
        {
            return false;
        }

        try
        {
            int x = Macro.GET_X_LPARAM(lParam);
            int y = Macro.GET_Y_LPARAM(lParam);

            Rect rect = new(button.PointToScreen(new Point()), new Size(DpiHelper.CalcDPIX(button.ActualWidth), DpiHelper.CalcDPIY(button.ActualHeight)));

            if (rect.Contains(new Point(x, y)))
            {
                return true;
            }
        }
        catch (OverflowException)
        {
            return true;
        }
        return false;
    }

    private static bool IsSnapLayoutEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", true);
            object? registryValueObject = key?.GetValue("EnableSnapAssistFlyout");

            if (registryValueObject == null)
            {
                return true;
            }
            int registryValue = (int)registryValueObject;
            return registryValue > 0;
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }
        return true;
    }
}

file static class Macro
{
    public static ushort LOWORD(nint value) => (ushort)((long)value & 0xFFFF);

    public static ushort HIWORD(nint value) => (ushort)((((long)value) >> 0x10) & 0xFFFF);

    public static int GET_X_LPARAM(nint wParam) => LOWORD(wParam);

    public static int GET_Y_LPARAM(nint wParam) => HIWORD(wParam);
}
