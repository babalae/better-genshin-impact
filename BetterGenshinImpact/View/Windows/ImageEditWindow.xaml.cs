using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Helpers.Ui;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BetterGenshinImpact.View.Windows;

/// <summary>
/// 背景图片编辑对话框：支持 90° 步进旋转与鼠标拖拽裁剪。
/// 旋转通过 TransformedBitmap 重建位图实现；裁剪框坐标始终以"当前编辑位图的像素坐标"存储，
/// 避免窗口缩放 / 旋转后坐标错位，保存时直接映射为 Int32Rect 交给 CroppedBitmap。
/// </summary>
public partial class ImageEditWindow
{
    private readonly string _sourcePath;
    private BitmapSource _originalImage = null!;
    private BitmapSource _editImage = null!; // 含旋转效果的当前编辑状态
    private int _rotation; // 0 / 90 / 180 / 270，已应用到 _editImage

    // 裁剪区域（编辑位图像素坐标），Empty 表示不裁剪
    private Rect _cropPx = Rect.Empty;

    // 拖拽状态
    private Point _dragStartPx; // 按下时的像素坐标
    private Rect _dragOrigPx; // 按下时的裁剪框
    private DragMode _mode;

    private enum DragMode
    {
        None,
        Create, // 新建选区
        Move, // 移动选区
        Resize // 边缘缩放（同时记录激活的边）
    }

    private enum ResizeEdge
    {
        None = 0,
        Left = 1,
        Top = 2,
        Right = 4,
        Bottom = 8
    }

    private ResizeEdge _activeEdges;

    /// <summary>编辑结果：最终应写入配置的图片路径；null 表示取消。</summary>
    public string? ResultImagePath { get; private set; }

    public ImageEditWindow(string imagePath)
    {
        _sourcePath = imagePath;
        InitializeComponent();
        SourceInitialized += (s, e) => WindowHelper.TryApplySystemBackdrop(this);
        Loaded += (_, _) => LoadImage();
    }

    private void LoadImage()
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad; // 立即载入内存，源文件不被锁定，可随时覆盖保存
            bmp.UriSource = new Uri(_sourcePath);
            bmp.EndInit();
            bmp.Freeze();
            _originalImage = NormalizeDpi(bmp);
        }
        catch (Exception e)
        {
            ThemedMessageBox.Show($"图片加载失败：{e.Message}", "错误", MessageBoxButton.OK, ThemedMessageBox.MessageBoxIcon.Error);
            DialogResult = false;
            return;
        }

        _editImage = _originalImage;
        PreviewImage.Source = _editImage;
        UpdateInfoText();
    }

    /// <summary>
    /// 将位图规范化为 96 DPI（像素数据不变，仅重写 DPI 元数据）。
    /// WPF 的 Image 控件按设备无关尺寸（PixelWidth × 96 / DpiX）布局，
    /// 若不规范化，非等轴 DPI 图片（如部分扫描件）的显示纵横比会与像素纵横比不一致，
    /// 导致裁剪框与底图错位、保存结果与用户框选区域不符。
    /// 规范化后 DIP 尺寸 == 像素尺寸，画布坐标映射逻辑保持简单正确。
    /// </summary>
    private static BitmapSource NormalizeDpi(BitmapSource source)
    {
        if (Math.Abs(source.DpiX - 96) < 0.5 && Math.Abs(source.DpiY - 96) < 0.5)
        {
            return source;
        }

        var format = source.Format;
        var stride = (source.PixelWidth * format.BitsPerPixel + 7) / 8;
        var buffer = new byte[stride * source.PixelHeight];
        source.CopyPixels(buffer, stride, 0);
        var normalized = BitmapSource.Create(source.PixelWidth, source.PixelHeight, 96, 96, format, source.Palette, buffer, stride);
        normalized.Freeze();
        return normalized;
    }

    private void UpdateInfoText()
    {
        var text = $"{_editImage.PixelWidth} × {_editImage.PixelHeight} px";
        if (!_cropPx.IsEmpty)
        {
            text += $" ｜ 裁剪区域 {(int)_cropPx.Width} × {(int)_cropPx.Height} px";
        }

        InfoText.Text = text;
    }

    #region 坐标映射（画布显示坐标 <-> 位图像素坐标）

    /// <summary>Uniform 拉伸下，显示坐标 = 偏移 + 像素 × 缩放。</summary>
    private (double scale, double offX, double offY) GetViewMapping()
    {
        var iw = (double)_editImage.PixelWidth;
        var ih = (double)_editImage.PixelHeight;
        var cw = CropCanvas.ActualWidth;
        var ch = CropCanvas.ActualHeight;
        if (iw <= 0 || ih <= 0 || cw <= 0 || ch <= 0)
        {
            return (1, 0, 0);
        }

        var s = Math.Min(cw / iw, ch / ih);
        return (s, (cw - iw * s) / 2, (ch - ih * s) / 2);
    }

    private Point DisplayToPixel(Point p)
    {
        var (s, ox, oy) = GetViewMapping();
        return new Point((p.X - ox) / s, (p.Y - oy) / s);
    }

    private Point PixelToDisplay(Point p)
    {
        var (s, ox, oy) = GetViewMapping();
        return new Point(p.X * s + ox, p.Y * s + oy);
    }

    /// <summary>把像素裁剪框换算为画布上的 Rectangle 位置尺寸。</summary>
    private void UpdateCropVisual()
    {
        if (_cropPx.IsEmpty)
        {
            CropRect.Visibility = Visibility.Collapsed;
        }
        else
        {
            var tl = PixelToDisplay(new Point(_cropPx.X, _cropPx.Y));
            var br = PixelToDisplay(new Point(_cropPx.X + _cropPx.Width, _cropPx.Y + _cropPx.Height));
            Canvas.SetLeft(CropRect, Math.Min(tl.X, br.X));
            Canvas.SetTop(CropRect, Math.Min(tl.Y, br.Y));
            CropRect.Width = Math.Abs(br.X - tl.X);
            CropRect.Height = Math.Abs(br.Y - tl.Y);
            CropRect.Visibility = CropRect.Width > 4 && CropRect.Height > 4
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    #endregion

    #region 裁剪框鼠标交互

    private void CropCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_originalImage is not { }) return;

        var mouse = e.GetPosition(CropCanvas);
        var px = DisplayToPixel(mouse);

        if (_cropPx.IsEmpty)
        {
            _mode = DragMode.Create;
            _dragStartPx = px;
            _cropPx = new Rect(px, px);
        }
        else
        {
            _activeEdges = HitTestEdges(px);
            if (_activeEdges != ResizeEdge.None)
            {
                _mode = DragMode.Resize;
            }
            else if (RectContains(_cropPx, px))
            {
                _mode = DragMode.Move;
            }
            else
            {
                // 点击框外：重新开一个选区
                _mode = DragMode.Create;
                _dragStartPx = px;
                _cropPx = new Rect(px, px);
            }

            _dragOrigPx = _cropPx;
        }

        _dragStartPx = px;
        CropCanvas.CaptureMouse();
        UpdateCropVisual();
        e.Handled = true;
    }

    private void CropCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_mode == DragMode.None || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var px = DisplayToPixel(e.GetPosition(CropCanvas));
        var imgW = (double)_editImage.PixelWidth;
        var imgH = (double)_editImage.PixelHeight;

        switch (_mode)
        {
            case DragMode.Create:
            {
                var x1 = Math.Clamp(Math.Min(_dragStartPx.X, px.X), 0, imgW);
                var y1 = Math.Clamp(Math.Min(_dragStartPx.Y, px.Y), 0, imgH);
                var x2 = Math.Clamp(Math.Max(_dragStartPx.X, px.X), 0, imgW);
                var y2 = Math.Clamp(Math.Max(_dragStartPx.Y, px.Y), 0, imgH);
                _cropPx = new Rect(x1, y1, x2 - x1, y2 - y1);
                break;
            }
            case DragMode.Move:
            {
                var dx = px.X - _dragStartPx.X;
                var dy = px.Y - _dragStartPx.Y;
                var nx = Math.Clamp(_dragOrigPx.X + dx, 0, imgW - _dragOrigPx.Width);
                var ny = Math.Clamp(_dragOrigPx.Y + dy, 0, imgH - _dragOrigPx.Height);
                _cropPx = new Rect(nx, ny, _dragOrigPx.Width, _dragOrigPx.Height);
                break;
            }
            case DragMode.Resize:
            {
                var r = _dragOrigPx;
                var left = r.X;
                var top = r.Y;
                var right = r.X + r.Width;
                var bottom = r.Y + r.Height;
                if (_activeEdges.HasFlag(ResizeEdge.Left)) left = Math.Clamp(px.X, 0, right - MinPx);
                if (_activeEdges.HasFlag(ResizeEdge.Top)) top = Math.Clamp(px.Y, 0, bottom - MinPx);
                if (_activeEdges.HasFlag(ResizeEdge.Right)) right = Math.Clamp(px.X, left + MinPx, imgW);
                if (_activeEdges.HasFlag(ResizeEdge.Bottom)) bottom = Math.Clamp(px.Y, top + MinPx, imgH);
                _cropPx = new Rect(left, top, right - left, bottom - top);
                break;
            }
        }

        UpdateCropVisual();
        UpdateInfoText();
        e.Handled = true;
    }

    private void CropCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_mode == DragMode.None)
        {
            return;
        }

        CropCanvas.ReleaseMouseCapture();
        // 选区太小视为误触，直接清除
        if (!_cropPx.IsEmpty && (_cropPx.Width < MinPx || _cropPx.Height < MinPx))
        {
            _cropPx = Rect.Empty;
        }

        _mode = DragMode.None;
        _activeEdges = ResizeEdge.None;
        UpdateCropVisual();
        UpdateInfoText();
        e.Handled = true;
    }

    private void CropCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateCropVisual(); // 窗口缩放后按新映射比例重绘裁剪框
    }

    /// <summary>最小选区（像素），防止拖出 1px 的废框。</summary>
    private const double MinPx = 8;

    /// <summary>判断点是否命中裁剪框的某条边（容差按显示像素换算为图像像素）。</summary>
    private ResizeEdge HitTestEdges(Point px)
    {
        if (_cropPx.IsEmpty) return ResizeEdge.None;

        var (s, _, _) = GetViewMapping();
        var tol = Math.Max(4 / s, MinPx / 2); // 4 显示像素的命中容差
        var r = _cropPx;
        var edges = ResizeEdge.None;
        if (Math.Abs(px.X - r.X) <= tol && px.Y >= r.Y - tol && px.Y <= r.Y + r.Height + tol) edges |= ResizeEdge.Left;
        if (Math.Abs(px.X - (r.X + r.Width)) <= tol && px.Y >= r.Y - tol && px.Y <= r.Y + r.Height + tol) edges |= ResizeEdge.Right;
        if (Math.Abs(px.Y - r.Y) <= tol && px.X >= r.X - tol && px.X <= r.X + r.Width + tol) edges |= ResizeEdge.Top;
        if (Math.Abs(px.Y - (r.Y + r.Height)) <= tol && px.X >= r.X - tol && px.X <= r.X + r.Width + tol) edges |= ResizeEdge.Bottom;
        return edges;
    }

    private static bool RectContains(Rect r, Point p)
    {
        return p.X >= r.X && p.X <= r.X + r.Width && p.Y >= r.Y && p.Y <= r.Y + r.Height;
    }

    #endregion

    #region 工具栏按钮

    private void RotateLeft_Click(object sender, RoutedEventArgs e) => Rotate(-90);

    private void RotateRight_Click(object sender, RoutedEventArgs e) => Rotate(90);

    private void Rotate(int delta)
    {
        if (_originalImage is not { }) return;

        _rotation = ((_rotation + delta) % 360 + 360) % 360;
        if (_rotation == 0)
        {
            _editImage = _originalImage;
        }
        else
        {
            var tb = new TransformedBitmap(_originalImage, new RotateTransform(_rotation));
            tb.Freeze();
            _editImage = tb;
        }

        PreviewImage.Source = _editImage;
        _cropPx = Rect.Empty; // 旋转后区域含义变化，重置裁剪更直观
        UpdateCropVisual();
        UpdateInfoText();
    }

    private void ResetCrop_Click(object sender, RoutedEventArgs e)
    {
        _cropPx = Rect.Empty;
        UpdateCropVisual();
        UpdateInfoText();
    }

    #endregion

    #region 底部操作按钮

    private void UseOriginalButton_Click(object sender, RoutedEventArgs e)
    {
        ResultImagePath = _sourcePath;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_originalImage is not { }) return;

        BitmapSource final;
        if (!_cropPx.IsEmpty && _cropPx.Width >= MinPx && _cropPx.Height >= MinPx)
        {
            var rc = new Int32Rect(
                (int)Math.Floor(_cropPx.X),
                (int)Math.Floor(_cropPx.Y),
                (int)Math.Ceiling(_cropPx.Width),
                (int)Math.Ceiling(_cropPx.Height));
            // 越界保护
            rc = new Int32Rect(
                Math.Clamp(rc.X, 0, _editImage.PixelWidth - 1),
                Math.Clamp(rc.Y, 0, _editImage.PixelHeight - 1),
                Math.Min(rc.Width, _editImage.PixelWidth - rc.X),
                Math.Min(rc.Height, _editImage.PixelHeight - rc.Y));
            var cb = new CroppedBitmap(_editImage, rc);
            cb.Freeze();
            final = cb;
        }
        else
        {
            final = _editImage;
        }

        var saved = TrySave(final);
        if (saved == null)
        {
            ThemedMessageBox.Show("编辑结果保存失败，请检查磁盘权限或换一张图片重试。", "错误", MessageBoxButton.OK, ThemedMessageBox.MessageBoxIcon.Error);
            return;
        }

        ResultImagePath = saved;
        DialogResult = true;
    }

    /// <summary>
    /// 保存策略：优先存到源文件同目录（用户可见、方便管理）；
    /// 目录不可写时降级到程序 User\Background 目录（持久化，不会被系统临时清理删除）。
    /// 文件名带时间戳：重复编辑同一图片时输出路径必然变化，
    /// 从而保证配置 PropertyChanged 事件触发、主窗口重新加载新结果。
    /// 始终另存副本，绝不覆盖用户原图。
    /// </summary>
    private string? TrySave(BitmapSource image)
    {
        var dir = Path.GetDirectoryName(_sourcePath);
        var baseName = Path.GetFileNameWithoutExtension(_sourcePath);
        var fileName = $"{baseName}_bg_{DateTime.Now:yyyyMMddHHmmssfff}.png";

        if (!string.IsNullOrEmpty(dir))
        {
            try
            {
                var path = Path.Combine(dir, fileName);
                Encode(image, path);
                return path;
            }
            catch
            {
                // 源目录不可写，走降级路径
            }
        }

        try
        {
            var fallbackDir = Global.Absolute("User\\Background");
            Directory.CreateDirectory(fallbackDir);
            var fallback = Path.Combine(fallbackDir, fileName);
            Encode(image, fallback);
            return fallback;
        }
        catch
        {
            return null;
        }
    }

    private static void Encode(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        encoder.Save(fs);
    }

    #endregion
}
