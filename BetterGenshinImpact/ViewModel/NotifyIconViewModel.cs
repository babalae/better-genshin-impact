using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Controls.Webview;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Vanara.PInvoke;
using Windows.Media.Protection.PlayReady;

namespace BetterGenshinImpact.ViewModel;

public partial class NotifyIconViewModel : ObservableObject
{
    [RelayCommand]
    public void ShowOrHide()
    {
        if (Application.Current.MainWindow.Visibility == Visibility.Visible)
        {
            Application.Current.MainWindow.Hide();
        }
        else
        {
            Application.Current.MainWindow.Activate();
            Application.Current.MainWindow.Focus();
            Application.Current.MainWindow.Show();
            WindowBacktray.Show(Application.Current.MainWindow);
        }
    }

    [RelayCommand]
    public void Exit()
    {
        App.GetService<IConfigService>()?.Save();
        Application.Current.Shutdown();
    }

    [RelayCommand]
    public async Task CheckUpdateAsync()
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            string jsonString = await httpClient.GetStringAsync(@"https://api.github.com/repos/babalae/better-genshin-impact/releases/latest");
            var jsonDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonString);

            if (jsonDict != null)
            {
                string? name = jsonDict["name"] as string;
                string? body = jsonDict["body"] as string;
                string md = $"# {name}{new string('\n', 2)}{body}";

                md = WebUtility.HtmlEncode(md);
                string md2html = ResourceHelper.GetString($"pack://application:,,,/Assets/Strings/md2html.html", Encoding.UTF8);
                var html = md2html.Replace("{{content}}", md);

                WebpageWindow win = new()
                {
                    Title = "更新日志",
                    Width = 800,
                    Height = 600,
                    Owner = Application.Current.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                win.NavigateToHtml(html);
                win.ShowDialog();
            }
        }
        catch (Exception e)
        {
            _ = e;
        }
    }
}

file static class WindowBacktray
{
    public static void Hide(Window window)
    {
        if (window != null)
        {
            window.Visibility = Visibility.Hidden;
            window.WindowState = WindowState.Minimized;
        }
    }

    public static void Show(Window window)
    {
        if (window != null)
        {
            if (window.Visibility != Visibility.Visible)
            {
                window.Visibility = Visibility.Visible;
            }
            if (window.WindowState == WindowState.Minimized)
            {
                nint hWnd = new WindowInteropHelper(Application.Current.MainWindow).Handle;
                _ = User32.SendMessage(hWnd, User32.WindowMessage.WM_SYSCOMMAND, User32.SysCommand.SC_RESTORE, IntPtr.Zero);
            }
        }
    }
}
