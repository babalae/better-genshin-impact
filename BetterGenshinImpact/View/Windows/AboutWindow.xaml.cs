using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Ui;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;

namespace BetterGenshinImpact.View.Windows;

public partial class AboutWindow
{
    private int _clickCount;
    private DateTime _lastClickTime;
    private const int RequiredClicks = 5;
    private const int MaxIntervalMs = 200;
    
    public AboutWindow()
    {
        InitializeComponent();
        SourceInitialized += (s, e) => WindowHelper.TryApplySystemBackdrop(this);

        Line1.Text = Base64Helper.DecodeToString("54mI5p2D5L+h5oGv");
        Line2.Text = Base64Helper.DecodeToString("6K+B5Lmm5Y+377yaIOi9r+iRl+eZu+Wtl+esrDE1MTU2OTUw5Y+3");
        Line3.Text = Base64Helper.DecodeToString("55m76K6w5Y+377yaIDIwMjVTUjA1MDA3NTI=");
        MouseDown += Window_MouseDown;
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var now = DateTime.Now;
        if ((now - _lastClickTime).TotalMilliseconds <= MaxIntervalMs)
        {
            _clickCount++;
        }
        else
        {
            _clickCount = 1;
        }

        _lastClickTime = now;

        if (_clickCount >= RequiredClicks)
        {

            RzTextBlock.Visibility = Visibility.Visible;
            
            _clickCount = 0;
        }
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}