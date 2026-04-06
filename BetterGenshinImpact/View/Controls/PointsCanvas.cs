using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BetterGenshinImpact.Model.MaskMap;
using BetterGenshinImpact.ViewModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.View.Controls;

/// <summary>
/// 高性能点位绘制控件，使用 DrawingVisual
/// </summary>
public class PointsCanvas : FrameworkElement
{
    private readonly VisualCollection _children;
    private readonly DrawingVisual _drawingVisual;
    private readonly Dictionary<string, Brush> _colorBrushCache;
    private readonly Random _random;
    private int _refreshQueued;

    // 私有字段
    private ObservableCollection<MaskMapPoint>? _points;
    private List<MaskMapPoint> _allPoints = new();
    private Dictionary<string, MaskMapPointLabel> _labelMap = new();
    private Rect _viewportRect = Rect.Empty;

    public event EventHandler? ViewportChanged;

    #region 依赖属性

    public static readonly DependencyProperty PointsSourceProperty =
        DependencyProperty.Register(
            nameof(PointsSource),
            typeof(ObservableCollection<MaskMapPoint>),
            typeof(PointsCanvas),
            new PropertyMetadata(null, OnPointsSourceChanged));

    public static readonly DependencyProperty LabelsSourceProperty =
        DependencyProperty.Register(
            nameof(LabelsSource),
            typeof(IEnumerable<MaskMapPointLabel>),
            typeof(PointsCanvas),
            new PropertyMetadata(null, OnLabelsSourceChanged));

    public static readonly DependencyProperty PointClickCommandProperty =
        DependencyProperty.Register(
            nameof(PointClickCommand),
            typeof(ICommand),
            typeof(PointsCanvas),
            new PropertyMetadata(null));

    public static readonly DependencyProperty PointRightClickCommandProperty =
        DependencyProperty.Register(
            nameof(PointRightClickCommand),
            typeof(ICommand),
            typeof(PointsCanvas),
            new PropertyMetadata(null));

    public static readonly DependencyProperty PointHoverCommandProperty =
        DependencyProperty.Register(
            nameof(PointHoverCommand),
            typeof(ICommand),
            typeof(PointsCanvas),
            new PropertyMetadata(null));

    /// <summary>
    /// 点击命令
    /// </summary>
    public ICommand PointClickCommand
    {
        get => (ICommand)GetValue(PointClickCommandProperty);
        set => SetValue(PointClickCommandProperty, value);
    }

    public ObservableCollection<MaskMapPoint>? PointsSource
    {
        get => (ObservableCollection<MaskMapPoint>?)GetValue(PointsSourceProperty);
        set => SetValue(PointsSourceProperty, value);
    }

    public IEnumerable<MaskMapPointLabel>? LabelsSource
    {
        get => (IEnumerable<MaskMapPointLabel>?)GetValue(LabelsSourceProperty);
        set => SetValue(LabelsSourceProperty, value);
    }

    /// <summary>
    /// 右键点击命令
    /// </summary>
    public ICommand PointRightClickCommand
    {
        get => (ICommand)GetValue(PointRightClickCommandProperty);
        set => SetValue(PointRightClickCommandProperty, value);
    }

    /// <summary>
    /// 悬停命令
    /// </summary>
    public ICommand PointHoverCommand
    {
        get => (ICommand)GetValue(PointHoverCommandProperty);
        set => SetValue(PointHoverCommandProperty, value);
    }

    #endregion

    private static void OnPointsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var canvas = (PointsCanvas)d;
        canvas.UpdatePoints(e.NewValue as ObservableCollection<MaskMapPoint>);
    }

    private static void OnLabelsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var canvas = (PointsCanvas)d;
        canvas.UpdateLabels(e.NewValue as IEnumerable<MaskMapPointLabel>);
    }

    public PointsCanvas()
    {
        _children = new VisualCollection(this);
        _drawingVisual = new DrawingVisual();
        _children.Add(_drawingVisual);
        _colorBrushCache = new Dictionary<string, Brush>();
        _random = new Random();

        // 注册鼠标事件
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseRightButtonDown += OnMouseRightButtonDown;
        MouseMove += OnMouseMove;
        
        // 启用命中测试
        IsHitTestVisible = true;

        MapIconImageCache.ImageUpdated += PointImageCacheManagerOnImageUpdated;
    }

    protected override void OnVisualParentChanged(DependencyObject oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        if (VisualParent == null)
        {
            MapIconImageCache.ImageUpdated -= PointImageCacheManagerOnImageUpdated;
        }
    }

    private void PointImageCacheManagerOnImageUpdated(object? sender, string e)
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) != 0)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            Interlocked.Exchange(ref _refreshQueued, 0);
            Refresh();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    #region 集合和属性变更处理

    private void OnPointsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (MaskMapPoint point in e.OldItems)
                UnsubscribePoint(point);
        }

        if (e.NewItems != null)
        {
            foreach (MaskMapPoint point in e.NewItems)
                SubscribePoint(point);
        }

        // 更新内部点位列表
        _allPoints = _points?.ToList() ?? new List<MaskMapPoint>();
        Refresh();
    }

    private void SubscribePoint(MaskMapPoint point)
    {
        if (point is INotifyPropertyChanged notifyPoint)
        {
            notifyPoint.PropertyChanged += OnPointPropertyChanged;
        }
    }

    private void UnsubscribePoint(MaskMapPoint point)
    {
        if (point is INotifyPropertyChanged notifyPoint)
        {
            notifyPoint.PropertyChanged -= OnPointPropertyChanged;
        }
    }

    private void OnPointPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        // 点位属性变化时重绘
        // 可以根据 e.PropertyName 做更细粒度的优化
        Refresh();
    }

    #endregion

    #region Visual 相关

    protected override int VisualChildrenCount => _children.Count;

    protected override Visual GetVisualChild(int index)
    {
        if (index < 0 || index >= _children.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        return _children[index];
    }

    #endregion

    #region 渲染逻辑

    /// <summary>
    /// 渲染所有点位
    /// </summary>
    private void RenderPoints()
    {
        using var dc = _drawingVisual.RenderOpen();
        if (_allPoints.Count == 0)
        {
            return;
        }

        if (!_viewportRect.IsEmpty)
        {
            // 扩展可视区域，避免边缘闪烁
            var expandedViewport = _viewportRect;
            expandedViewport.Inflate(MaskMapPointStatic.Width, MaskMapPointStatic.Height);

            var aw = ActualWidth;
            var ah = ActualHeight;
            if (aw <= 0 || ah <= 0)
            {
                return;
            }

            var scaleX = aw / _viewportRect.Width;
            var scaleY = ah / _viewportRect.Height;

            foreach (var point in _allPoints)
            {
                if (expandedViewport.Contains(point.ImageX, point.ImageY))
                {
                    var localX = (point.ImageX - _viewportRect.X) * scaleX;
                    var localY = (point.ImageY - _viewportRect.Y) * scaleY;
                    DrawPoint(dc, point, localX, localY, MaskMapPointStatic.Width, MaskMapPointStatic.Height);
                }
            }
        }
    }

    /// <summary>
    /// 绘制单个点
    /// </summary>
    private void DrawPoint(DrawingContext dc, MaskMapPoint point, double centerX, double centerY, double width, double height)
    {
        double radius = width / 2.0;
        double strokeThickness = 2.0;

        Point circleCenter = new Point(centerX, centerY);

        var fillBrush = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString("#323947"));
        fillBrush.Freeze();

        // 边框颜色 #D3BC8E
        var borderBrush = new SolidColorBrush(Color.FromRgb(0xD3, 0xBC, 0x8E));
        borderBrush.Freeze();

        var borderPen = new Pen(borderBrush, strokeThickness);
        borderPen.Freeze();

        var shadowBrush = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0));
        shadowBrush.Freeze();

        var shadowOffset = new Point(2, 2);

        // 绘制圆形阴影
        var shadowCircleGeometry = new EllipseGeometry(
            new Point(circleCenter.X + shadowOffset.X, circleCenter.Y + shadowOffset.Y),
            radius, radius);
        dc.DrawGeometry(shadowBrush, null, shadowCircleGeometry);

        var circleGeometry = new EllipseGeometry(circleCenter, radius, radius);
        dc.DrawGeometry(fillBrush, borderPen, circleGeometry);

        if (_labelMap.TryGetValue(point.LabelId, out var label))
        {
            var image = MapIconImageCache.TryGet(label.IconUrl);
            if (image != null)
            {
                Rect imageRect = new Rect(
                    circleCenter.X - radius,
                    circleCenter.Y - radius,
                    width,
                    height
                );

                dc.PushClip(circleGeometry);
                dc.DrawImage(image, imageRect);
                dc.Pop();
            }
            else
            {
                _ = MapIconImageCache.GetAsync(label.IconUrl, CancellationToken.None);
                
                var brush = GetColorBrush(label);
                dc.DrawEllipse(brush, null, new Point(centerX, centerY), width / 2.0, height / 2.0);
            }
        }
        else
        {
            // 没有标签信息，绘制默认随机颜色圆点
            var color = GenerateRandomColor(point.Id);
            var brush = new SolidColorBrush(color);
            brush.Freeze();

            dc.DrawEllipse(brush, null, new Point(centerX, centerY), width / 2.0, height / 2.0);
        }
    }

    /// <summary>
    /// 获取颜色画刷（带缓存）
    /// </summary>
    private Brush GetColorBrush(MaskMapPointLabel label)
    {
        if (_colorBrushCache.TryGetValue(label.LabelId, out var cachedBrush))
            return cachedBrush;

        Color color;
        if (label.Color.HasValue)
        {
            var c = label.Color.Value;
            color = Color.FromArgb(c.A, c.R, c.G, c.B);
        }
        else
        {
            color = GenerateRandomColor(label.LabelId);
        }

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        _colorBrushCache[label.LabelId] = brush;

        return brush;
    }

    /// <summary>
    /// 根据字符串生成一致的随机颜色
    /// </summary>
    private Color GenerateRandomColor(string seed)
    {
        var hash = seed?.GetHashCode() ?? 0;
        var random = new Random(hash);
        return Color.FromRgb(
            (byte)random.Next(80, 256),
            (byte)random.Next(80, 256),
            (byte)random.Next(80, 256));
    }

    #endregion

    #region 鼠标交互

    private static async Task ExecuteAsyncRelayCommandSafe(IAsyncRelayCommand command, object? parameter)
    {
        try
        {
            await command.ExecuteAsync(parameter);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private bool TryGetPointCenterPosition(MaskMapPoint point, out Point center)
    {
        center = default;

        if (_viewportRect.IsEmpty)
        {
            return false;
        }

        var aw = ActualWidth;
        var ah = ActualHeight;
        if (aw <= 0 || ah <= 0)
        {
            return false;
        }

        var scaleX = aw / _viewportRect.Width;
        var scaleY = ah / _viewportRect.Height;
        center = new Point(
            (point.ImageX - _viewportRect.X) * scaleX,
            (point.ImageY - _viewportRect.Y) * scaleY);
        return true;
    }

    private async void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var position = e.GetPosition(this);
        var point = HitTest(position);

        if (point != null)
        {
            var anchor = TryGetPointCenterPosition(point, out var center) ? center : position;
            var args = new MaskMapPointClickArgs(point, anchor);
            if (PointClickCommand is IAsyncRelayCommand asyncCommand)
            {
                if (asyncCommand.CanExecute(args))
                {
                    e.Handled = true;
                    await ExecuteAsyncRelayCommandSafe(asyncCommand, args);
                }
            }
            else if (PointClickCommand?.CanExecute(args) == true)
            {
                PointClickCommand.Execute(args);
                e.Handled = true;
            }
        }
    }

    private async void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var position = e.GetPosition(this);
        var point = HitTest(position);

        if (point == null)
        {
            return;
        }

        if (PointRightClickCommand is IAsyncRelayCommand asyncCommand)
        {
            if (asyncCommand.CanExecute(point))
            {
                e.Handled = true;
                await ExecuteAsyncRelayCommandSafe(asyncCommand, point);
            }
        }
        else if (PointRightClickCommand?.CanExecute(point) == true)
        {
            PointRightClickCommand.Execute(point);
            e.Handled = true;
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var position = e.GetPosition(this);
        var point = HitTest(position);

        if (point != null)
        {
            Cursor = Cursors.Hand;
            
            if (PointHoverCommand is IAsyncRelayCommand asyncCommand)
            {
                if (asyncCommand.CanExecute(point))
                {
                    _ = ExecuteAsyncRelayCommandSafe(asyncCommand, point);
                }
            }
            else if (PointHoverCommand?.CanExecute(point) == true)
            {
                PointHoverCommand.Execute(point);
            }
        }
        else
        {
            Cursor = Cursors.Arrow;
        }
    }

    /// <summary>
    /// 命中测试 - 查找被点击的点位
    /// </summary>
    private MaskMapPoint HitTest(Point position)
    {
        // TODO 有问题，需要继续优化实现
        if (_allPoints == null || _allPoints.Count == 0)
            return null;

        for (int i = _allPoints.Count - 1; i >= 0; i--)
        {
            var point = _allPoints[i];

            if (_viewportRect.IsEmpty)
            {
                if (point.Contains(position.X, position.Y))
                    return point;
                continue;
            }

            if (!_viewportRect.Contains(point.ImageX, point.ImageY))
                continue;

            var aw = ActualWidth;
            var ah = ActualHeight;
            if (aw <= 0 || ah <= 0)
            {
                if (point.Contains(position.X, position.Y))
                    return point;
                continue;
            }

            var scaleX = aw / _viewportRect.Width;
            var scaleY = ah / _viewportRect.Height;
            var localX = (point.ImageX - _viewportRect.X) * scaleX;
            var localY = (point.ImageY - _viewportRect.Y) * scaleY;
            var localW = MaskMapPointStatic.Width * scaleX;
            var localH = MaskMapPointStatic.Height * scaleY;
            var rect = new Rect(localX - localW / 2.0, localY - localH / 2.0, localW, localH);
            if (rect.Contains(position))
                return point;
        }

        return null;
    }

    #endregion

    #region 公共方法

    /// <summary>
    /// 更新点位数据
    /// </summary>
    public void UpdatePoints(ObservableCollection<MaskMapPoint>? points)
    {
        // 取消订阅旧集合
        if (_points != null)
        {
            _points.CollectionChanged -= OnPointsCollectionChanged;
            foreach (var point in _points)
                UnsubscribePoint(point);
        }

        // 设置新集合
        _points = points;

        // 订阅新集合
        if (_points != null)
        {
            _points.CollectionChanged += OnPointsCollectionChanged;
            foreach (var point in _points)
                SubscribePoint(point);
            
            _allPoints = _points.ToList();
        }
        else
        {
            _allPoints.Clear();
        }

        Refresh();
    }

    /// <summary>
    /// 更新标签数据
    /// </summary>
    public void UpdateLabels(IEnumerable<MaskMapPointLabel>? labels)
    {
        if (labels != null)
        {
            _labelMap = labels.ToDictionary(l => l.LabelId, l => l);
            _colorBrushCache.Clear(); // 清除颜色缓存
        }
        else
        {
            _labelMap.Clear();
            _colorBrushCache.Clear();
        }

        Refresh();
    }

    /// <summary>
    /// 更新可视区域
    /// </summary>
    public void UpdateViewport(double x, double y, double width, double height)
    {
        var newRect = new Rect(x, y, width, height);
        if (newRect.Equals(_viewportRect))
        {
            return;
        }
        _viewportRect = newRect;
        Debug.WriteLine($"Viewport updated: {_viewportRect}");
        ViewportChanged?.Invoke(this, EventArgs.Empty);
        Refresh();
    }

    /// <summary>
    /// 强制重绘
    /// </summary>
    public void Refresh()
    {
        RenderPoints();
    }

    /// <summary>
    /// 获取指定位置的点位
    /// </summary>
    public MaskMapPoint GetPointAt(double x, double y)
    {
        return HitTest(new Point(x, y));
    }

    #endregion
}
