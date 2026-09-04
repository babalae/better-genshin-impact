using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.GameTask.TravelLog;
using BetterGenshinImpact.GameTask.TravelLog.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;

namespace BetterGenshinImpact.ViewModel.Windows;

/// <summary>
/// 移动轨迹查看窗口
/// 在提瓦特全图上绘制指定日期的移动轨迹与里程统计
/// </summary>
public partial class TravelLogWindowViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// 显示位图宽度。256 级全图为 5632×3840，按 0.5 缩放展示
    /// </summary>
    private const int DisplayWidth = 2816;

    /// <summary>
    /// 2048 级图像坐标 → 256 级图像坐标的缩放
    /// </summary>
    private const int Scale2048To256 = 8;

    /// <summary>
    /// 模板匹配彩图瓦片 1 像素对应的游戏单位数（RoughZoom）
    /// </summary>
    private const float ColorTileZoom = 5f;

    [ObservableProperty]
    private WriteableBitmap? _mapBitmap;

    [ObservableProperty]
    private List<string> _availableDates = [];

    [ObservableProperty]
    private string _selectedDate = string.Empty;

    [ObservableProperty]
    private string _statisticsText = string.Empty;

    private readonly ILogger _logger = TaskControl.Logger;

    /// <summary>
    /// 缩放后的展示底图
    /// </summary>
    private Mat? _displayMap;

    /// <summary>
    /// 2048 级图像坐标 → 显示位图像素的缩放系数
    /// </summary>
    private float _imageToDisplayScale = 1f;

    private readonly DispatcherTimer _refreshTimer;

    private int _lastDrawnVersion = -1;

    private string? _loadedDate;

    private ISceneMap? _teyvatMap;

    private bool _disposed;

    public TravelLogWindowViewModel()
    {
        InitDisplayMap();

        AvailableDates = TravelLogService.Instance.ListAvailableDates();
        SelectedDate = DateTime.Now.ToString("yyyy-MM-dd");

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();

        Refresh();
    }

    private void InitDisplayMap()
    {
        try
        {
            var mapPath = Global.Absolute(@"Assets/Map/Teyvat/Teyvat_0_256.png");
            if (!File.Exists(mapPath))
            {
                _logger.LogWarning("[TravelLog] 地图文件不存在：{Path}", mapPath);
                return;
            }

            var matchingMethod = TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod;
            _teyvatMap = MapManager.GetMap(MapTypes.Teyvat, matchingMethod);

            using var fullMap = new Mat(mapPath);
            _imageToDisplayScale = (float)DisplayWidth / fullMap.Width;

            // 优先用彩色瓦片拼接底图，失败则回退灰度图（默认按彩色模式加载，已是 3 通道）
            var baseMap = TryBuildColorBaseMap(fullMap.Size()) ?? fullMap.Clone();
            _displayMap = baseMap.Resize(new OpenCvSharp.Size(DisplayWidth, (int)(baseMap.Height * _imageToDisplayScale)));
            baseMap.Dispose();
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "[TravelLog] 地图底图加载失败");
        }
    }

    /// <summary>
    /// 地图背景瓦片信息（对应 Assets/Map/Teyvat/mapback*_info.json）
    /// </summary>
    private sealed class MapBackLayerInfo
    {
        public string LayerId { get; set; } = string.Empty;
        public int Floor { get; set; }
        public float Scale { get; set; } = 1;
        public double Left { get; set; }
        public double Top { get; set; }
    }

    /// <summary>
    /// 用 MapBack 彩色瓦片拼接 256 级彩色底图。
    /// 瓦片锚点（Left/Top）为游戏坐标，瓦片 1 像素 = ColorTileZoom 游戏单位。
    /// </summary>
    private Mat? TryBuildColorBaseMap(OpenCvSharp.Size canvasSize)
    {
        try
        {
            if (_teyvatMap is not SceneBaseMap sceneMap)
            {
                return null;
            }

            var mapDir = Global.Absolute(@"Assets/Map/Teyvat");
            var layers = new List<MapBackLayerInfo>();
            foreach (var infoFile in new[] { "mapback_info.json", "mapback_6_0_info.json" })
            {
                var infoPath = Path.Combine(mapDir, infoFile);
                if (File.Exists(infoPath))
                {
                    layers.AddRange(JsonConvert.DeserializeObject<List<MapBackLayerInfo>>(File.ReadAllText(infoPath)) ?? []);
                }
            }

            if (layers.Count == 0)
            {
                return null;
            }

            // 2048 级地图原点在 256 级图像中的位置
            var origin = sceneMap.MapOriginInImageCoordinate;
            var origin256 = new Point2f(origin.X / Scale2048To256, origin.Y / Scale2048To256);
            // 瓦片像素 → 256 级像素 的缩放（瓦片 1px = ColorTileZoom 游戏单位 = ColorTileZoom*2 个2048像素 = /8 个256像素）
            var tileScale = ColorTileZoom * 2 / Scale2048To256;

            var canvas = new Mat(canvasSize, MatType.CV_8UC3, new Scalar(214, 207, 188));
            var seaColorSampled = false;

            foreach (var layer in layers.Where(l => l.Floor == 0))
            {
                var tilePath = Path.Combine(mapDir, $"{layer.LayerId}_color.webp");
                if (!File.Exists(tilePath))
                {
                    continue;
                }

                using var tile = Bv.ImRead(tilePath);
                if (tile == null || tile.Empty())
                {
                    continue;
                }

                // 第一块瓦片的左上角区域一般是海洋，取均值作为海洋填充色
                if (!seaColorSampled)
                {
                    var sampleRect = new Rect(2, 2, Math.Min(24, tile.Width - 4), Math.Min(24, tile.Height - 4));
                    using var sample = new Mat(tile, sampleRect);
                    var mean = Cv2.Mean(sample);
                    canvas.SetTo(mean);
                    seaColorSampled = true;
                }

                // 瓦片左上角对应游戏坐标 (Left, Top)，1 游戏单位 = 2 个 2048 级像素 = 2/8 个 256 级像素
                var posX = (int)Math.Round(origin256.X - layer.Left * (2d / Scale2048To256));
                var posY = (int)Math.Round(origin256.Y - layer.Top * (2d / Scale2048To256));

                var dstW = (int)Math.Round(tile.Width * tileScale);
                var dstH = (int)Math.Round(tile.Height * tileScale);
                var dstRect = new Rect(posX, posY, dstW, dstH);
                var canvasRect = new Rect(0, 0, canvas.Width, canvas.Height);
                var clip = dstRect.Intersect(canvasRect);
                if (clip.Width <= 0 || clip.Height <= 0)
                {
                    continue;
                }

                // 目标区域被画布裁剪时，源图按同样比例裁剪
                var srcRect = new Rect(
                    (int)((clip.X - posX) / tileScale),
                    (int)((clip.Y - posY) / tileScale),
                    (int)(clip.Width / tileScale),
                    (int)(clip.Height / tileScale));
                srcRect = srcRect.Intersect(new Rect(0, 0, tile.Width, tile.Height));
                if (srcRect.Width <= 0 || srcRect.Height <= 0)
                {
                    continue;
                }

                using var src = new Mat(tile, srcRect);
                using var resized = src.Resize(clip.Size);
                resized.CopyTo(new Mat(canvas, clip));
            }

            return seaColorSampled ? canvas : null;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "[TravelLog] 彩色地图合成失败，回退灰度底图");
            return null;
        }
    }

    partial void OnSelectedDateChanged(string value)
    {
        _lastDrawnVersion = -1; // 强制重绘
        _loadedDate = null;
        Refresh();
    }

    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var isToday = SelectedDate == today;

            TravelLogData data;
            if (isToday)
            {
                var version = TravelLogService.Instance.TodayVersion;
                if (_loadedDate == SelectedDate && version == _lastDrawnVersion)
                {
                    return; // 无新点，跳过重绘
                }
                _lastDrawnVersion = version;
                data = TravelLogService.Instance.GetTodaySnapshot();
            }
            else
            {
                if (_loadedDate == SelectedDate)
                {
                    return; // 历史日期数据不变，避免反复读盘
                }
                data = TravelLogService.Instance.LoadByDate(SelectedDate);
            }

            _loadedDate = SelectedDate;
            StatisticsText = FormatStatistics(data, isToday);
            DrawTrajectory(data);
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "[TravelLog] 轨迹重绘失败");
        }
    }

    private static string FormatStatistics(TravelLogData data, bool isToday)
    {
        var meters = data.TotalDistance;
        var distanceText = meters >= 1000
            ? $"{meters / 1000:F2} 公里"
            : $"{meters:F0} 米";
        var title = isToday ? "今日已移动约" : $"{data.Date} 共移动约";
        return $"{title} {distanceText} · 共 {data.Points.Count} 个轨迹点";
    }

    private void DrawTrajectory(TravelLogData data)
    {
        if (_displayMap == null || _teyvatMap == null)
        {
            return;
        }

        using var canvas = _displayMap.Clone();

        if (data.Points.Count > 0)
        {
            var displayPoints = data.Points
                .Select(p => ToDisplayPoint(p))
                .ToList();

            // 按分段绘制轨迹线（传送导致的断段之间不连线）
            var lineColor = new Scalar(0, 255, 0); // 绿色轨迹
            foreach (var (start, end) in EnumerateSegmentRanges(data.Points.Count, data.Segments))
            {
                if (end - start < 2)
                {
                    continue;
                }

                var pts = displayPoints.GetRange(start, end - start).ToArray();
                Cv2.Polylines(canvas, [pts], false, lineColor, 2, LineTypes.AntiAlias);
            }

            // 起点标记（绿点）
            var first = displayPoints[0];
            Cv2.Circle(canvas, first, 5, new Scalar(0, 200, 0), -1, LineTypes.AntiAlias);

            // 最新位置标记（红点白边）
            var last = displayPoints[^1];
            Cv2.Circle(canvas, last, 6, new Scalar(255, 255, 255), -1, LineTypes.AntiAlias);
            Cv2.Circle(canvas, last, 4, new Scalar(0, 0, 255), -1, LineTypes.AntiAlias);
        }

        MapBitmap = canvas.ToWriteableBitmap();
    }

    /// <summary>
    /// 游戏坐标 → 显示位图像素坐标
    /// </summary>
    private Point ToDisplayPoint(TravelLogPoint p)
    {
        // 游戏坐标（1024 级）→ 2048 级图像坐标 → 显示像素
        var image2048 = _teyvatMap!.ConvertGenshinMapCoordinatesToImageCoordinates(new Point2f(p.X, p.Y));
        var factor = _imageToDisplayScale / Scale2048To256;
        return new Point((int)(image2048.X * factor), (int)(image2048.Y * factor));
    }

    /// <summary>
    /// 根据分段起始索引列表，枚举每段的 [start, end) 范围
    /// </summary>
    private static IEnumerable<(int start, int end)> EnumerateSegmentRanges(int pointCount, List<int> segments)
    {
        if (pointCount == 0)
        {
            yield break;
        }

        if (segments.Count == 0)
        {
            yield return (0, pointCount);
            yield break;
        }

        for (var i = 0; i < segments.Count; i++)
        {
            var start = segments[i];
            var end = i + 1 < segments.Count ? segments[i + 1] : pointCount;
            if (start < end)
            {
                yield return (start, end);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _refreshTimer.Stop();
        _displayMap?.Dispose();
        _displayMap = null;
    }
}
