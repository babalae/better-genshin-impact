using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Interface;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows;
using Wpf.Ui;

namespace BetterGenshinImpact.ViewModel;

public partial class MainWindowViewModel : ObservableObject, IViewModel
{
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly IConfigService _configService;
    public string Title => $"BetterGI · 更好的原神 · {Global.Version}{(RuntimeHelper.IsDebug ? " · Dev" : string.Empty)}";

    [ObservableProperty]
    public bool _isVisible = true;

    [ObservableProperty]
    public WindowState _windowState = WindowState.Normal;

    public AllConfig Config { get; set; }

    public MainWindowViewModel(INavigationService navigationService, IConfigService configService)
    {
        _configService = configService;
        Config = _configService.Get();
        _logger = App.GetLogger<MainWindowViewModel>();
    }

    [RelayCommand]
    private void OnHide()
    {
        IsVisible = false;
    }

    [RelayCommand]
    private void OnClosing(CancelEventArgs e)
    {
        if (Config.CommonConfig.ExitToTray)
        {
            e.Cancel = true;
            OnHide();
        }
    }

    [RelayCommand]
    [SuppressMessage("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "MVVMTK0039:Async void returning method annotated with RelayCommand")]
    private async void OnLoaded()
    {
        _logger.LogInformation("更好的原神 {Version}", Global.Version);
        try
        {
            await Task.Run(() =>
            {
                try
                {
                    var s = OcrFactory.Paddle.Ocr(new Mat(Global.Absolute("Assets\\Model\\PaddleOCR\\test_ocr.png"), ImreadModes.Grayscale));
                    Debug.WriteLine("PaddleOcr预热结果:" + s);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    _logger.LogError("PaddleOcr预热异常：" + e.Source + "\r\n--" + Environment.NewLine + e.StackTrace + "\r\n---" + Environment.NewLine + e.Message);
                    var innerException = e.InnerException;
                    if (innerException != null)
                    {
                        _logger.LogError("PaddleOcr预热内部异常：" + innerException.Source + "\r\n--" + Environment.NewLine + innerException.StackTrace + "\r\n---" + Environment.NewLine + innerException.Message);
                        throw innerException;
                    }
                    else
                    {
                        throw;
                    }
                }
            });
        }
        catch (Exception e)
        {
            MessageBox.Show("PaddleOcr预热失败：" + e.Source + "\r\n--" + Environment.NewLine + e.StackTrace + "\r\n---" + Environment.NewLine + e.Message);
        }

        try
        {
            await Task.Run(GetNewestInfo);
        }
        catch (Exception e)
        {
            Debug.WriteLine("获取最新版本信息失败：" + e.Source + "\r\n--" + Environment.NewLine + e.StackTrace + "\r\n---" + Environment.NewLine + e.Message);
            _logger.LogWarning("获取 BetterGI 最新版本信息失败");
        }
    }

    private async void GetNewestInfo()
    {
        try
        {
            var httpClient = new HttpClient();
            var notice = await httpClient.GetFromJsonAsync<Notice>(@"https://hui-config.oss-cn-hangzhou.aliyuncs.com/bgi/notice.json");
            if (notice != null && !string.IsNullOrWhiteSpace(notice.Version))
            {
                if (Global.IsNewVersion(notice.Version))
                {
                    if (!string.IsNullOrEmpty(Config.NotShowNewVersionNoticeEndVersion)
                        && !Global.IsNewVersion(Config.NotShowNewVersionNoticeEndVersion, notice.Version))
                    {
                        return;
                    }

                    await UIDispatcherHelper.Invoke(async () =>
                    {
                        var uiMessageBox = new Wpf.Ui.Controls.MessageBox
                        {
                            Title = "更新提示",
                            Content = $"存在最新版本 {notice.Version}，点击确定前往下载页面下载最新版本",
                            PrimaryButtonText = "确定",
                            SecondaryButtonText = "不再提示",
                            CloseButtonText = "取消",
                        };

                        var result = await uiMessageBox.ShowDialogAsync();
                        if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
                        {
                            Process.Start(new ProcessStartInfo("https://bgi.huiyadan.com/download.html") { UseShellExecute = true });
                        }
                        else if (result == Wpf.Ui.Controls.MessageBoxResult.Secondary)
                        {
                            Config.NotShowNewVersionNoticeEndVersion = notice.Version;
                        }
                    });
                }
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine("获取最新版本信息失败：" + e.Source + "\r\n--" + Environment.NewLine + e.StackTrace + "\r\n---" + Environment.NewLine + e.Message);
            _logger.LogWarning("获取 BetterGI 最新版本信息失败");
        }
    }
}
