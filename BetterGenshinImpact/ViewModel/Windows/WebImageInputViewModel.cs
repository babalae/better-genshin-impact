using BetterGenshinImpact.Helpers;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.ViewModel.Pages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Windows;
    
public partial  class WebImageInputViewModel : ViewModel
{
    public static event Action OnSubmitWebImageUrl;
    public event Action? RequestClose;
    private readonly HttpClient _httpClient = new();
    [ObservableProperty]
    private string _url = File.Exists(HomePageViewModel.CustomBannerImageUrlPath) ? File.ReadAllText(HomePageViewModel.CustomBannerImageUrlPath) : "";
    [ObservableProperty]
    private string _buttonText = "确定";
    [ObservableProperty]
    private bool _isEnabled = true;
    [RelayCommand]
    private void SubmitWebImageUrl()
    {
        if (string.IsNullOrEmpty(_url))
        {
            return;
        }
        _ = DownloadBannerImageAsync(_url).ContinueWith(t =>
        {
            if (t.Result)
            {
                IsEnabled = true;
                ButtonText = "确定";
                // 写入文件
                File.WriteAllText(HomePageViewModel.CustomBannerImageUrlPath, _url);
                UIDispatcherHelper.Invoke(() =>
                {
                    OnSubmitWebImageUrl?.Invoke();
                    RequestClose?.Invoke();
                });
            }
            else
            {
                IsEnabled = true;
                ButtonText = "确定";
            }
        }, TaskScheduler.Default);
    }
    private async Task<bool> DownloadBannerImageAsync(string url)
    {
        IsEnabled = false;
        ButtonText = "下载中...";
        // 下载图片
        var imageBytes = await _httpClient.GetByteArrayAsync(url);
        if (imageBytes.Length == 0)
        {
            return false;
        }
        // 保存图片到本地
        File.WriteAllBytes(HomePageViewModel.CustomBannerImageNetworkPath, imageBytes);
        return true;
    }
}