using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
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

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private Rect _placementRect = Rect.Empty;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _text = string.Empty;

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

    [RelayCommand]
    public void Close()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        IsOpen = false;
        IsLoading = false;
    }
}
