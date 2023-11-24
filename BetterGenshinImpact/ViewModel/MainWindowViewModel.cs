using System;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Interface;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows;
using Wpf.Ui;

namespace BetterGenshinImpact.ViewModel
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly ILogger<MainWindowViewModel> _logger;
        private readonly IConfigService _configService;
        public string Title => $"BetterGI · 更好的原神 · {Global.Version}";

        public MainWindowViewModel(INavigationService navigationService, IConfigService configService)
        {
            _configService = configService;
            _logger = App.GetLogger<MainWindowViewModel>();
        }


        [RelayCommand]
        private async void OnLoaded()
        {
            _logger.LogInformation("更好的原神 {Version}", Global.Version);
            try
            {
                await Task.Run(() =>
                {
                    var s = OcrFactory.Paddle.Ocr(new Mat(Global.Absolute("Assets\\Model\\PaddleOCR\\test_ocr.png"), ImreadModes.Grayscale));
                    Debug.WriteLine("PaddleOcr预热结果:" + s);
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
            var httpClient = new HttpClient();
            var notice = await httpClient.GetFromJsonAsync<Notice>(@"https://hui-config.oss-cn-hangzhou.aliyuncs.com/bgi/notice.json");
            if (notice != null && !string.IsNullOrWhiteSpace(notice.Version))
            {
                if (Global.IsNewVersion(notice.Version))
                {
                    await UIDispatcherHelper.Invoke(async () =>
                    {
                        var uiMessageBox = new Wpf.Ui.Controls.MessageBox
                        {
                            Title = "更新提示",
                            Content = $"存在最新版本 {notice.Version}，点击确定前往下载页面下载最新版本",
                            PrimaryButtonText = "确定",
                            CloseButtonText = "取消",
                        };

                        var result = await uiMessageBox.ShowDialogAsync();
                        if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
                        {
                            Process.Start(new ProcessStartInfo("https://bgi.huiyadan.com/download.html") { UseShellExecute = true });
                        }
                    });
                }
            }
        }

        [RelayCommand]
        private void OnClosed()
        {
            _configService.Save();
            WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "Close", "", ""));
            Debug.WriteLine("MainWindowViewModel Closed");
            Application.Current.Shutdown();
        }

    }
}