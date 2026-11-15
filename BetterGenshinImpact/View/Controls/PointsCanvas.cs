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
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using BetterGenshinImpact.Model.MaskMap;
using BetterGenshinImpact.ViewModel;
using CommunityToolkit.Mvvm.Input;
using Vanara.PInvoke;

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

    /// <summary>点击圆比图标圆半径内缩 1 DIP，避免边框和阴影扩大点击范围。</summary>
    private const double HitRadiusInset = 1;

    /// <summary>当前展开的重叠点位菜单，视口或点位变化时关闭。</summary>
    private ContextMenu? _selectionMenu;

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
    /// 可视区域（2048 级地图坐标），由 VM 绑定
    /// </summary>
    public static readonly DependencyProperty ViewportProperty =
        DependencyProperty.Register(
            nameof(Viewport),
            typeof(Rect),
            typeof(PointsCanvas),
            new PropertyMetadata(Rect.Empty, OnViewportChanged));

    public Rect Viewport
    {
        get => (Rect)GetValue(ViewportProperty);
        set => SetValue(ViewportProperty, value);
    }

    private static void OnViewportChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((PointsCanvas)d).UpdateViewport((Rect)e.NewValue);
    }

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
        IsVisibleChanged += (_, _) => CloseSelectionMenu();
        Unloaded += (_, _) => CloseSelectionMenu();
        
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
        CloseSelectionMenu();
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
        CloseSelectionMenu();
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

    /// <summary>隐藏图标置底，同一隐藏状态保持来源顺序；命中与菜单按绘制的反序遍历。</summary>
    private IEnumerable<MaskMapPoint> EnumeratePointsByStackingOrder(bool topFirst)
    {
        var start = topFirst ? _allPoints.Count - 1 : 0;
        var step = topFirst ? -1 : 1;
        for (var layer = 0; layer < 2; layer++)
        {
            var hiddenLayer = topFirst ? layer == 1 : layer == 0;
            for (var i = start; i >= 0 && i < _allPoints.Count; i += step)
            {
                var point = _allPoints[i];
                if (point.IsHidden == hiddenLayer) yield return point;
            }
        }
    }

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

        // 绘制与命中共用屏幕投影；边缘只露出一部分的圆也保留，图标尺寸不随地图缩放。
        foreach (var point in EnumeratePointsByStackingOrder(topFirst: false))
        {
            if (TryGetVisiblePointCenter(point, out var center))
            {
                DrawPoint(dc, point, center.X, center.Y, MaskMapPointStatic.Width, MaskMapPointStatic.Height);
            }
        }
    }

    /// <summary>
    /// 绘制单个点
    /// </summary>
    private void DrawPoint(DrawingContext dc, MaskMapPoint point, double centerX, double centerY, double width, double height)
    {
        if (point.IsHidden)
        {
            dc.PushOpacity(0.35);
        }

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

        if (point.IsHidden)
        {
            dc.Pop();
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

    /// <summary>将地图坐标投影到控件的 DIP 坐标；未定位时不保留旧坐标命中。</summary>
    private bool TryGetPointCenterPosition(MaskMapPoint point, out Point center)
    {
        center = default;

        if (_viewportRect.IsEmpty || _viewportRect.Width <= 0 || _viewportRect.Height <= 0)
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

    /// <summary>左键先判定重叠点位，需要消歧时展开菜单，再复用原有详情命令。</summary>
    private async void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var position = e.GetPosition(this);
        if (HitTest(position) == null) return;
        // 在异步详情请求开始前标记已处理，避免同一次点击继续向外传播。
        e.Handled = true;
        await HandlePointClickAsync(position, false);
    }

    /// <summary>右键也允许选择被遮挡点位，选定后执行原有隐藏切换命令。</summary>
    private async void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var position = e.GetPosition(this);
        if (HitTest(position) == null) return;
        e.Handled = true;
        await HandlePointClickAsync(position, true);
    }

    /// <summary>统一点击入口；单点直接执行，多点菜单保持由用户决定具体目标。</summary>
    internal async Task<bool> HandlePointClickAsync(Point position, bool rightClick)
    {
        CloseSelectionMenu();
        var points = GetPointsAt(position.X, position.Y);
        if (points.Count == 0)
        {
            return false;
        }

        var command = rightClick ? PointRightClickCommand : PointClickCommand;
        if (points.Count > 1)
        {
            _selectionMenu = CreateSelectionMenu(points, rightClick);
            _selectionMenu.Closed += OnSelectionMenuClosed;
            _selectionMenu.IsOpen = true;
            return true;
        }

        var parameter = GetClickParameter(points[0], rightClick);
        return await ExecutePointCommandAsync(command, parameter);
    }

    /// <summary>保持左右键现有命令参数协议，详情弹窗锚定最终选中图标而非列表位置。</summary>
    private object GetClickParameter(MaskMapPoint point, bool rightClick)
    {
        TryGetPointCenterPosition(point, out var center);
        return rightClick ? point : new MaskMapPointClickArgs(point, center);
    }

    /// <summary>命令仍由 ViewModel 处理，控件只负责输入消歧和异步异常隔离。</summary>
    private static async Task<bool> ExecutePointCommandAsync(ICommand? command, object parameter)
    {
        if (command?.CanExecute(parameter) != true)
        {
            return false;
        }

        if (command is IAsyncRelayCommand asyncCommand)
            await ExecuteAsyncRelayCommandSafe(asyncCommand, parameter);
        else
            command.Execute(parameter);
        return true;
    }

    /// <summary>沿用 WPF-UI 的菜单样式、键盘导航、菜单外点击关闭和长列表滚动。</summary>
    internal ContextMenu CreateSelectionMenu(IReadOnlyList<MaskMapPoint> points, bool rightClick)
    {
        TryGetPointCenterPosition(points[0], out var center);
        var radius = MaskMapPointStatic.Width / 2d;
        var menu = new ContextMenu
        {
            PlacementTarget = this,
            Placement = PlacementMode.Right,
            PlacementRectangle = new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2),
            HorizontalOffset = 8,
            MinWidth = 220,
            MaxHeight = 360,
            StaysOpen = false
        };
        // 关闭动画可能晚于 IsOpen=false；用真实 Closed 事件确认原生弹窗已完成焦点恢复。
        TaskCompletionSource? menuClosed = null;
        menu.Opened += (_, _) => menuClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        menu.Closed += (_, _) => menuClosed?.TrySetResult();

        foreach (var point in points)
        {
            _labelMap.TryGetValue(point.LabelId, out var label);
            var title = string.IsNullOrWhiteSpace(label?.Name) ? "点位" : label.Name;
            var header = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
            header.Children.Add(new TextBlock { Text = title, MaxWidth = 260, TextTrimming = TextTrimming.CharacterEllipsis });
            // 同类点位也要可区分；隐藏点位仍能选中，以便恢复显示。
            header.Children.Add(new TextBlock
            {
                Text = $"#{point.Id}" + (point.IsHidden ? " · 已隐藏" : string.Empty),
                FontSize = 11,
                Opacity = 0.65,
                MaxWidth = 260,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            var command = rightClick ? PointRightClickCommand : PointClickCommand;
            var parameter = GetClickParameter(point, rightClick);
            var item = new MenuItem
            {
                Header = header,
                ToolTip = $"{title} #{point.Id}",
                Command = new AsyncRelayCommand(() => ExecuteSelectionAsync(menu,
                        menuClosed?.Task ?? Task.CompletedTask, command, parameter),
                    () => command?.CanExecute(parameter) == true)
            };
            var icon = label == null ? null : MapIconImageCache.TryGet(label.IconUrl);
            if (icon != null)
            {
                item.Icon = new Image
                {
                    Source = icon,
                    Width = 24,
                    Height = 24,
                    Clip = new EllipseGeometry(new Point(12, 12), 12, 12)
                };
            }
            menu.Items.Add(item);
        }

        return menu;
    }

    /// <summary>先完成原生菜单关闭和焦点恢复，再将交互交回遮罩，保持与直接点击图标相同的前台状态。</summary>
    private async Task ExecuteSelectionAsync(ContextMenu menu, Task menuClosed, ICommand? command, object parameter)
    {
        menu.IsOpen = false;
        await menuClosed;
        // ContextMenu 关闭会恢复此前窗口的焦点。延后到输入事件处理结束，避免它再次把前台交给游戏。
        await Dispatcher.InvokeAsync(ActivateInteractionWindow, DispatcherPriority.Background);
        await ExecutePointCommandAsync(command, parameter);
    }

    /// <summary>让遮罩接续菜单交互；游戏失去前台后，调度器沿用直接点击点位时的暂停行为。</summary>
    protected virtual void ActivateInteractionWindow()
    {
        var window = Window.GetWindow(this);
        if (window?.IsVisible != true) return;
        window.Activate();
        _ = User32.SetForegroundWindow(new WindowInteropHelper(window).Handle);
        window.Focus();
    }

    /// <summary>菜单关闭后释放候选快照，不跨地图视口保留引用。</summary>
    private void OnSelectionMenuClosed(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu)
        {
            menu.Closed -= OnSelectionMenuClosed;
            if (ReferenceEquals(_selectionMenu, menu)) _selectionMenu = null;
        }
    }

    /// <summary>地图、候选或布局变化时撤销尚未确认的点位选择。</summary>
    private void CloseSelectionMenu()
    {
        if (_selectionMenu != null) _selectionMenu.IsOpen = false;
    }

    /// <summary>控件尺寸影响屏幕投影，调整窗口后重绘并关闭旧位置的菜单。</summary>
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        CloseSelectionMenu();
        Refresh();
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
    private MaskMapPoint? HitTest(Point position)
    {
        var radius = Math.Min(MaskMapPointStatic.Width, MaskMapPointStatic.Height) / 2d - HitRadiusInset;
        foreach (var point in EnumeratePointsByStackingOrder(topFirst: true))
        {
            if (TryGetVisiblePointCenter(point, out var center) && (position - center).LengthSquared <= radius * radius)
                return point;
        }

        return null;
    }

    /// <summary>绘制与命中使用相同可见范围，包含控件边缘部分露出的图标。</summary>
    private bool TryGetVisiblePointCenter(MaskMapPoint point, out Point center)
    {
        return TryGetPointCenterPosition(point, out center) &&
               center.X >= -MaskMapPointStatic.Width / 2d && center.X <= ActualWidth + MaskMapPointStatic.Width / 2d &&
               center.Y >= -MaskMapPointStatic.Height / 2d && center.Y <= ActualHeight + MaskMapPointStatic.Height / 2d;
    }

    /// <summary>命中后仅收集与该图标点击圆交叠超过一半的点位，不沿相邻点无限扩展。</summary>
    internal IReadOnlyList<MaskMapPoint> GetPointsAt(double x, double y)
    {
        var point = HitTest(new Point(x, y));
        if (point == null) return Array.Empty<MaskMapPoint>();
        TryGetPointCenterPosition(point, out var center);
        var radius = Math.Min(MaskMapPointStatic.Width, MaskMapPointStatic.Height) / 2d - HitRadiusInset;
        var points = new List<MaskMapPoint> { point };
        foreach (var candidate in EnumeratePointsByStackingOrder(topFirst: true))
        {
            if (ReferenceEquals(candidate, point) || !TryGetVisiblePointCenter(candidate, out var other)) continue;
            var distance = (other - center).Length;
            if (distance >= radius * 2) continue;

            // 等半径两圆交叠面积：2r²acos(d/2r) - d/2√(4r²-d²)，以单圆面积为分母。
            var overlap = 2 * radius * radius * Math.Acos(distance / (2 * radius)) -
                          distance / 2 * Math.Sqrt(4 * radius * radius - distance * distance);
            if (overlap > Math.PI * radius * radius / 2) points.Add(candidate);
        }
        return points;
    }

    #endregion

    #region 公共方法

    /// <summary>
    /// 更新点位数据
    /// </summary>
    public void UpdatePoints(ObservableCollection<MaskMapPoint>? points)
    {
        CloseSelectionMenu();
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
        CloseSelectionMenu();
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
    private void UpdateViewport(Rect newRect)
    {
        if (newRect.Equals(_viewportRect))
        {
            return;
        }
        _viewportRect = newRect;
        CloseSelectionMenu();
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
    public MaskMapPoint? GetPointAt(double x, double y)
    {
        return HitTest(new Point(x, y));
    }

    #endregion
}
