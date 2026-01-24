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
using BetterGenshinImpact.Service.Model.MihoyoMap.Requests;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.ViewModel;

public partial class MaskMapPointInfoPopupViewModel : ObservableObject
{
    private readonly ILogger<MaskMapPointInfoPopupViewModel> _logger = App.GetLogger<MaskMapPointInfoPopupViewModel>();
    private CancellationTokenSource? _cts;
    private static readonly HttpClient _http = new();

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
        IsLoading = true;
        IsOpen = true;

        try
        {
            var apiService = App.GetService<IMihoyoMapApiService>();
            if (apiService == null)
            {
                Text = "地图服务未就绪";
                return;
            }

            if (!int.TryParse(point.Id, out var pointId))
            {
                Text = $"点位 ID 非法: {point.Id}";
                return;
            }

            var resp = await apiService.GetPointInfoAsync(new PointInfoRequest
            {
                PointId = pointId
            }, ct);

            ct.ThrowIfCancellationRequested();

            if (resp.Retcode != 0 || resp.Data == null)
            {
                Text = $"查询失败: {resp.Retcode} {resp.Message}";
                return;
            }

            var content = (resp.Data.Info.Content ?? string.Empty).Trim();
            Text = string.IsNullOrEmpty(content) ? "暂无描述" : content;

            var imageUrl = string.Empty;
            if (resp.Data.Info.UrlList is { Count: > 0 })
            {
                imageUrl = resp.Data.Info.UrlList[0] ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                imageUrl = resp.Data.Info.Img ?? string.Empty;
            }

            var img = await LoadImageNoCacheAsync(imageUrl, ct);
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

    private static async Task<ImageSource?> LoadImageNoCacheAsync(string url, CancellationToken ct)
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
                    using var ms = new MemoryStream(bytes);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();
                    return (ImageSource)bmp;
                });
            }

            return await Application.Current.Dispatcher.InvokeAsync(() =>
            {
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
    }
}
