using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BetterGenshinImpact.Model.MaskMap;
using BetterGenshinImpact.Service.Interface;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.ViewModel;

public partial class MaskMapPointInfoPopupViewModel : ObservableObject
{
    private readonly ILogger<MaskMapPointInfoPopupViewModel> _logger = App.GetLogger<MaskMapPointInfoPopupViewModel>();
    private CancellationTokenSource? _cts;
    private static readonly HttpClient _http = new();
    private MemoryStream? _imageStream;

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isTextLoading;
    [ObservableProperty] private string _textError = string.Empty;
    [ObservableProperty] private Rect _placementRect = Rect.Empty;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private IReadOnlyList<MaskMapLink> _urlList = Array.Empty<MaskMapLink>();
    [ObservableProperty] private bool _hasUrlList;
    [ObservableProperty] private ImageSource? _image;
    [ObservableProperty] private bool _hasImage;
    [ObservableProperty] private bool _isImageLoading;
    [ObservableProperty] private string _imageError = string.Empty;

    partial void OnUrlListChanged(IReadOnlyList<MaskMapLink> value)
    {
        HasUrlList = value is { Count: > 0 };
    }

    public async Task ShowAsync(MaskMapPoint point, Point anchorPosition, string title, CancellationToken externalCt = default)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        var ct = _cts.Token;

        PlacementRect = new Rect(
            anchorPosition.X - MaskMapPointStatic.Width / 2.0,
            anchorPosition.Y - MaskMapPointStatic.Height / 2.0,
            MaskMapPointStatic.Width,
            MaskMapPointStatic.Height);

        Title = title ?? string.Empty;
        Text = string.Empty;
        UrlList = point.VideoUrls;
        TextError = string.Empty;
        IsTextLoading = true;
        Image = null;
        HasImage = false;
        ImageError = string.Empty;
        IsImageLoading = false;
        DisposeImageStream();
        IsLoading = true;
        IsOpen = true;

        try
        {
            var service = App.GetService<IMaskMapPointService>();
            if (service == null)
            {
                TextError = "地图服务未就绪";
                return;
            }

            var info = await service.GetPointInfoAsync(point, ct);
            ct.ThrowIfCancellationRequested();

            Text = string.IsNullOrEmpty(info.Text) ? "暂无描述" : info.Text;
            IsTextLoading = false;
            if (info.UrlList is { Count: > 0 })
            {
                UrlList = info.UrlList;
            }

            HasImage = !string.IsNullOrWhiteSpace(info.ImageUrl);
            if (HasImage)
            {
                IsImageLoading = true;
                try
                {
                    var img = await LoadImageNoCacheAsync(info.ImageUrl, ct);
                    ct.ThrowIfCancellationRequested();
                    if (img == null)
                    {
                        ImageError = "图片加载失败";
                    }
                    else
                    {
                        Image = img;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "加载点位图片失败");
                    ImageError = "图片加载失败";
                }
                finally
                {
                    IsImageLoading = false;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "查询地图点位详情失败");
            TextError = "查询失败";
        }
        finally
        {
            IsTextLoading = false;
            IsImageLoading = false;
            IsLoading = false;
        }
    }

    private void DisposeImageStream()
    {
        _imageStream?.Dispose();
        _imageStream = null;
    }

    private async Task<ImageSource?> LoadImageNoCacheAsync(string url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        try
        {
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = await _http.GetByteArrayAsync(url, ct);
                return await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    DisposeImageStream();
                    _imageStream = new MemoryStream(bytes, writable: false);
                    _imageStream.Position = 0;
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = _imageStream;
                    bmp.EndInit();
                    bmp.Freeze();
                    return (ImageSource)bmp;
                });
            }

            return await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                DisposeImageStream();
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(url, UriKind.RelativeOrAbsolute);
                bmp.EndInit();
                bmp.Freeze();
                return (ImageSource)bmp;
            });
        }
        catch
        {
            return null;
        }
    }

    [RelayCommand]
    public void Close()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        IsOpen = false;
        IsLoading = false;
        IsTextLoading = false;
        TextError = string.Empty;
        Text = string.Empty;
        UrlList = Array.Empty<MaskMapLink>();
        HasImage = false;
        IsImageLoading = false;
        ImageError = string.Empty;
        Image = null;
        DisposeImageStream();
    }

    [RelayCommand]
    private void OpenUrl(string? url)
    {
        url = NormalizeUrl(url);
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "打开链接失败: {Url}", url);
        }
    }

    private static string NormalizeUrl(string? url)
    {
        url = (url ?? string.Empty).Trim();
        url = url.Trim('`').Trim('"').Trim('\'').Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var abs))
        {
            return abs.AbsoluteUri;
        }

        if (url.StartsWith("//", StringComparison.Ordinal))
        {
            return "https:" + url;
        }

        if (url.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            return "https://" + url;
        }

        return url;
    }
}
