using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.AutoTrackPath.Model;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Map;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using CvRect = OpenCvSharp.Rect;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;

namespace BetterGenshinImpact.View.Controls;

public sealed class MapTileViewerControl : FrameworkElement
{
    public static readonly DependencyProperty MapNameProperty = DependencyProperty.Register(
        nameof(MapName),
        typeof(string),
        typeof(MapTileViewerControl),
        new FrameworkPropertyMetadata(nameof(MapTypes.Teyvat), FrameworkPropertyMetadataOptions.AffectsRender, OnMapNameChanged));

    public static readonly DependencyProperty FollowZoomProperty = DependencyProperty.Register(
        nameof(FollowZoom),
        typeof(double),
        typeof(MapTileViewerControl),
        new FrameworkPropertyMetadata(5.0, FrameworkPropertyMetadataOptions.AffectsRender, OnFollowZoomChanged));

    public static readonly DependencyProperty ShowTeleportPointsProperty = DependencyProperty.Register(
        nameof(ShowTeleportPoints),
        typeof(bool),
        typeof(MapTileViewerControl),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    private const int TileSize = 512;
    private readonly Dictionary<TileKey, BitmapSource> _tileCache = new();
    private readonly HashSet<TileKey> _pendingTiles = [];
    private readonly object _tileCacheLock = new();
    private readonly object _mapImageLock = new();
    private readonly object _mapBitmapLock = new();
    private readonly Pen _pathPen = new(new SolidColorBrush(Color.FromRgb(63, 164, 255)), 3);
    private readonly Pen _recordPathPen = new(new SolidColorBrush(Color.FromRgb(255, 178, 72)), 2);
    private readonly Pen _recordRoutePreviewPen = new(new SolidColorBrush(Color.FromArgb(120, 160, 185, 196)), 1.5);
    private readonly Pen _targetPen = new(new SolidColorBrush(Color.FromRgb(255, 99, 99)), 2);
    private readonly Pen _teleportOutlinePen = new(new SolidColorBrush(Color.FromRgb(230, 245, 255)), 1.4);
    private readonly Pen _selectedRecorderOuterPen = new(new SolidColorBrush(Color.FromRgb(255, 255, 255)), 2);
    private readonly Pen _selectedRecorderInnerPen = new(new SolidColorBrush(Color.FromRgb(35, 200, 210)), 3);
    private readonly Brush _currentBrush = new SolidColorBrush(Color.FromRgb(80, 220, 120));
    private readonly Brush _targetBrush = new SolidColorBrush(Color.FromRgb(255, 99, 99));
    private readonly Brush _teleportBrush = new SolidColorBrush(Color.FromRgb(77, 174, 255));
    private readonly Brush _goddessBrush = new SolidColorBrush(Color.FromRgb(84, 238, 225));
    private readonly Brush _domainBrush = new SolidColorBrush(Color.FromRgb(179, 136, 255));
    private readonly Brush _labelBrush = new SolidColorBrush(Color.FromArgb(210, 32, 32, 32));
    private readonly Brush _selectedRecorderHaloBrush = new SolidColorBrush(Color.FromArgb(86, 35, 200, 210));
    private readonly Brush _selectedRecorderFillBrush = new SolidColorBrush(Color.FromRgb(35, 200, 210));
    private readonly Brush _recordRoutePreviewBrush = new SolidColorBrush(Color.FromArgb(150, 160, 185, 196));
    private readonly Typeface _typeface = new("Segoe UI");

    private Mat _mapImage = new();
    private BitmapSource? _mapBitmap;
    private string _loadedMapName = string.Empty;
    private string _loadingMapName = string.Empty;
    private double _sourceScaleX = 1;
    private double _sourceScaleY = 1;
    private double _zoom = 0.35;
    private WpfPoint _pan;
    private bool _followCurrentPosition = true;
    private bool _isMapLoading;
    private int _mapLoadVersion;
    private int _tileGenerationVersion;
    private bool _isDragging;
    private bool _dragExceeded;
    private WpfPoint _dragStart;
    private WpfPoint _dragStartPan;
    private Point2f? _currentFeaturePoint;
    private Point2f? _currentPoint;
    private Point2f? _targetPoint;
    private Point2f? _selectedRecorderPoint;
    private List<Point2f> _selectedRecorderPoints = [];
    private List<Point2f> _pathPoints = [];
    private List<RecordedMapPoint> _recordedPoints = [];
    private List<List<RecordedMapPoint>> _recordedRouteGroups = [];
    private List<MapTeleportPoint> _teleportPoints = [];
    private MapTeleportPoint? _hoverTeleportPoint;
    private bool _isRecorderMode;
    private bool _isDraggingRecorderPoint;
    private int _draggedRecorderPointIndex = -1;

    public MapTileViewerControl()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = Cursors.Hand;

        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseMove += OnMouseMove;
        MouseWheel += OnMouseWheel;
        ContextMenuOpening += OnContextMenuOpening;
        SizeChanged += (_, _) =>
        {
            if (_followCurrentPosition && _currentPoint is { } point)
            {
                CenterOn(point);
            }
        };

        WeakReferenceMessenger.Default.Register<PropertyChangedMessage<object>>(this, (_, msg) =>
        {
            RunOnUiThread(() => HandleMessage(msg));
        });
    }

    private void HandleMessage(PropertyChangedMessage<object> msg)
    {
            if (msg.PropertyName == "SendCurrentPosition" && msg.NewValue is Point2f point)
            {
                if (point.X == 0 && point.Y == 0)
                {
                    return;
                }

                _currentFeaturePoint = point;
                _currentPoint = ConvertFeatureImageCoordinateToDisplayPoint(point);
                if (_followCurrentPosition)
                {
                    CenterOn(_currentPoint.Value);
                }

                InvalidateVisual();
            return;
        }

        if (msg.PropertyName == "SelectPathingTargetPosition" && msg.NewValue is Point2f target)
        {
            _targetPoint = ConvertGameCoordinateToImagePoint(target);
            InvalidateVisual();
            return;
        }

        if (msg.PropertyName == "UpdateCurrentPathing" && msg.NewValue is PathingTask pathingTask)
        {
            _pathPoints = pathingTask.Positions.Select(ConvertToMapPoint).ToList();
            if (_followCurrentPosition && _currentPoint is { } current)
            {
                CenterOn(current);
            }
            else if (_pathPoints.Count > 0)
            {
                FitPath();
            }

            InvalidateVisual();
            return;
        }

        if (msg.PropertyName == "UpdateRecorderPathing" && msg.NewValue is PathingTask recorderTask)
        {
            _recordedPoints = recorderTask.Positions.Select(CreateRecordedMapPoint).ToList();
            InvalidateVisual();
            return;
        }

        if (msg.PropertyName == "UpdateRecorderRouteList" && msg.NewValue is IEnumerable<PathingTask> recorderTasks)
        {
            _recordedRouteGroups = recorderTasks
                .Select(task => task.Positions.Select(CreateRecordedMapPoint).ToList())
                .Where(points => points.Count > 0)
                .ToList();
            InvalidateVisual();
            return;
        }

        if (msg.PropertyName == "SelectRecorderWaypointPosition" && msg.NewValue is Point2f recorderPoint)
        {
            _selectedRecorderPoint = ConvertGameCoordinateToImagePoint(recorderPoint);
            _selectedRecorderPoints = [_selectedRecorderPoint.Value];
            InvalidateVisual();
            return;
        }

        if (msg.PropertyName == "SelectRecorderWaypointPositions" && msg.NewValue is IEnumerable<Point2f> recorderPoints)
        {
            _selectedRecorderPoints = recorderPoints
                .Select(ConvertGameCoordinateToImagePoint)
                .ToList();
            _selectedRecorderPoint = _selectedRecorderPoints.Count == 1
                ? _selectedRecorderPoints[0]
                : null;

            InvalidateVisual();
            return;
        }

        if (msg.PropertyName == "ClearSelectedRecorderWaypoint")
        {
            _selectedRecorderPoint = null;
            _selectedRecorderPoints = [];
            InvalidateVisual();
            return;
        }

        if (msg.PropertyName == "ClearCurrentPathing")
        {
            _pathPoints = [];
            InvalidateVisual();
            return;
        }

        if (msg.PropertyName == "ResetMapView")
        {
            SetFollowCurrentPosition(true);
            if (_currentPoint is { } current)
            {
                CenterOn(current);
            }
            else
            {
                FitMap();
            }

            InvalidateVisual();
            return;
        }

        if (msg.PropertyName == "LocateCurrentMapView")
        {
            var locatePoint = _currentPoint ?? ConvertGameCoordinateToImagePoint(new Point2f(0, 0));
            CenterOn(locatePoint, useFollowZoom: true);
            InvalidateVisual();
            return;
        }

        if (msg.PropertyName == "SetMapFollowCurrent" && msg.NewValue is bool followCurrent)
        {
            SetFollowCurrentPosition(followCurrent, false);
            if (_followCurrentPosition && _currentPoint is { } current)
            {
                CenterOn(current);
            }

            InvalidateVisual();
            return;
        }

        if (msg.PropertyName == "SetMapViewerRecorderMode" && msg.NewValue is bool recorderMode)
        {
            _isRecorderMode = recorderMode;
            InvalidateVisual();
            return;
        }

        if (msg.PropertyName == "MapZoomIn")
        {
            ZoomAroundCenter(1.25);
            return;
        }

        if (msg.PropertyName == "MapZoomOut")
        {
            ZoomAroundCenter(0.8);
            return;
        }

        if (msg.PropertyName == "MapToggleZoom")
        {
            ToggleZoomAroundCenter();
        }
    }

    private void RunOnUiThread(Action action)
    {
        if (Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _ = Dispatcher.BeginInvoke(action);
    }

    public string MapName
    {
        get => (string)GetValue(MapNameProperty);
        set => SetValue(MapNameProperty, value);
    }

    public double FollowZoom
    {
        get => (double)GetValue(FollowZoomProperty);
        set => SetValue(FollowZoomProperty, value);
    }

    public bool ShowTeleportPoints
    {
        get => (bool)GetValue(ShowTeleportPointsProperty);
        set => SetValue(ShowTeleportPointsProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        EnsureMapLoading();

        drawingContext.DrawRectangle(Brushes.Black, null, new WpfRect(0, 0, ActualWidth, ActualHeight));
        if (!HasMapImage() || ActualWidth <= 0 || ActualHeight <= 0)
        {
            DrawStatus(drawingContext, _isMapLoading ? "地图加载中..." : "地图资源未加载");
            return;
        }

        DrawMapImage(drawingContext);
        DrawTeleportPoints(drawingContext);
        DrawPath(drawingContext);
        DrawRecordedPath(drawingContext);
        DrawSelectedRecorderPoint(drawingContext);
        DrawTargetMarker(drawingContext, _targetPoint);
        DrawCurrentMarker(drawingContext);
    }

    private static void OnMapNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not MapTileViewerControl control)
        {
            return;
        }

        control._loadedMapName = string.Empty;
        control._loadingMapName = string.Empty;
        control._teleportPoints = [];
        control._hoverTeleportPoint = null;
        control.ClearTiles();
        control.InvalidateVisual();
    }

    private static void OnFollowZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not MapTileViewerControl control)
        {
            return;
        }

        if (control._followCurrentPosition && control._currentPoint is { } current)
        {
            control.CenterOn(current);
        }

        control.InvalidateVisual();
    }

    private void EnsureMapLoading()
    {
        var mapName = string.IsNullOrWhiteSpace(MapName) ? nameof(MapTypes.Teyvat) : MapName;
        if (string.Equals(_loadedMapName, mapName, StringComparison.OrdinalIgnoreCase) && HasMapImage())
        {
            return;
        }

        if (_isMapLoading && string.Equals(_loadingMapName, mapName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var version = Interlocked.Increment(ref _mapLoadVersion);
        _isMapLoading = true;
        _loadingMapName = mapName;
        _loadedMapName = string.Empty;
        ClearTiles();
        lock (_mapImageLock)
        {
            _mapImage.Dispose();
            _mapImage = new Mat();
        }
        lock (_mapBitmapLock)
        {
            _mapBitmap = null;
        }

        ResetInitialView(mapName);

        var dispatcher = Dispatcher;
        _ = Task.Run(() =>
        {
            var image = LoadMapImage(mapName, out var bitmap, out var sourceScaleX, out var sourceScaleY);
            return new LoadedMapResult(version, mapName, image, bitmap, sourceScaleX, sourceScaleY);
        }).ContinueWith(task => dispatcher.BeginInvoke(() =>
        {
            if (task.IsFaulted)
            {
                Debug.WriteLine(task.Exception);
                _isMapLoading = false;
                InvalidateVisual();
                return;
            }

            var result = task.Result;
            if (result.Version != _mapLoadVersion)
            {
                result.Image.Dispose();
                return;
            }

            lock (_mapImageLock)
            {
                _mapImage.Dispose();
                _mapImage = result.Image;
            }
            lock (_mapBitmapLock)
            {
                _mapBitmap = result.Bitmap;
            }

            _sourceScaleX = result.SourceScaleX;
            _sourceScaleY = result.SourceScaleY;
            _loadedMapName = result.MapName;
            _teleportPoints = LoadTeleportPoints(result.MapName);
            _hoverTeleportPoint = null;
            _isMapLoading = false;
            ClearTiles();
            if (_currentFeaturePoint is { } featurePoint)
            {
                _currentPoint = ConvertFeatureImageCoordinateToDisplayPoint(featurePoint);
            }

            if (_followCurrentPosition && _currentPoint is { } current)
            {
                CenterOn(current);
            }
            else
            {
                ResetInitialView(result.MapName);
            }

            InvalidateVisual();
        }));
    }

    private static Mat LoadMapImage(string mapName, out BitmapSource? bitmap, out double sourceScaleX, out double sourceScaleY)
    {
        bitmap = TryLoadRecorderMapBitmap(mapName);
        if (bitmap != null)
        {
            var recorderGeometry = GetMapGeometry(mapName);
            sourceScaleX = recorderGeometry.LogicalWidth / bitmap.PixelWidth;
            sourceScaleY = recorderGeometry.LogicalHeight / bitmap.PixelHeight;
            return new Mat();
        }

        sourceScaleX = 1;
        sourceScaleY = 1;
        var highQualityRelativePath = mapName switch
        {
            nameof(MapTypes.Teyvat) => string.Empty,
            _ => string.Empty
        };

        var fallbackRelativePath = mapName switch
        {
            nameof(MapTypes.Teyvat) => @"Assets/Map/Teyvat/Teyvat_0_256.png",
            nameof(MapTypes.TheChasm) => @"Assets/Map/TheChasm/TheChasm_0_1024.png",
            nameof(MapTypes.Enkanomiya) => @"Assets/Map/Enkanomiya/Enkanomiya_0_1024.png",
            nameof(MapTypes.SeaOfBygoneEras) => @"Assets/Map/SeaOfBygoneEras/SeaOfBygoneEras_0_1024.png",
            nameof(MapTypes.AncientSacredMountain) => @"Assets/Map/AncientSacredMountain/AncientSacredMountain_0_1024.png",
            nameof(MapTypes.TempleOfSpace) => @"Assets/Map/TempleOfSpace/TempleOfSpace_0_1024.png",
            _ => @"Assets/Map/Teyvat/Teyvat_0_256.png"
        };

        var mapImage = TryLoadImage(highQualityRelativePath);
        if (mapImage.Empty())
        {
            mapImage = TryLoadImage(fallbackRelativePath);
        }

        if (mapImage.Empty())
        {
            return new Mat();
        }

        var geometry = GetMapGeometry(mapName);
        sourceScaleX = geometry.LogicalWidth / (double)mapImage.Width;
        sourceScaleY = geometry.LogicalHeight / (double)mapImage.Height;
        if (double.IsNaN(sourceScaleX) || double.IsInfinity(sourceScaleX) || sourceScaleX <= 0)
        {
            sourceScaleX = 1;
        }

        if (double.IsNaN(sourceScaleY) || double.IsInfinity(sourceScaleY) || sourceScaleY <= 0)
        {
            sourceScaleY = sourceScaleX;
        }

        return mapImage;
    }

    private static BitmapSource? TryLoadRecorderMapBitmap(string mapName)
    {
        var fileName = mapName switch
        {
            nameof(MapTypes.Teyvat) => "1024_map.jpg",
            nameof(MapTypes.TheChasm) => "thechasm_1024_map.jpg",
            nameof(MapTypes.Enkanomiya) => "enkanomiya_1024_map.jpg",
            nameof(MapTypes.SeaOfBygoneEras) => "seaofbygoneeras_1024_map.jpg",
            nameof(MapTypes.AncientSacredMountain) => "ancientsacredmountain_1024.jpg",
            nameof(MapTypes.TempleOfSpace) => "templeofspace_1024.jpg",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var candidates = new[]
        {
            Global.Absolute(Path.Combine("Assets", "Map", "Editor", fileName)),
            Global.Absolute(Path.Combine("Assets", "Map", "Tracker", fileName)),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "bettergi-map", "public", fileName)),
        };

        foreach (var path in candidates.Where(File.Exists))
        {
            var image = TryLoadBitmap(path, mapName == nameof(MapTypes.Teyvat) ? 4096 : 2048);
            if (image != null)
            {
                return image;
            }
        }

        return null;
    }

    private static BitmapSource? TryLoadBitmap(string path, int decodePixelHeight)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.UriSource = new Uri(path, UriKind.Absolute);
            if (decodePixelHeight > 0)
            {
                image.DecodePixelHeight = decodePixelHeight;
            }

            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return null;
        }
    }

    private static Mat TryLoadImage(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return new Mat();
        }

        var path = Global.Absolute(relativePath);
        return File.Exists(path) ? new Mat(path, ImreadModes.Color) : new Mat();
    }

    private void DrawTiles(DrawingContext dc)
    {
        var mapWidth = _mapImage.Width * _sourceScaleX;
        var mapHeight = _mapImage.Height * _sourceScaleY;
        var viewport = ScreenToMap(new WpfRect(0, 0, ActualWidth, ActualHeight));
        var startX = Math.Max(0, (int)Math.Floor(viewport.Left / TileSize));
        var endX = Math.Min((int)Math.Ceiling(mapWidth / (double)TileSize), (int)Math.Ceiling(viewport.Right / TileSize));
        var startY = Math.Max(0, (int)Math.Floor(viewport.Top / TileSize));
        var endY = Math.Min((int)Math.Ceiling(mapHeight / (double)TileSize), (int)Math.Ceiling(viewport.Bottom / TileSize));

        for (var y = startY; y < endY; y++)
        {
            for (var x = startX; x < endX; x++)
            {
                var key = new TileKey(x, y);
                var image = GetTile(key);
                if (image == null)
                {
                    continue;
                }

                var mapRect = new WpfRect(x * TileSize, y * TileSize, TileSize, TileSize);
                var screenRect = MapToScreen(mapRect);
                dc.DrawImage(image, screenRect);
            }
        }
    }

    private void DrawMapImage(DrawingContext dc)
    {
        BitmapSource? directBitmap;
        lock (_mapBitmapLock)
        {
            directBitmap = _mapBitmap;
        }

        if (directBitmap != null)
        {
            var directMapRect = new WpfRect(0, 0, directBitmap.PixelWidth, directBitmap.PixelHeight);
            dc.DrawImage(directBitmap, MapToScreen(directMapRect));
            return;
        }

        BitmapSource? bitmap;
        lock (_tileCacheLock)
        {
            if (!_tileCache.TryGetValue(new TileKey(-1, -1), out bitmap))
            {
                bitmap = null;
            }
        }

        if (bitmap == null)
        {
            var version = _tileGenerationVersion;
            lock (_tileCacheLock)
            {
                if (!_pendingTiles.Add(new TileKey(-1, -1)))
                {
                    return;
                }
            }

            _ = Task.Run(BuildMapBitmap).ContinueWith(task => RunOnUiThread(() =>
            {
                lock (_tileCacheLock)
                {
                    _pendingTiles.Remove(new TileKey(-1, -1));
                }

                if (version != _tileGenerationVersion || task.IsFaulted || task.Result == null)
                {
                    if (task.IsFaulted)
                    {
                        Debug.WriteLine(task.Exception);
                    }

                    return;
                }

                lock (_tileCacheLock)
                {
                    _tileCache[new TileKey(-1, -1)] = task.Result;
                }

                InvalidateVisual();
            }));
            return;
        }

        var mapRect = new WpfRect(0, 0, _mapImage.Width, _mapImage.Height);
        dc.DrawImage(bitmap, MapToScreen(mapRect));
    }

    private BitmapSource? BuildMapBitmap()
    {
        try
        {
            lock (_mapImageLock)
            {
                if (_mapImage.Empty())
                {
                    return null;
                }

                var bitmap = _mapImage.ToBitmapSource();
                bitmap.Freeze();
                return bitmap;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return null;
        }
    }

    private BitmapSource? GetTile(TileKey key)
    {
        lock (_tileCacheLock)
        {
            if (_tileCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            if (!_pendingTiles.Add(key))
            {
                return null;
            }
        }

        var version = _tileGenerationVersion;
        var sourceScale = _sourceScaleX;
        _ = Task.Run(() => BuildTile(key, sourceScale)).ContinueWith(task => RunOnUiThread(() =>
        {
            lock (_tileCacheLock)
            {
                _pendingTiles.Remove(key);
            }

            if (version != _tileGenerationVersion)
            {
                return;
            }

            if (task.IsFaulted)
            {
                Debug.WriteLine(task.Exception);
                return;
            }

            if (task.Result == null)
            {
                return;
            }

            lock (_tileCacheLock)
            {
                if (_tileCache.Count > 256)
                {
                    _tileCache.Clear();
                }

                _tileCache[key] = task.Result;
            }

            InvalidateVisual();
        }));

        return null;
    }

    private BitmapSource? BuildTile(TileKey key, double sourceScale)
    {
        if (sourceScale <= 0 || double.IsNaN(sourceScale) || double.IsInfinity(sourceScale))
        {
            return null;
        }

        try
        {
            lock (_mapImageLock)
            {
                if (_mapImage.Empty())
                {
                    return null;
                }

                var sourceRect = new CvRect(
                    (int)Math.Floor(key.X * TileSize / sourceScale),
                    (int)Math.Floor(key.Y * TileSize / sourceScale),
                    Math.Max(1, (int)Math.Ceiling(TileSize / sourceScale)),
                    Math.Max(1, (int)Math.Ceiling(TileSize / sourceScale)));
                sourceRect = sourceRect.Intersect(new CvRect(0, 0, _mapImage.Width, _mapImage.Height));
                if (sourceRect.Width <= 0 || sourceRect.Height <= 0)
                {
                    return null;
                }

                using var source = new Mat(_mapImage, sourceRect);
                using var tile = new Mat();
                Cv2.Resize(source, tile, new OpenCvSharp.Size(TileSize, TileSize), 0, 0, InterpolationFlags.Linear);
                var bitmap = tile.ToBitmapSource();
                bitmap.Freeze();
                return bitmap;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return null;
        }
    }

    private void ClearTiles()
    {
        Interlocked.Increment(ref _tileGenerationVersion);
        lock (_tileCacheLock)
        {
            _tileCache.Clear();
            _pendingTiles.Clear();
        }
    }

    private void DrawPath(DrawingContext dc)
    {
        if (_pathPoints.Count < 2)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(MapToScreen(_pathPoints[0]), false, false);
            context.PolyLineTo(_pathPoints.Skip(1).Select(MapToScreen).ToList(), true, false);
        }

        geometry.Freeze();
        dc.DrawGeometry(null, _pathPen, geometry);
    }

    private void DrawTeleportPoints(DrawingContext dc)
    {
        if (!ShowTeleportPoints || _teleportPoints.Count == 0)
        {
            return;
        }

        var viewport = new WpfRect(-24, -24, ActualWidth + 48, ActualHeight + 48);
        foreach (var teleport in _teleportPoints)
        {
            var point = MapToScreen(teleport.DisplayPoint);
            if (!viewport.Contains(point))
            {
                continue;
            }

            DrawTeleportPoint(dc, teleport, point);
        }

        if (_hoverTeleportPoint is { } hover)
        {
            var point = MapToScreen(hover.DisplayPoint);
            if (viewport.Contains(point))
            {
                var label = string.IsNullOrWhiteSpace(hover.Name)
                    ? FormatTeleportType(hover.Type)
                    : $"{hover.Name} / {FormatTeleportType(hover.Type)}";
                var text = CreateText(label);
                var rect = new WpfRect(point.X + 12, point.Y - 15, Math.Min(text.Width + 14, 260), 28);
                dc.DrawRoundedRectangle(_labelBrush, null, rect, 6, 6);
                dc.DrawText(text, new WpfPoint(rect.X + 7, rect.Y + 5));
            }
        }
    }

    private void DrawTeleportPoint(DrawingContext dc, MapTeleportPoint teleport, WpfPoint point)
    {
        var brush = GetTeleportBrush(teleport.Type);
        if (IsGoddess(teleport.Type))
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(new WpfPoint(point.X, point.Y - 8), true, true);
                context.LineTo(new WpfPoint(point.X + 8, point.Y), true, false);
                context.LineTo(new WpfPoint(point.X, point.Y + 8), true, false);
                context.LineTo(new WpfPoint(point.X - 8, point.Y), true, false);
            }

            geometry.Freeze();
            dc.DrawGeometry(brush, _teleportOutlinePen, geometry);
            dc.DrawEllipse(Brushes.White, null, point, 2, 2);
            return;
        }

        if (IsDomain(teleport.Type))
        {
            dc.DrawEllipse(brush, _teleportOutlinePen, point, 6.5, 6.5);
            dc.DrawEllipse(null, new Pen(Brushes.White, 1), point, 3.5, 3.5);
            return;
        }

        dc.DrawEllipse(brush, _teleportOutlinePen, point, 5.5, 5.5);
        dc.DrawEllipse(Brushes.White, null, point, 2.2, 2.2);
    }

    private void DrawRecordedPath(DrawingContext dc)
    {
        DrawRecordedRoutePreviews(dc);

        if (_recordedPoints.Count == 0)
        {
            return;
        }

        if (_recordedPoints.Count >= 2)
        {
            for (var i = 0; i < _recordedPoints.Count - 1; i++)
            {
                if (IsTeleportWaypoint(_recordedPoints[i + 1].Type))
                {
                    continue;
                }

                dc.DrawLine(_recordPathPen, MapToScreen(_recordedPoints[i].Point), MapToScreen(_recordedPoints[i + 1].Point));
            }
        }

        for (var i = 0; i < _recordedPoints.Count; i++)
        {
            var point = MapToScreen(_recordedPoints[i].Point);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(255, 178, 72)), new Pen(Brushes.White, 1), point, 6, 6);
            var indexText = CreateText((i + 1).ToString());
            dc.DrawText(indexText, new WpfPoint(point.X + 8, point.Y - 14));
        }
    }

    private void DrawRecordedRoutePreviews(DrawingContext dc)
    {
        if (_recordedRouteGroups.Count == 0)
        {
            return;
        }

        foreach (var route in _recordedRouteGroups)
        {
            if (route.Count >= 2)
            {
                for (var i = 0; i < route.Count - 1; i++)
                {
                    if (IsTeleportWaypoint(route[i + 1].Type))
                    {
                        continue;
                    }

                    dc.DrawLine(_recordRoutePreviewPen, MapToScreen(route[i].Point), MapToScreen(route[i + 1].Point));
                }
            }

            foreach (var point in route)
            {
                dc.DrawEllipse(_recordRoutePreviewBrush, null, MapToScreen(point.Point), 3.5, 3.5);
            }
        }
    }

    private void DrawSelectedRecorderPoint(DrawingContext dc)
    {
        if (_selectedRecorderPoints.Count == 0)
        {
            if (_selectedRecorderPoint == null)
            {
                return;
            }

            DrawSelectedRecorderMarker(dc, _selectedRecorderPoint.Value);
            return;
        }

        foreach (var selectedPoint in _selectedRecorderPoints)
        {
            DrawSelectedRecorderMarker(dc, selectedPoint);
        }
    }

    private void DrawSelectedRecorderMarker(DrawingContext dc, Point2f mapPoint)
    {
        var point = MapToScreen(mapPoint);
        dc.DrawEllipse(_selectedRecorderHaloBrush, null, point, 19, 19);
        dc.DrawEllipse(null, _selectedRecorderOuterPen, point, 15, 15);
        dc.DrawEllipse(null, _selectedRecorderInnerPen, point, 11, 11);
        dc.DrawEllipse(_selectedRecorderFillBrush, null, point, 4.5, 4.5);
    }

    private void DrawTargetMarker(DrawingContext dc, Point2f? mapPoint)
    {
        if (mapPoint == null)
        {
            return;
        }

        var point = MapToScreen(mapPoint.Value);
        var haloBrush = new SolidColorBrush(Color.FromArgb(56, 255, 99, 99));
        var shadowBrush = new SolidColorBrush(Color.FromArgb(82, 0, 0, 0));
        var whitePen = new Pen(Brushes.White, 2.2);
        var targetPen = new Pen(_targetBrush, 2);

        dc.DrawEllipse(shadowBrush, null, new WpfPoint(point.X + 1, point.Y + 2), 15, 15);
        dc.DrawEllipse(haloBrush, null, point, 16, 16);
        dc.DrawEllipse(null, whitePen, point, 10, 10);
        dc.DrawEllipse(_targetBrush, _targetPen, point, 7, 7);
        dc.DrawLine(targetPen, new WpfPoint(point.X - 18, point.Y), new WpfPoint(point.X - 11, point.Y));
        dc.DrawLine(targetPen, new WpfPoint(point.X + 11, point.Y), new WpfPoint(point.X + 18, point.Y));
        dc.DrawLine(targetPen, new WpfPoint(point.X, point.Y - 18), new WpfPoint(point.X, point.Y - 11));
        dc.DrawLine(targetPen, new WpfPoint(point.X, point.Y + 11), new WpfPoint(point.X, point.Y + 18));
    }

    private void DrawCurrentMarker(DrawingContext dc)
    {
        if (_currentPoint == null)
        {
            return;
        }

        var point = MapToScreen(_currentPoint.Value);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(90, 80, 220, 120)), null, point, 18, 18);
        dc.DrawEllipse(_currentBrush, new Pen(Brushes.White, 2), point, 8, 8);
        dc.DrawLine(new Pen(Brushes.White, 1), new WpfPoint(point.X - 18, point.Y), new WpfPoint(point.X + 18, point.Y));
        dc.DrawLine(new Pen(Brushes.White, 1), new WpfPoint(point.X, point.Y - 18), new WpfPoint(point.X, point.Y + 18));

        var label = _currentFeaturePoint is { } featurePoint
            ? $"当前 {FormatGameCoordinate(featurePoint)}"
            : "当前";
        var text = CreateText(label);
        var labelRect = new WpfRect(point.X + 16, point.Y - 34, text.Width + 14, 26);
        dc.DrawRoundedRectangle(_labelBrush, null, labelRect, 5, 5);
        dc.DrawText(text, new WpfPoint(labelRect.X + 7, labelRect.Y + 4));
    }

    private void DrawStatus(DrawingContext dc, string status)
    {
        var text = CreateText(status);
        dc.DrawRoundedRectangle(_labelBrush, null, new WpfRect(12, ActualHeight - 38, text.Width + 18, 26), 6, 6);
        dc.DrawText(text, new WpfPoint(21, ActualHeight - 34));
    }

    private FormattedText CreateText(string text)
    {
        return new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            _typeface,
            12,
            Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (!TrySelectRecordedPointForContextMenu(Mouse.GetPosition(this)))
        {
            e.Handled = true;
        }
    }

    private bool TrySelectRecordedPointForContextMenu(WpfPoint screenPoint)
    {
        if (!_isRecorderMode || !TryFindNearestRecordedPoint(screenPoint, out var recordedPoint))
        {
            return false;
        }

        Focus();
        if (!IsSelectedRecorderPoint(recordedPoint.Point))
        {
            WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(
                this,
                "SelectRecorderWaypointIndex",
                new object(),
                recordedPoint.Index));
        }

        return true;
    }

    private bool IsSelectedRecorderPoint(Point2f point)
    {
        const float tolerance = 0.01f;
        return _selectedRecorderPoints.Any(selected =>
            Math.Abs(selected.X - point.X) < tolerance &&
            Math.Abs(selected.Y - point.Y) < tolerance);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        _isDragging = true;
        _dragExceeded = false;
        _dragStart = e.GetPosition(this);
        _dragStartPan = _pan;
        _isDraggingRecorderPoint = false;
        _draggedRecorderPointIndex = -1;
        if (_isRecorderMode && TryFindNearestRecordedPoint(_dragStart, out var recordedPoint))
        {
            _isDraggingRecorderPoint = true;
            _draggedRecorderPointIndex = recordedPoint.Index;
            Cursor = Cursors.SizeAll;
            WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(
                this,
                "SelectRecorderWaypointIndex",
                new object(),
                recordedPoint.Index));
        }

        CaptureMouse();
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        ReleaseMouseCapture();
        _isDragging = false;
        if (_isDraggingRecorderPoint)
        {
            if (!_dragExceeded && _draggedRecorderPointIndex >= 0)
            {
                WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(
                    this,
                    "SelectRecorderWaypointIndex",
                    new object(),
                    _draggedRecorderPointIndex));
            }

            _isDraggingRecorderPoint = false;
            _draggedRecorderPointIndex = -1;
            Cursor = Cursors.Hand;
            return;
        }

        if (_dragExceeded)
        {
            return;
        }

        var target = ScreenToMap(e.GetPosition(this));
        if (TryFindNearestTeleport(e.GetPosition(this), out var teleport))
        {
            target = teleport.DisplayPoint;
            _targetPoint = target;
            WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(
                this,
                "SelectPathingTargetPosition",
                new object(),
                new Point2f((float)teleport.GameX, (float)teleport.GameY)));
        }
        else
        {
            _targetPoint = target;
            WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "SelectPathingTargetPosition", new object(), ConvertImageCoordinateToGamePoint(target)));
        }

        InvalidateVisual();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging)
        {
            UpdateHoveredTeleport(e.GetPosition(this));
            UpdateRecorderPointHover(e.GetPosition(this));
            return;
        }

        var point = e.GetPosition(this);
        var dx = point.X - _dragStart.X;
        var dy = point.Y - _dragStart.Y;
        if (_isDraggingRecorderPoint && _draggedRecorderPointIndex >= 0)
        {
            if (Math.Abs(dx) + Math.Abs(dy) > 2)
            {
                _dragExceeded = true;
                SetFollowCurrentPosition(false);
            }

            var mapPoint = ScreenToMap(point);
            var gamePoint = ConvertImageCoordinateToGamePoint(mapPoint);
            UpdateRecordedPointPreview(_draggedRecorderPointIndex, mapPoint);
            WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(
                this,
                "MoveRecorderWaypointPosition",
                new object(),
                new RecorderWaypointMapUpdate(_draggedRecorderPointIndex, gamePoint)));
            InvalidateVisual();
            return;
        }

        if (Math.Abs(dx) + Math.Abs(dy) > 4)
        {
            _dragExceeded = true;
            SetFollowCurrentPosition(false);
        }

        _pan = new WpfPoint(_dragStartPan.X + dx, _dragStartPan.Y + dy);
        InvalidateVisual();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!HasMapImage())
        {
            return;
        }

        SetFollowCurrentPosition(false);
        ZoomAt(e.GetPosition(this), e.Delta > 0 ? 1.25 : 0.8);
    }

    private void ZoomAroundCenter(double factor)
    {
        if (!HasMapImage())
        {
            return;
        }

        SetFollowCurrentPosition(false);
        ZoomAt(new WpfPoint(ActualWidth / 2, ActualHeight / 2), factor);
    }

    private void ToggleZoomAroundCenter()
    {
        if (!HasMapImage())
        {
            return;
        }

        SetFollowCurrentPosition(false);
        var levels = new[] { 0.35, 1.0, 2.5, 5.0, 8.0 };
        var nextZoom = levels.FirstOrDefault(level => level > _zoom + 0.05);
        if (nextZoom <= 0)
        {
            nextZoom = levels[0];
        }

        SetZoomAt(new WpfPoint(ActualWidth / 2, ActualHeight / 2), nextZoom);
    }

    private void ZoomAt(WpfPoint screenPoint, double factor)
    {
        SetZoomAt(screenPoint, Math.Clamp(_zoom * factor, 0.12, 8));
    }

    private void SetZoomAt(WpfPoint screenPoint, double zoom)
    {
        var before = ScreenToMap(screenPoint);
        _zoom = Math.Clamp(zoom, 0.12, 8);
        var after = MapToScreen(before);
        _pan = new WpfPoint(_pan.X + screenPoint.X - after.X, _pan.Y + screenPoint.Y - after.Y);
        PublishZoom();
        InvalidateVisual();
    }

    private void FitMap()
    {
        if (!HasMapImage() || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var mapWidth = GetMapDisplayWidth();
        var mapHeight = GetMapDisplayHeight();
        _zoom = Math.Min(ActualWidth / mapWidth, ActualHeight / mapHeight);
        if (double.IsNaN(_zoom) || _zoom <= 0)
        {
            _zoom = 1;
        }

        _pan = new WpfPoint((ActualWidth - mapWidth * _zoom) / 2, (ActualHeight - mapHeight * _zoom) / 2);
        PublishZoom();
    }

    private bool HasMapImage()
    {
        lock (_mapBitmapLock)
        {
            if (_mapBitmap != null)
            {
                return true;
            }
        }

        lock (_mapImageLock)
        {
            return !_mapImage.Empty();
        }
    }

    private double GetMapDisplayWidth()
    {
        lock (_mapBitmapLock)
        {
            if (_mapBitmap != null)
            {
                return _mapBitmap.PixelWidth;
            }
        }

        lock (_mapImageLock)
        {
            return _mapImage.Width;
        }
    }

    private double GetMapDisplayHeight()
    {
        lock (_mapBitmapLock)
        {
            if (_mapBitmap != null)
            {
                return _mapBitmap.PixelHeight;
            }
        }

        lock (_mapImageLock)
        {
            return _mapImage.Height;
        }
    }

    private void ResetInitialView(string? mapName = null)
    {
        _zoom = 0.35;
        var origin = ConvertFeatureImageCoordinateToDisplayPoint(GetMapGeometry(mapName ?? MapName).Origin);
        _pan = new WpfPoint(ActualWidth / 2 - origin.X * _zoom, ActualHeight / 2 - origin.Y * _zoom);
        PublishZoom();
    }

    private void FitPath()
    {
        if (_pathPoints.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var minX = _pathPoints.Min(p => p.X);
        var minY = _pathPoints.Min(p => p.Y);
        var maxX = _pathPoints.Max(p => p.X);
        var maxY = _pathPoints.Max(p => p.Y);
        var width = Math.Max(256, maxX - minX);
        var height = Math.Max(256, maxY - minY);
        _zoom = Math.Clamp(Math.Min((ActualWidth - 80) / width, (ActualHeight - 80) / height), 0.12, 8);
        _pan = new WpfPoint(ActualWidth / 2 - (minX + width / 2) * _zoom, ActualHeight / 2 - (minY + height / 2) * _zoom);
        SetFollowCurrentPosition(false);
        PublishZoom();
    }

    private void FitPoints(IReadOnlyList<Point2f> points)
    {
        if (points.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var minX = points.Min(p => p.X);
        var minY = points.Min(p => p.Y);
        var maxX = points.Max(p => p.X);
        var maxY = points.Max(p => p.Y);
        var width = Math.Max(220, maxX - minX);
        var height = Math.Max(220, maxY - minY);
        var availableWidth = Math.Max(120, ActualWidth - 120);
        var availableHeight = Math.Max(120, ActualHeight - 120);
        _zoom = Math.Clamp(Math.Min(availableWidth / width, availableHeight / height), 0.12, 8);
        _pan = new WpfPoint(ActualWidth / 2 - (minX + width / 2) * _zoom, ActualHeight / 2 - (minY + height / 2) * _zoom);
        SetFollowCurrentPosition(false);
        PublishZoom();
    }

    private void CenterOn(Point2f point, bool useFollowZoom = false)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        if (_followCurrentPosition || useFollowZoom)
        {
            _zoom = GetSafeFollowZoom();
        }

        _pan = new WpfPoint(ActualWidth / 2 - point.X * _zoom, ActualHeight / 2 - point.Y * _zoom);
        PublishZoom();
    }

    private void PublishZoom()
    {
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "UpdateMapZoom", new object(), _zoom));
    }

    private double GetSafeFollowZoom()
    {
        return FollowZoom is > 0 and <= 20 && !double.IsNaN(FollowZoom) && !double.IsInfinity(FollowZoom)
            ? FollowZoom
            : 5.0;
    }

    private void SetFollowCurrentPosition(bool value, bool notify = true)
    {
        if (_followCurrentPosition == value)
        {
            return;
        }

        _followCurrentPosition = value;
        if (notify)
        {
            WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "MapFollowCurrentChanged", new object(), value));
        }
    }

    private void UpdateHoveredTeleport(WpfPoint screenPoint)
    {
        var previous = _hoverTeleportPoint;
        _hoverTeleportPoint = TryFindNearestTeleport(screenPoint, out var teleport) ? teleport : null;
        if (!ReferenceEquals(previous, _hoverTeleportPoint))
        {
            InvalidateVisual();
        }
    }

    private bool TryFindNearestTeleport(WpfPoint screenPoint, out MapTeleportPoint teleport)
    {
        teleport = default!;
        if (!ShowTeleportPoints || _teleportPoints.Count == 0)
        {
            return false;
        }

        var hitRadius = Math.Max(12, Math.Min(24, 8 * _zoom));
        var hitRadiusSquared = hitRadius * hitRadius;
        MapTeleportPoint? best = null;
        var bestDistance = double.MaxValue;
        foreach (var candidate in _teleportPoints)
        {
            var point = MapToScreen(candidate.DisplayPoint);
            var dx = point.X - screenPoint.X;
            var dy = point.Y - screenPoint.Y;
            var distance = dx * dx + dy * dy;
            if (distance > hitRadiusSquared || distance >= bestDistance)
            {
                continue;
            }

            best = candidate;
            bestDistance = distance;
        }

        if (best == null)
        {
            return false;
        }

        teleport = best;
        return true;
    }

    private void UpdateRecorderPointHover(WpfPoint screenPoint)
    {
        if (!_isRecorderMode)
        {
            Cursor = Cursors.Hand;
            return;
        }

        Cursor = TryFindNearestRecordedPoint(screenPoint, out _) ? Cursors.SizeAll : Cursors.Hand;
    }

    private bool TryFindNearestRecordedPoint(WpfPoint screenPoint, out RecordedMapPoint recordedPoint)
    {
        recordedPoint = default!;
        if (_recordedPoints.Count == 0)
        {
            return false;
        }

        const double hitRadius = 15;
        const double hitRadiusSquared = hitRadius * hitRadius;
        RecordedMapPoint? best = null;
        var bestDistance = double.MaxValue;
        foreach (var candidate in _recordedPoints)
        {
            var point = MapToScreen(candidate.Point);
            var dx = point.X - screenPoint.X;
            var dy = point.Y - screenPoint.Y;
            var distance = dx * dx + dy * dy;
            if (distance > hitRadiusSquared || distance >= bestDistance)
            {
                continue;
            }

            best = candidate;
            bestDistance = distance;
        }

        if (best == null)
        {
            return false;
        }

        recordedPoint = best;
        return true;
    }

    private void UpdateRecordedPointPreview(int index, Point2f mapPoint)
    {
        var listIndex = _recordedPoints.FindIndex(i => i.Index == index);
        if (listIndex < 0)
        {
            return;
        }

        _recordedPoints[listIndex] = _recordedPoints[listIndex] with { Point = mapPoint };
    }

    private List<MapTeleportPoint> LoadTeleportPoints(string mapName)
    {
        try
        {
            if (!MapLazyAssets.Instance.ScenesDic.TryGetValue(mapName, out var scene))
            {
                return [];
            }

            var matchingMethod = TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod;
            var map = MapManager.GetMap(mapName, matchingMethod);
            return scene.Points
                .Where(IsTeleportLike)
                .Select(tp => CreateTeleportPoint(map, tp))
                .Where(tp => tp != null)
                .Cast<MapTeleportPoint>()
                .ToList();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return [];
        }
    }

    private MapTeleportPoint? CreateTeleportPoint(ISceneMap map, GiTpPosition tp)
    {
        var gamePoint = new Point2f((float)tp.X, (float)tp.Y);
        var featurePoint = map.ConvertGenshinMapCoordinatesToImageCoordinates(gamePoint);
        var displayPoint = ConvertFeatureImageCoordinateToDisplayPoint(featurePoint);
        return new MapTeleportPoint(
            displayPoint,
            tp.X,
            tp.Y,
            tp.Name ?? string.Empty,
            tp.Type ?? string.Empty);
    }

    private static bool IsTeleportLike(GiTpPosition teleport)
    {
        if (string.IsNullOrWhiteSpace(teleport.Type))
        {
            return false;
        }

        return teleport.Type.Contains("Teleport", StringComparison.OrdinalIgnoreCase) ||
               teleport.Type.Contains("Domain", StringComparison.OrdinalIgnoreCase) ||
               IsGoddess(teleport.Type);
    }

    private static bool IsGoddess(string? type)
    {
        return string.Equals(type, "Goddess", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDomain(string? type)
    {
        return !string.IsNullOrWhiteSpace(type) && type.Contains("Domain", StringComparison.OrdinalIgnoreCase);
    }

    private Brush GetTeleportBrush(string? type)
    {
        if (IsGoddess(type))
        {
            return _goddessBrush;
        }

        return IsDomain(type) ? _domainBrush : _teleportBrush;
    }

    private static string FormatTeleportType(string? type)
    {
        if (IsGoddess(type))
        {
            return "七天神像";
        }

        if (IsDomain(type))
        {
            return "秘境";
        }

        return "传送锚点";
    }

    private Point2f ConvertToMapPoint(Waypoint waypoint)
    {
        return ConvertGameCoordinateToImagePoint(new Point2f((float)waypoint.X, (float)waypoint.Y));
    }

    private RecordedMapPoint CreateRecordedMapPoint(Waypoint waypoint, int index)
    {
        return new RecordedMapPoint(ConvertToMapPoint(waypoint), waypoint.Type, index);
    }

    private static bool IsTeleportWaypoint(string? type)
    {
        return string.Equals(type, WaypointType.Teleport.Code, StringComparison.OrdinalIgnoreCase);
    }

    private Point2f ConvertGameCoordinateToImagePoint(Point2f point)
    {
        return ConvertFeatureImageCoordinateToDisplayPoint(ConvertGameCoordinateToFeatureImagePoint(point));
    }

    private Point2f ConvertGameCoordinateToFeatureImagePoint(Point2f point)
    {
        var geometry = GetMapGeometry(MapName);
        return new Point2f(
            geometry.Origin.X - point.X * geometry.GameToImageScale,
            geometry.Origin.Y - point.Y * geometry.GameToImageScale);
    }

    private Point2f ConvertImageCoordinateToGamePoint(Point2f point)
    {
        return ConvertFeatureImageCoordinateToGamePoint(ConvertDisplayCoordinateToFeatureImagePoint(point));
    }

    private Point2f ConvertFeatureImageCoordinateToGamePoint(Point2f point)
    {
        var geometry = GetMapGeometry(MapName);
        return new Point2f(
            (geometry.Origin.X - point.X) / geometry.GameToImageScale,
            (geometry.Origin.Y - point.Y) / geometry.GameToImageScale);
    }

    private string FormatGameCoordinate(Point2f featurePoint)
    {
        try
        {
            var matchingMethod = TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod;
            var gamePoint = MapManager.GetMap(MapName, matchingMethod).ConvertImageCoordinatesToGenshinMapCoordinates(featurePoint);
            if (gamePoint is { } point)
            {
                return $"{point.X:F2}, {point.Y:F2}";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }

        var fallback = ConvertFeatureImageCoordinateToGamePoint(featurePoint);
        return $"{fallback.X:F2}, {fallback.Y:F2}";
    }

    private Point2f ConvertFeatureImageCoordinateToDisplayPoint(Point2f point)
    {
        var scale = GetFeatureToDisplayScale();
        return new Point2f((float)(point.X / scale), (float)(point.Y / scale));
    }

    private Point2f ConvertDisplayCoordinateToFeatureImagePoint(Point2f point)
    {
        var scale = GetFeatureToDisplayScale();
        return new Point2f((float)(point.X * scale), (float)(point.Y * scale));
    }

    private double GetFeatureToDisplayScale()
    {
        return _sourceScaleY > 0 && !double.IsNaN(_sourceScaleY) && !double.IsInfinity(_sourceScaleY)
            ? _sourceScaleY
            : 1;
    }

    private static MapGeometry GetMapGeometry(string mapName)
    {
        return mapName switch
        {
            nameof(MapTypes.TheChasm) => new MapGeometry(2, 2, 1, 1, 1024),
            nameof(MapTypes.Enkanomiya) => new MapGeometry(3, 3, 1, 1, 1024),
            nameof(MapTypes.SeaOfBygoneEras) => new MapGeometry(3, 4, 2, 5, 1024),
            nameof(MapTypes.AncientSacredMountain) => new MapGeometry(4, 4, 1, 1, 1024),
            nameof(MapTypes.TempleOfSpace) => new MapGeometry(4, 3, 1, 1, 1024),
            _ => new MapGeometry(15, 22, 7, 15, 2048)
        };
    }

    private WpfPoint MapToScreen(Point2f point)
    {
        return new WpfPoint(point.X * _zoom + _pan.X, point.Y * _zoom + _pan.Y);
    }

    private WpfRect MapToScreen(WpfRect mapRect)
    {
        return new WpfRect(mapRect.X * _zoom + _pan.X, mapRect.Y * _zoom + _pan.Y, mapRect.Width * _zoom, mapRect.Height * _zoom);
    }

    private Point2f ScreenToMap(WpfPoint point)
    {
        return new Point2f((float)((point.X - _pan.X) / _zoom), (float)((point.Y - _pan.Y) / _zoom));
    }

    private WpfRect ScreenToMap(WpfRect rect)
    {
        return new WpfRect(
            Math.Floor((rect.X - _pan.X) / _zoom),
            Math.Floor((rect.Y - _pan.Y) / _zoom),
            Math.Ceiling(rect.Width / _zoom),
            Math.Ceiling(rect.Height / _zoom));
    }

    private readonly record struct TileKey(int X, int Y);

    private sealed record LoadedMapResult(int Version, string MapName, Mat Image, BitmapSource? Bitmap, double SourceScaleX, double SourceScaleY);

    private sealed record RecordedMapPoint(Point2f Point, string? Type, int Index);

    private sealed record MapTeleportPoint(Point2f DisplayPoint, double GameX, double GameY, string Name, string Type);

    private readonly record struct MapGeometry(int Rows, int Cols, int UpRows, int LeftCols, int BlockWidth)
    {
        public double LogicalWidth => Cols * BlockWidth;

        public double LogicalHeight => Rows * BlockWidth;

        public Point2f Origin => new((LeftCols + 1) * BlockWidth, (UpRows + 1) * BlockWidth);

        public float GameToImageScale => BlockWidth / 1024f;
    }
}

public sealed record RecorderWaypointMapUpdate(int Index, Point2f Point);
