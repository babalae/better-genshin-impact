using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace BetterGenshinImpact.View.Controls.Webview;

public class WebpagePanel : UserControl
{
    private Uri _currentUri = null!;
    private WebView2 _webView = null!;
    private bool _initialized = false;

    public WebView2 WebView => _webView;

    public string? DownloadFolderPath { get; set; }

    public Action? OnWebViewInitializedAction { get; set; }

    public WebpagePanel()
    {
        if (!IsWebView2Available())
        {
            Content = CreateDownloadButton();
        }
        else
        {
            EnsureWebView2DataFolder();
            _webView = new WebView2()
            {
                CreationProperties = new CoreWebView2CreationProperties
                {
                    UserDataFolder = Path.Combine(new FileInfo(Environment.ProcessPath!).DirectoryName!, @"WebView2Data\\"),

                    // TODO: change the theme from `md2html.html` to fit it firstly.
                    // AdditionalBrowserArguments = "--enable-features=WebContentsForceDark"
                }
            };
            _webView.CoreWebView2InitializationCompleted += WebView_CoreWebView2InitializationCompleted;
            _webView.NavigationStarting += NavigationStarting_CancelNavigation;
            Content = _webView;
        }
    }

    private void WebView_CoreWebView2InitializationCompleted(object? sender, CoreWebView2InitializationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            _initialized = true;
            if (!string.IsNullOrEmpty(DownloadFolderPath))
            {
                WebView.CoreWebView2.Profile.DefaultDownloadFolderPath = DownloadFolderPath;
            }
            // 调用外部设置的 Action
            OnWebViewInitializedAction?.Invoke();
        }
        else
        {
            _ = e.InitializationException;
        }
    }

    public static bool IsWebView2Available()
    {
        try
        {
            return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static Uri FilePathToFileUrl(string filePath)
    {
        var uri = new StringBuilder();
        foreach (var v in filePath)
            if (v >= 'a' && v <= 'z' || v >= 'A' && v <= 'Z' || v >= '0' && v <= '9' ||
                v == '+' || v == '/' || v == ':' || v == '.' || v == '-' || v == '_' || v == '~' ||
                v > '\x80')
                uri.Append(v);
            else if (v == Path.DirectorySeparatorChar || v == Path.AltDirectorySeparatorChar)
                uri.Append('/');
            else
                uri.Append($"%{(int)v:X2}");
        if (uri.Length >= 2 && uri[0] == '/' && uri[1] == '/') // UNC path
            uri.Insert(0, "file:");
        else
            uri.Insert(0, "file:///");

        try
        {
            return new Uri(uri.ToString());
        }
        catch
        {
            return null!;
        }
    }

    public void NavigateToFile(string path)
    {
        var uri = Path.IsPathRooted(path) ? FilePathToFileUrl(path) : new Uri(path);

        NavigateToUri(uri);
    }

    public void NavigateToUri(Uri uri)
    {
        if (_webView == null)
            return;

        _webView.Source = uri;
        _currentUri = _webView.Source;
    }

    public void NavigateToHtml(string html)
    {
        _webView?.EnsureCoreWebView2Async()
            .ContinueWith(_ => SpinWait.SpinUntil(() => _initialized))
            .ContinueWith(_ => Dispatcher.Invoke(() => _webView?.NavigateToString(html)));
    }

    private void NavigationStarting_CancelNavigation(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (e.Uri.StartsWith("data:")) // when using NavigateToString
            return;

        var newUri = new Uri(e.Uri);
        if (newUri != _currentUri) e.Cancel = true;
    }

    public void Dispose()
    {
        _webView?.Dispose();
        _webView = null!;
    }

    private Button CreateDownloadButton()
    {
        var button = new Button
        {
            Content = "查看需要安装 Microsoft Edge WebView2 点击这里开始下载",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(20, 6, 20, 6)
        };
        button.Click += (sender, e) => Process.Start("https://go.microsoft.com/fwlink/p/?LinkId=2124703");

        return button;
    }

    private void EnsureWebView2DataFolder()
    {
        try
        {
            string folder = Path.Combine(new FileInfo(Environment.ProcessPath!).DirectoryName!, @"WebView2Data\\");
            Directory.CreateDirectory(folder);
            DirectoryInfo info = new DirectoryInfo(folder);
            DirectorySecurity access = info.GetAccessControl();
            access.AddAccessRule(new FileSystemAccessRule("Everyone", FileSystemRights.FullControl, AccessControlType.Allow));
            info.SetAccessControl(access);
        }
        catch { }
    }

}
