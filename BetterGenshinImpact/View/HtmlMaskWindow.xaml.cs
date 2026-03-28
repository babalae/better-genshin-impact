using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;
using BetterGenshinImpact.Core.Script.Dependence;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers;
using Microsoft.Web.WebView2.Core;

namespace BetterGenshinImpact.View;

/// <summary>
/// HTML遮罩窗口 - 仅用于显示，不可交互（点击穿透）
/// </summary>
public partial class HtmlMaskWindow : Window
{
    private static readonly ConcurrentDictionary<string, HtmlMaskWindow> _windows = new();
    private const int MaxWindows = 5;

    private readonly string _id;
    private readonly string _webView2DataPath;
    private bool _navigationCompleted;

    /// <summary>
    /// 窗口唯一标识
    /// </summary>
    public string MaskId => _id;

    private HtmlMaskWindow(string url, string? id, string webView2DataPath)
    {
        _id = id ?? Guid.NewGuid().ToString("N");
        _webView2DataPath = webView2DataPath;
        InitializeComponent();
        Loaded += OnLoaded;
        InitializeAsync(url);
    }

    #region 静态窗口管理

    /// <summary>
    /// 显示HTML遮罩窗口
    /// </summary>
    public static string Show(string url, string? id, string workDir)
    {
        // 指定ID时先关闭已有窗口
        if (id != null && _windows.TryGetValue(id, out var existing))
        {
            existing.Dispatcher.Invoke(() => existing.Close());
        }

        if (_windows.Count >= MaxWindows)
        {
            throw new InvalidOperationException($"最多同时打开 {MaxWindows} 个HTML遮罩窗口");
        }

        string webView2DataPath = Path.Combine(workDir, "WebView2Data");

        return Application.Current.Dispatcher.Invoke(() =>
        {
            var window = new HtmlMaskWindow(url, id, webView2DataPath);
            string wid = window.MaskId;
            _windows[wid] = window;
            window.Closed += (_, _) =>
            {
                _windows.TryRemove(wid, out _);
                window.DisposeWebView();
            };
            window.Show();
            return wid;
        });
    }

    /// <summary>
    /// 关闭指定窗口
    /// </summary>
    public static bool Close(string id)
    {
        if (_windows.TryGetValue(id, out var window))
        {
            window.Dispatcher.Invoke(() => window.Close());
            return true;
        }
        return false;
    }

    /// <summary>
    /// 关闭所有窗口
    /// </summary>
    public static void CloseAll()
    {
        foreach (var kvp in _windows)
        {
            kvp.Value.Dispatcher.Invoke(() => kvp.Value.Close());
        }
    }

    /// <summary>
    /// 隐藏所有窗口（保留生命，不关闭）
    /// </summary>
    public static void HideAll()
    {
        foreach (var kvp in _windows)
        {
            kvp.Value.Dispatcher.Invoke(() => kvp.Value.Hide());
        }
    }

    /// <summary>
    /// 显示所有窗口
    /// </summary>
    public static void ShowAll()
    {
        foreach (var kvp in _windows)
        {
            kvp.Value.Dispatcher.Invoke(() =>
            {
                kvp.Value.Show();
                kvp.Value.UpdatePosition();
            });
        }
    }

    /// <summary>
    /// 同步所有窗口位置
    /// </summary>
    public static void UpdateAllPositions()
    {
        foreach (var kvp in _windows)
        {
            kvp.Value.UpdatePosition();
        }
    }

    /// <summary>
    /// 获取所有窗口ID
    /// </summary>
    public static string[] GetWindowIds()
    {
        var keys = new string[_windows.Count];
        _windows.Keys.CopyTo(keys, 0);
        return keys;
    }

    /// <summary>
    /// 窗口是否存在
    /// </summary>
    public static bool Exists(string id)
    {
        return _windows.ContainsKey(id);
    }

    /// <summary>
    /// 通知窗口刷新待推送的消息
    /// </summary>
    internal static void NotifyFlush(string windowId)
    {
        if (!_windows.TryGetValue(windowId, out var window)) return;
        window.Dispatcher.BeginInvoke(() =>
        {
            // 页面还没加载完，消息留在队列中由 NavigationCompleted 统一推送
            if (!window._navigationCompleted) return;
            if (window.WebView.CoreWebView2 == null) return;
            HtmlMask.FlushPendingMessages(windowId, json =>
            {
                window.WebView.CoreWebView2.PostWebMessageAsString(json);
            });
        });
    }

    #endregion

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SetClickThrough();
        UpdatePosition();
    }

    private async void InitializeAsync(string url)
    {
        await WebView.EnsureCoreWebView2Async(
            await CoreWebView2Environment.CreateAsync(null, _webView2DataPath));

        WebView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
        WebView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
        WebView.CoreWebView2.Settings.IsScriptEnabled = true;
        WebView.CoreWebView2.Settings.IsWebMessageEnabled = true;

        // 监听HTML发来的消息，解析 url + data
        WebView.CoreWebView2.WebMessageReceived += (_, e) =>
        {
            try
            {
                string raw = e.TryGetWebMessageAsString();
                string url = "";
                string data = raw;

                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("url", out var ep))
                    {
                        url = ep.GetString() ?? "";
                        data = root.TryGetProperty("data", out var d) ? d.GetRawText() : "{}";
                    }
                }
                catch { }

                HtmlMask.SendFromHtml(_id, url, data);
            }
            catch { }
        };

        // 页面加载完成后推送队列中待发送的消息
        WebView.CoreWebView2.NavigationCompleted += (_, _) =>
        {
            _navigationCompleted = true;
            HtmlMask.FlushPendingMessages(_id, json =>
            {
                WebView.CoreWebView2.PostWebMessageAsString(json);
            });
        };

        if (!string.IsNullOrEmpty(url))
        {
            WebView.Source = new Uri(url);
        }
    }

    /// <summary>
    /// 更新窗口位置
    /// </summary>
    public void UpdatePosition()
    {
        try
        {
            var gameHandle = TaskContext.Instance().GameHandle;
            if (gameHandle == IntPtr.Zero) return;

            var currentRect = SystemControl.GetCaptureRect(gameHandle);
            Dispatcher.Invoke(() =>
            {
                Left = currentRect.Left;
                Top = currentRect.Top;
                Width = currentRect.Width;
                Height = currentRect.Height;
            });
        }
        catch { }
    }

    /// <summary>
    /// 设置点击穿透
    /// </summary>
    private void SetClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = (int)GetWindowLong(hwnd, WindowLongFlags.GWL_EXSTYLE);
        User32.SetWindowLong(hwnd, WindowLongFlags.GWL_EXSTYLE,
            (IntPtr)(style | (int)User32.WindowStylesEx.WS_EX_TRANSPARENT));
    }

    /// <summary>
    /// 释放 WebView2 资源，停止媒体播放
    /// </summary>
    private void DisposeWebView()
    {
        try
        {
            WebView.Dispose();
        }
        catch { }
    }
}
