using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers.DpiAwareness;
using BetterGenshinImpact.Helpers.Extensions;
using Mat = OpenCvSharp.Mat;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using BetterGenshinImpact.Helpers;
using System.Windows.Media;
using System.Windows.Interop;
using Vanara.PInvoke;
using Size = OpenCvSharp.Size;
using System.Windows.Media.Imaging;
using BetterGenshinImpact.GameTask.AutoSkip;
using BetterGenshinImpact.Service;

namespace BetterGenshinImpact.View.Windows;

public partial class PictureInPictureWindow : Window
{
    private const double MinWidth = 220;
    private const double MaxWidth = 1280;
    private const double MarginSize = 16;

    private double _aspectRatio = 16d / 9d;
    private bool _initializedPosition;
    private bool _pointerDown;
    private bool _dragging;
    private Point _downPoint;
    private Size _cacheSize;

    public event Action? ClosedByUser;

    public PictureInPictureWindow()
    {
        InitializeComponent();
        ShowActivated = false;
        Opacity = 0;
        Loaded += OnLoaded;
        CompositionTarget.Rendering += Loop;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120));
        BeginAnimation(OpacityProperty, fade);
        UpdateClip();
    }

    private void Loop(object? sender, EventArgs e)
    {
        if (PictureInPictureService.IsManuallyClosed || !IsVisible || TaskContext.Instance().Config.AutoSkipConfig.PictureInPictureSourceType != nameof(PictureSourceType.CaptureLoop))
        {
            return;
        }

        using var mat = TaskTriggerDispatcher.GlobalGameCapture?.Capture();
        if (mat != null)
        {
            if (_cacheSize != mat.Size() || PreviewImage.Source == null)
            {
                PreviewImage.Source = mat.ToWriteableBitmap();
                _cacheSize = mat.Size();
                if (!_initializedPosition)
                {
                    PositionNearGame(TaskContext.Instance().SystemInfo.CaptureAreaRect);
                    _initializedPosition = true;
                }
            }
            else
            {
                mat.UpdateWriteableBitmap((WriteableBitmap)PreviewImage.Source);
            }
        }
        else
        {
            Debug.WriteLine("截图失败");
        }
    }

    public void SetFrame(Mat? frame)
    {
        if (frame == null || TaskContext.Instance().Config.AutoSkipConfig.PictureInPictureSourceType != nameof(PictureSourceType.TriggerDispatcher))
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            // 转移所有权：后台线程把 frame 交给 UI 线程处理并释放
            _ = Dispatcher.BeginInvoke(new Action(() => SetFrame(frame)));
            return;
        }

        try
        {
            var size = new Size(frame.Width, frame.Height);
            if (_cacheSize != size || PreviewImage.Source is not WriteableBitmap wb)
            {
                var bitmap = frame.ToWriteableBitmap();
                PreviewImage.Source = bitmap;
                _cacheSize = size;
                UpdateSizeFromFrame(frame.Width, frame.Height);
                UpdateClip();
                if (!_initializedPosition)
                {
                    PositionNearGame(TaskContext.Instance().SystemInfo.CaptureAreaRect);
                    _initializedPosition = true;
                }
            }
            else
            {
                frame.UpdateWriteableBitmap(wb);
            }
        }
        finally
        {
            frame.Dispose();
        }
    }

    private void UpdateSizeFromFrame(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        _aspectRatio = width * 1d / height;
        if (double.IsNaN(Width) || Width == 0)
        {
            var targetWidth = Math.Clamp(width / 4d, MinWidth, MaxWidth / 1.5);
            Width = targetWidth;
            Height = targetWidth / _aspectRatio;
        }
    }

    private void PositionNearGame(RECT captureRect)
    {
        var dpi = DpiHelper.ScaleY;
        var targetLeft = captureRect.Left / dpi + captureRect.Width / dpi - Width - MarginSize;
        var targetTop = captureRect.Top / dpi + MarginSize;

        Left = Math.Max(0, targetLeft);
        Top = Math.Max(0, targetTop);
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var ratio = e.Delta > 0 ? 1.08 : 0.92;
        var nextWidth = Math.Clamp(Width * ratio, MinWidth, MaxWidth);
        Width = nextWidth;
        Height = nextWidth / _aspectRatio;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pointerDown = true;
        _dragging = false;
        _downPoint = e.GetPosition(this);
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            SystemControl.ActivateWindow();
        }

        _pointerDown = false;
        _dragging = false;
    }

    private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        ClosedByUser?.Invoke();
        Close();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_pointerDown || _dragging)
        {
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _downPoint.X) > 4 || Math.Abs(current.Y - _downPoint.Y) > 4)
        {
            _dragging = true;
            try
            {
                DragMove();
            }
            catch
            {
                // ignored
            }
        }
    }

    private void OnBorderSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateClip();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = User32.GetWindowLong(hwnd, User32.WindowLongFlags.GWL_EXSTYLE);
        _ = User32.SetWindowLong(hwnd, User32.WindowLongFlags.GWL_EXSTYLE, exStyle | (int)User32.WindowStylesEx.WS_EX_TOOLWINDOW);
    }

    private void UpdateClip()
    {
        if (PreviewImage == null || ContainerBorder == null)
        {
            return;
        }

        var radius = ContainerBorder.CornerRadius.TopLeft;
        if (ContainerBorder.ActualWidth <= 0 || ContainerBorder.ActualHeight <= 0)
        {
            return;
        }

        PreviewImage.Clip = new RectangleGeometry(new Rect(0, 0, ContainerBorder.ActualWidth, ContainerBorder.ActualHeight), radius, radius);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        PreviewImage.Source = null;
    }
}