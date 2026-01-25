using System;
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
    [ObservableProperty] private Rect _placementRect = Rect.Empty;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private ImageSource? _image;

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
        Text = "正在加载...";
        Image = null;
        DisposeImageStream();
        IsLoading = true;
        IsOpen = true;

        try
        {
            var service = App.GetService<IMaskMapPointService>();
            if (service == null)
            {
                Text = "地图服务未就绪";
                return;
            }

            var info = await service.GetPointInfoAsync(point, ct);
            ct.ThrowIfCancellationRequested();

            Text = string.IsNullOrEmpty(info.Text) ? "暂无描述" : info.Text;
            var img = await LoadImageNoCacheAsync(info.ImageUrl, ct);
            ct.ThrowIfCancellationRequested();
            Image = img;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "查询地图点位详情失败");
            Text = "查询失败";
        }
        finally
        {
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
        Image = null;
        DisposeImageStream();
    }
}
