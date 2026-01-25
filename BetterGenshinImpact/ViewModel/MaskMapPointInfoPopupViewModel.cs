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
    [ObservableProperty] private bool _isGifImage;
    [ObservableProperty] private Uri? _gifSourceUri;
    [ObservableProperty] private Stream? _gifSourceStream;

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
        IsGifImage = false;
        GifSourceUri = null;
        GifSourceStream = null;
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
                    var (isGif, staticImage, gifUri, gifStream) = await LoadPopupImageNoCacheAsync(info.ImageUrl, ct);
                    ct.ThrowIfCancellationRequested();

                    IsGifImage = isGif;
                    GifSourceUri = gifUri;
                    GifSourceStream = gifStream;
                    Image = staticImage;

                    if (IsGifImage)
                    {
                        if (GifSourceUri == null && GifSourceStream == null)
                        {
                            ImageError = "图片加载失败";
                        }
                    }
                    else
                    {
                        if (Image == null)
                        {
                            ImageError = "图片加载失败";
                        }
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
        GifSourceStream = null;
        _imageStream?.Dispose();
        _imageStream = null;
    }

    private async Task<(bool IsGif, ImageSource? StaticImage, Uri? GifSourceUri, Stream? GifSourceStream)> LoadPopupImageNoCacheAsync(
        string url,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return default;
        }

        try
        {
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = await _http.GetByteArrayAsync(url, ct);

                if (IsGifBytes(bytes))
                {
                    DisposeImageStream();
                    _imageStream = new MemoryStream(bytes, writable: false);
                    _imageStream.Position = 0;
                    return (IsGif: true, StaticImage: null, GifSourceUri: null, GifSourceStream: _imageStream);
                }

                var img = await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    using var ms = new MemoryStream(bytes, writable: false);
                    ms.Position = 0;
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();
                    return (ImageSource)bmp;
                });

                return (IsGif: false, StaticImage: img, GifSourceUri: null, GifSourceStream: null);
            }

            if (LooksLikeGifUri(url))
            {
                if (Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var gifUri))
                {
                    DisposeImageStream();
                    return (IsGif: true, StaticImage: null, GifSourceUri: gifUri, GifSourceStream: null);
                }
            }

            var staticImg = await Application.Current.Dispatcher.InvokeAsync(() =>
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
            return (IsGif: false, StaticImage: staticImg, GifSourceUri: null, GifSourceStream: null);
        }
        catch
        {
            return default;
        }
    }

    private static bool LooksLikeGifUri(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var uri))
        {
            var path = uri.IsAbsoluteUri ? uri.AbsolutePath : uri.OriginalString;
            var q = path.IndexOfAny(['?', '#']);
            if (q >= 0)
            {
                path = path[..q];
            }

            return path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
        }

        return url.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGifBytes(byte[] bytes)
    {
        return bytes is
        [
            (byte)'G', (byte)'I', (byte)'F', (byte)'8',
            (byte)'7' or (byte)'9',
            (byte)'a',
            ..
        ];
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
        IsGifImage = false;
        GifSourceUri = null;
        GifSourceStream = null;
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
