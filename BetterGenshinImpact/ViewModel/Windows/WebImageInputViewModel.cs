using BetterGenshinImpact.Service.Interface;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.ViewModel.Windows;

public partial class WebImageInputViewModel : ViewModel
{
    public event Action? SubmitCompleted;
    public event Action? RequestClose;

    private readonly IBannerImageService _bannerImageService;
    private readonly ILogger<WebImageInputViewModel> _logger = App.GetLogger<WebImageInputViewModel>();
    private CancellationTokenSource? _downloadCancellationTokenSource;

    [ObservableProperty]
    private string _url = "";

    [ObservableProperty]
    private string _buttonText = "确定";

    [ObservableProperty]
    private bool _isEnabled = true;

    public WebImageInputViewModel(IBannerImageService bannerImageService)
    {
        _bannerImageService = bannerImageService;
        try
        {
            Url = _bannerImageService.ReadConfiguredUrl() ?? "";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取网络背景图片地址失败");
            Toast.Warning($"读取网络背景图片地址失败：{ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SubmitWebImageUrlAsync()
    {
        var url = Url.Trim();
        if (string.IsNullOrEmpty(url))
        {
            Toast.Warning("请输入网络图片地址。");
            return;
        }

        CancelDownload();
        var cancellationTokenSource = new CancellationTokenSource();
        _downloadCancellationTokenSource = cancellationTokenSource;
        IsEnabled = false;
        ButtonText = "下载中...";

        try
        {
            // 下载图片
            // 保存图片到本地
            if (!await _bannerImageService.DownloadAndSaveAsync(url, cancellationTokenSource.Token))
            {
                return;
            }

            cancellationTokenSource.Token.ThrowIfCancellationRequested();
            // 写入文件
            _bannerImageService.SaveConfiguredUrl(url);
            Url = url;
            SubmitCompleted?.Invoke();
            RequestClose?.Invoke();
        }
        catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
        {
            // 用户关闭窗口或新下载替代旧下载时无需提示。
        }
        catch (ArgumentException ex)
        {
            Toast.Warning(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "下载网络背景图片失败");
            Toast.Error($"图片下载失败：{ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_downloadCancellationTokenSource, cancellationTokenSource))
            {
                _downloadCancellationTokenSource = null;
            }

            cancellationTokenSource.Dispose();
            IsEnabled = true;
            ButtonText = "确定";
        }
    }

    public void CancelDownload()
    {
        var cancellationTokenSource = _downloadCancellationTokenSource;
        if (cancellationTokenSource is null)
        {
            return;
        }

        _downloadCancellationTokenSource = null;
        cancellationTokenSource.Cancel();
        _bannerImageService.InvalidatePendingDownloads();
    }
}
