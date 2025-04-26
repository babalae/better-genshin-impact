using BetterGenshinImpact.GameTask;
using Fischless.GameCapture;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BetterGenshinImpact.Helpers;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.View;

public partial class CaptureTestWindow : Window
{
    private IGameCapture? _capture;

    private long _captureTime;
    private long _transferTime;
    private long _captureCount;

    public CaptureTestWindow()
    {
        _captureTime = 0;
        _transferTime = 0;
        _captureCount = 0;
        InitializeComponent();
        Closed += (sender, args) =>
        {
            CompositionTarget.Rendering -= Loop;
            _capture?.Stop();

            Debug.WriteLine("平均截图耗时:" + _captureTime * 1.0 / _captureCount);
            Debug.WriteLine("平均转换耗时:" + _transferTime * 1.0 / _captureCount);
            Debug.WriteLine("平均总耗时:" + (_captureTime + _transferTime) * 1.0 / _captureCount);
        };
    }

    public void StartCapture(IntPtr hWnd, CaptureModes captureMode)
    {
        if (hWnd == IntPtr.Zero)
        {
            Toast.Warning("请选择窗口");
            return;
        }

        _capture = GameCaptureFactory.Create(captureMode);
        //_capture.IsClientEnabled = true;
        _capture.Start(hWnd,
            new Dictionary<string, object>()
            {
                { "autoFixWin11BitBlt", OsVersionHelper.IsWindows11 && TaskContext.Instance().Config.AutoFixWin11BitBlt }
            }
        );

        CompositionTarget.Rendering += Loop;
    }

    private static BitmapSource ConvertToBitmapSource(Bitmap bitmap, out bool bottomUp)
    {
        var bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            System.Drawing.Imaging.ImageLockMode.ReadOnly, bitmap.PixelFormat);

        var stride = bitmapData.Stride;
        var buffer = bitmapData.Scan0;
        if (stride < 0)
        {
            bottomUp = true;
            stride = -stride;
            buffer -= stride * (bitmapData.Height - 1);
        }
        else
        {
            bottomUp = false;
        }

        var pixelFormat = bitmap.PixelFormat switch
        {
            System.Drawing.Imaging.PixelFormat.Format24bppRgb => PixelFormats.Bgr24,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb => PixelFormats.Bgra32,
            _ => throw new NotSupportedException($"Unsupported pixel format {bitmap.PixelFormat}")
        };

        var bitmapSource = BitmapSource.Create(
            bitmapData.Width, bitmapData.Height,
            bitmap.HorizontalResolution, bitmap.VerticalResolution,
            pixelFormat, null,
            buffer, stride * bitmapData.Height, stride);

        bitmap.UnlockBits(bitmapData);

        return bitmapSource;
    }

    private void Loop(object? sender, EventArgs e)
    {
        var sw = new Stopwatch();
        sw.Start();
        var image = _capture?.Capture();
        sw.Stop();
        Debug.WriteLine("截图耗时:" + sw.ElapsedMilliseconds);
        _captureTime += sw.ElapsedMilliseconds;

        var bitmap = image?.ForceGetBitmap();
        if (bitmap != null)
        {
            Debug.WriteLine($"Bitmap:{bitmap.Width}x{bitmap.Height}");
            _captureCount++;
            sw.Reset();
            sw.Start();
            DisplayCaptureResultImage.Source = ConvertToBitmapSource(bitmap, out var bottomUp);
            sw.Stop();
            Debug.WriteLine("转换耗时:" + sw.ElapsedMilliseconds);
            _transferTime += sw.ElapsedMilliseconds;

            // 上下翻转渲染 bottom-up bitmap
            if (bottomUp && Transform.ScaleY > 0)
            {
                Transform.ScaleY = -1;
            }
            else if (!bottomUp && Transform.ScaleY < 0)
            {
                Transform.ScaleY = 1;
            }
        }
        else
        {
            Debug.WriteLine("截图失败");
        }
    }
}
