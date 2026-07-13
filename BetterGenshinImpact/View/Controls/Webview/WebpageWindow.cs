using System;
using System.Windows;
using System.Windows.Media;
using BetterGenshinImpact.Helpers.Ui;
using Microsoft.Web.WebView2.Wpf;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Controls.Webview;

public class WebpageWindow : Window
{
    public WebpagePanel? Panel => Content as WebpagePanel;

    public WebView2 WebView => Panel!.WebView;

    public WebpageWindow()
    {
        WebpagePanel wp = new();

        Content = wp;
        // Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowHelper.TryApplySystemBackdrop(this);
    }

    public void NavigateToUri(Uri uri)
    {
        Panel?.NavigateToUri(uri);
    }

    public void NavigateToHtml(string html)
    {
        Panel?.NavigateToHtml(html);
    }

    public void NavigateToFile(string path)
    {
        Panel?.NavigateToFile(path);
    }
}
