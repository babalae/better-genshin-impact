using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Xaml.Behaviors;
using Vanara.PInvoke;

namespace BetterGenshinImpact.View.Behavior;

/// <summary>
/// 把窗口内容区锁定为固定宽高比。
///
/// 两条来源分开处理：
/// - 用户拖窗口边缘走 WM_SIZING，实时约束，比在 SizeChanged 里回写更平滑；
/// - 非拖拽的尺寸变化（改分辨率、Win+方向键、任务栏并排/层叠）不经过 WM_SIZING，
///   由 SizeChanged 按宽度反推高度补上。
/// </summary>
public sealed class WindowAspectRatioBehavior : Behavior<FrameworkElement>
{
    private const int WmEnterSizeMove = 0x0231;
    private const int WmExitSizeMove = 0x0232;
    private const int WmSizing = 0x0214;
    private const int WmszLeft = 1;
    private const int WmszRight = 2;
    private const int WmszTop = 3;
    private const int WmszTopLeft = 4;
    private const int WmszTopRight = 5;
    private const int WmszBottom = 6;
    private const int WmszBottomLeft = 7;
    private const int WmszBottomRight = 8;

    /// <summary>
    /// 是否启用宽高比约束。
    /// </summary>
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.Register(
            nameof(IsEnabled),
            typeof(bool),
            typeof(WindowAspectRatioBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>
    /// 目标宽高比，默认 16:9。
    /// </summary>
    public static readonly DependencyProperty AspectRatioProperty =
        DependencyProperty.Register(
            nameof(AspectRatio),
            typeof(double),
            typeof(WindowAspectRatioBehavior),
            new PropertyMetadata(16d / 9d, OnAspectRatioChanged));

    private Window? _window;
    private HwndSource? _source;
    private DispatcherOperation? _sizeCorrectionOperation;
    private bool _isInteractiveSizing;
    private Size? _lastInteractiveSize;

    /// <summary>程序自己改尺寸时置位，避免 SizeChanged 再次触发校正形成回环。</summary>
    private bool _correctingSize;

    /// <summary>
    /// 是否启用宽高比约束。
    /// </summary>
    public bool IsEnabled
    {
        get => (bool)GetValue(IsEnabledProperty);
        set => SetValue(IsEnabledProperty, value);
    }

    /// <summary>
    /// 目标宽高比。
    /// </summary>
    public double AspectRatio
    {
        get => (double)GetValue(AspectRatioProperty);
        set => SetValue(AspectRatioProperty, value);
    }

    /// <summary>
    /// 比例本身变化（例如用户把分身分辨率从 16:9 改成 16:10）时也要重新校正一次，
    /// 否则只有下次尺寸变化才会生效。
    ///
    /// 另外，打开「保持宽高比」的瞬间就把窗口调正，不必等用户下一次拖拽；
    /// 关闭时不动窗口尺寸——用户关掉它往往就是想自由拉伸。
    /// </summary>
    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WindowAspectRatioBehavior behavior && e.NewValue is true)
        {
            behavior.ScheduleSizeCorrection();
        }
    }

    private static void OnAspectRatioChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WindowAspectRatioBehavior behavior)
        {
            behavior._lastInteractiveSize = null;
            behavior.ScheduleSizeCorrection();
        }
    }

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.Loaded += OnAssociatedObjectLoaded;
        AssociatedObject.Unloaded += OnAssociatedObjectUnloaded;
        AssociatedObject.IsVisibleChanged += OnAssociatedObjectIsVisibleChanged;

        if (AssociatedObject.IsLoaded)
        {
            AttachWindowHook();
        }
    }

    /// <inheritdoc />
    protected override void OnDetaching()
    {
        AssociatedObject.Loaded -= OnAssociatedObjectLoaded;
        AssociatedObject.Unloaded -= OnAssociatedObjectUnloaded;
        AssociatedObject.IsVisibleChanged -= OnAssociatedObjectIsVisibleChanged;
        DetachWindowHook();
        base.OnDetaching();
    }

    private void OnAssociatedObjectLoaded(object sender, RoutedEventArgs e)
    {
        AttachWindowHook();
    }

    /// <summary>
    /// 宿主变可见时补一次校正。
    ///
    /// 连接提示覆盖时 RDP 宿主是折叠的，ActualWidth/Height 为 0，校正会被
    /// TryGetSizingMetrics 拒掉且不保留重试；登录后宿主变可见，但既不触发窗口
    /// SizeChanged、也不改比例绑定，窗口就一直停在连接前的尺寸。
    /// </summary>
    private void OnAssociatedObjectIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            ScheduleSizeCorrection();
        }
    }

    private void OnAssociatedObjectUnloaded(object sender, RoutedEventArgs e)
    {
        DetachWindowHook();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        DetachWindowHook();
    }

    private void AttachWindowHook()
    {
        if (_source is not null)
        {
            return;
        }

        _window = Window.GetWindow(AssociatedObject);
        _source = _window is null
            ? null
            : PresentationSource.FromVisual(_window) as HwndSource;
        if (_window is null || _source is null)
        {
            return;
        }

        _window.Closed += OnWindowClosed;
        _window.SizeChanged += OnWindowSizeChanged;
        _source.AddHook(WindowProc);
        ScheduleSizeCorrection();
    }

    private void DetachWindowHook()
    {
        CancelSizeCorrection();
        _isInteractiveSizing = false;
        _lastInteractiveSize = null;
        _source?.RemoveHook(WindowProc);
        _source = null;

        if (_window is not null)
        {
            _window.Closed -= OnWindowClosed;
            _window.SizeChanged -= OnWindowSizeChanged;
            _window = null;
        }
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // 最后一次布局可能晚于 WM_EXITSIZEMOVE；仍保留 WM_SIZING 已接受的尺寸。
        if (MatchesInteractiveSize())
        {
            CancelSizeCorrection();
            return;
        }

        ScheduleSizeCorrection();
    }

    private bool CanCorrectSize() =>
        IsEnabled && _window is not null && !_correctingSize && !_isInteractiveSizing
        && _window.WindowState == WindowState.Normal
        && _window.ResizeMode != ResizeMode.NoResize
        && !(_window.MinHeight == _window.MaxHeight && _window.MinHeight > 0)
        && double.IsFinite(AspectRatio) && AspectRatio > 0;

    private bool MatchesInteractiveSize()
    {
        if (_lastInteractiveSize is not { } size || _window is null
            || _source?.CompositionTarget is not { } target)
        {
            return false;
        }

        var transform = target.TransformToDevice;
        return Math.Abs(_window.ActualWidth * transform.M11 - size.Width) <= 1
            && Math.Abs(_window.ActualHeight * transform.M22 - size.Height) <= 1;
    }

    private void ScheduleSizeCorrection()
    {
        if (!CanCorrectSize() || _sizeCorrectionOperation is not null)
        {
            return;
        }

        // 同一轮布局只保留一次校正，执行时读最新尺寸；交互拖拽只走 WM_SIZING。
        _sizeCorrectionOperation = AssociatedObject.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() =>
            {
                _sizeCorrectionOperation = null;
                ConstrainHeightToRatio();
            }));
    }

    private void CancelSizeCorrection()
    {
        _sizeCorrectionOperation?.Abort();
        _sizeCorrectionOperation = null;
    }

    /// <summary>
    /// 按当前宽度把窗口高度校正到 <see cref="AspectRatio"/>。
    ///
    /// 宽度是基准：用户拖的、系统贴边给的宽度都保留，只调整高度。
    /// 但如果算出来的高度超出工作区，就反过来按高度定宽度，否则窗口会顶出屏幕。
    /// </summary>
    private void ConstrainHeightToRatio()
    {
        // 排队期间可能已进入拖拽、全屏或禁止调整大小，执行时必须重新检查。
        if (!CanCorrectSize() || _window is null || MatchesInteractiveSize())
        {
            return;
        }

        _lastInteractiveSize = null;
        var handle = new WindowInteropHelper(_window).Handle;
        if (handle == IntPtr.Zero || !TryGetSizingMetrics(handle, out var metrics))
        {
            return;
        }

        var transform = _source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var scaleX = transform.M11 > 0 ? transform.M11 : 1;
        var scaleY = transform.M22 > 0 ? transform.M22 : 1;

        // 按内容区算：窗口外框含标题栏与边框，直接拿外框尺寸推比例会把内容算歪。
        var frameWidth = metrics.FrameWidth / scaleX;
        var frameHeight = metrics.FrameHeight / scaleY;
        var contentWidth = _window.ActualWidth - frameWidth;
        if (contentWidth <= 0)
        {
            return;
        }

        // 用窗口所在显示器的工作区：SystemParameters.WorkArea 只给主显示器的。
        var screen = System.Windows.Forms.Screen.FromHandle(handle);

        var contentHeight = contentWidth / AspectRatio;
        var targetWidth = _window.ActualWidth;
        var targetHeight = contentHeight + frameHeight;
        var minHeight = Math.Max(0, _window.MinHeight);
        var minWidth = Math.Max(0, _window.MinWidth);

        // 下限：算出来的高度比 MinHeight 还小时，WPF 会把 Height 夹上去，
        // 那样内容区比例就歪了。改为按高度反推宽度，保持比例。
        if (targetHeight < minHeight)
        {
            targetHeight = minHeight;
            contentHeight = Math.Max(1, targetHeight - frameHeight);
            targetWidth = Math.Max(minWidth, contentHeight * AspectRatio + frameWidth);
        }

        // 上限同理：MaxHeight 是有限值时 WPF 会夹住高度，宽度必须按夹取后的高度重算，
        // 否则内容区比例失真。
        var maxHeight = _window.MaxHeight;
        if (double.IsFinite(maxHeight) && maxHeight > 0 && targetHeight > maxHeight)
        {
            targetHeight = Math.Max(minHeight, maxHeight);
            contentHeight = Math.Max(1, targetHeight - frameHeight);
            targetWidth = Math.Max(minWidth, contentHeight * AspectRatio + frameWidth);
        }

        // 上限：窗口位置是固定的，高度只能长到「底部贴住工作区底部」为止，
        // 否则多出来的部分会跑到屏幕外，画面被裁掉一截。
        var workAreaBottom = screen.WorkingArea.Bottom / scaleY;
        var maxHeightByPosition = workAreaBottom - _window.Top;
        if (maxHeightByPosition > 0 && targetHeight > maxHeightByPosition)
        {
            targetHeight = Math.Max(minHeight, maxHeightByPosition);
            contentHeight = Math.Max(1, targetHeight - frameHeight);
            targetWidth = contentHeight * AspectRatio + frameWidth;

            // 工作区压得太狠时宽度可能低于 MinWidth：WPF 会把它撑回去，宽度变了高度
            // 却没跟着变，比例就歪了。这里反过来按 MinWidth 重算高度，保持比例。
            if (targetWidth < minWidth)
            {
                targetWidth = minWidth;
                contentHeight = Math.Max(1, (targetWidth - frameWidth) / AspectRatio);
                targetHeight = contentHeight + frameHeight;

                // 反推出来的高度可能又顶出工作区底边：这种极端冲突下优先保证窗口
                // 不出屏（底边可见比比例精确更重要），比例交给 WPF 夹取时自然让步。
                if (targetHeight > maxHeightByPosition)
                {
                    targetHeight = Math.Max(minHeight, maxHeightByPosition);
                    targetWidth = Math.Max(minWidth, (targetHeight - frameHeight) * AspectRatio + frameWidth);
                }
            }
        }

        if (Math.Abs(targetHeight - _window.ActualHeight) < 1
            && Math.Abs(targetWidth - _window.ActualWidth) < 1)
        {
            return;
        }

        _correctingSize = true;
        try
        {
            _window.Width = targetWidth;
            _window.Height = targetHeight;
        }
        finally
        {
            _correctingSize = false;
        }
    }

    private nint WindowProc(
        nint hWnd,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (message == WmEnterSizeMove)
        {
            _isInteractiveSizing = false;
            _lastInteractiveSize = null;
            return 0;
        }

        if (message == WmExitSizeMove)
        {
            _isInteractiveSizing = false;
            return 0;
        }

        // 移动窗口也会进入 size/move 循环；只有 WM_SIZING 才接管尺寸校正，
        // 避免把拖动贴边或跨显示器移动产生的非拖拽尺寸变化一起跳过。
        if (message == WmSizing)
        {
            _isInteractiveSizing = true;
            CancelSizeCorrection();
        }

        if (message != WmSizing ||
            !IsEnabled ||
            _window?.WindowState != WindowState.Normal ||
            lParam == 0 ||
            !double.IsFinite(AspectRatio) ||
            AspectRatio <= 0 ||
            !TryGetSizingMetrics(hWnd, out var metrics))
        {
            return 0;
        }

        var sizingEdge = unchecked((int)wParam.ToInt64());
        var rect = Marshal.PtrToStructure<NativeRect>(lParam);
        if (!ConstrainRect(ref rect, sizingEdge, metrics))
        {
            return 0;
        }

        Marshal.StructureToPtr(rect, lParam, false);
        _lastInteractiveSize = new Size(rect.Right - rect.Left, rect.Bottom - rect.Top);
        handled = true;
        return 1;
    }

    private bool TryGetSizingMetrics(nint hWnd, out SizingMetrics metrics)
    {
        metrics = default;
        if (_window is null ||
            _source?.CompositionTarget is null ||
            AssociatedObject.ActualWidth <= 0 ||
            AssociatedObject.ActualHeight <= 0 ||
            !User32.GetWindowRect(hWnd, out var windowRect))
        {
            return false;
        }

        var contentTopLeft = AssociatedObject.PointToScreen(new Point(0, 0));
        var contentBottomRight = AssociatedObject.PointToScreen(
            new Point(AssociatedObject.ActualWidth, AssociatedObject.ActualHeight));
        var contentWidth = Math.Abs(contentBottomRight.X - contentTopLeft.X);
        var contentHeight = Math.Abs(contentBottomRight.Y - contentTopLeft.Y);
        if (contentWidth <= 0 || contentHeight <= 0)
        {
            return false;
        }

        var frameWidth = Math.Max(0, windowRect.Right - windowRect.Left - contentWidth);
        var frameHeight = Math.Max(0, windowRect.Bottom - windowRect.Top - contentHeight);
        var transformToDevice = _source.CompositionTarget.TransformToDevice;
        var minimumOuterWidth = _window.MinWidth * transformToDevice.M11;
        var minimumOuterHeight = _window.MinHeight * transformToDevice.M22;
        var minimumContentWidth = Math.Max(
            1,
            Math.Max(
                minimumOuterWidth - frameWidth,
                (minimumOuterHeight - frameHeight) * AspectRatio));

        metrics = new SizingMetrics(
            frameWidth,
            frameHeight,
            minimumContentWidth,
            minimumContentWidth / AspectRatio);
        return true;
    }

    private bool ConstrainRect(
        ref NativeRect rect,
        int sizingEdge,
        SizingMetrics metrics)
    {
        if (sizingEdge is < WmszLeft or > WmszBottomRight)
        {
            return false;
        }

        var requestedWidth = Math.Max(1, rect.Right - rect.Left);
        var requestedHeight = Math.Max(1, rect.Bottom - rect.Top);
        var widthDrivenSize = CalculateWidthDrivenSize(requestedWidth, metrics);
        var heightDrivenSize = CalculateHeightDrivenSize(requestedHeight, metrics);
        var useWidthAsDriver = sizingEdge is WmszLeft or WmszRight ||
                               sizingEdge is WmszTopLeft or WmszTopRight or WmszBottomLeft or WmszBottomRight &&
                               Math.Abs(widthDrivenSize.Height - requestedHeight) <=
                               Math.Abs(heightDrivenSize.Width - requestedWidth) / AspectRatio;
        var targetSize = useWidthAsDriver ? widthDrivenSize : heightDrivenSize;
        var targetWidth = Math.Max(1, (int)Math.Round(targetSize.Width));
        var targetHeight = Math.Max(1, (int)Math.Round(targetSize.Height));

        if (sizingEdge is WmszLeft or WmszTopLeft or WmszBottomLeft)
        {
            rect.Left = rect.Right - targetWidth;
        }
        else
        {
            rect.Right = rect.Left + targetWidth;
        }

        if (sizingEdge is WmszTop or WmszTopLeft or WmszTopRight)
        {
            rect.Top = rect.Bottom - targetHeight;
        }
        else
        {
            rect.Bottom = rect.Top + targetHeight;
        }

        return true;
    }

    private Size CalculateWidthDrivenSize(double requestedWidth, SizingMetrics metrics)
    {
        var contentWidth = Math.Max(
            metrics.MinimumContentWidth,
            requestedWidth - metrics.FrameWidth);
        return new Size(
            contentWidth + metrics.FrameWidth,
            contentWidth / AspectRatio + metrics.FrameHeight);
    }

    private Size CalculateHeightDrivenSize(double requestedHeight, SizingMetrics metrics)
    {
        var contentHeight = Math.Max(
            metrics.MinimumContentHeight,
            requestedHeight - metrics.FrameHeight);
        return new Size(
            contentHeight * AspectRatio + metrics.FrameWidth,
            contentHeight + metrics.FrameHeight);
    }

    private readonly record struct SizingMetrics(
        double FrameWidth,
        double FrameHeight,
        double MinimumContentWidth,
        double MinimumContentHeight);


    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}