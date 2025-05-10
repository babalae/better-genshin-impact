using BetterGenshinImpact.GameTask;
using Fischless.GameCapture;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BetterGenshinImpact.Helpers;
using OpenCvSharp;
using Wpf.Ui.Violeta.Controls;
using Size = OpenCvSharp.Size;

namespace BetterGenshinImpact.View;

public partial class CaptureTestWindow
{
    private IGameCapture? _capture;
    private Size _cacheSize;

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

    private static WriteableBitmap ToWriteableBitmap(Mat mat)
    {
        PixelFormat pixelFormat;
        var type = mat.Type();
        if (type == MatType.CV_8UC3)
        {
            pixelFormat = PixelFormats.Bgr24;
        }
        else if (type == MatType.CV_8UC4)
        {
            pixelFormat = PixelFormats.Bgra32;
        }
        else
        {
            throw new NotSupportedException($"Unsupported pixel format {type}");
        }

        var bitmap = new WriteableBitmap(mat.Width, mat.Height, 96, 96, pixelFormat, null);
        UpdateWriteableBitmap(bitmap, mat);

        return bitmap;
    }

    private static unsafe void UpdateWriteableBitmap(WriteableBitmap bitmap, Mat mat)
    {
        bitmap.Lock();
        var stride = bitmap.BackBufferStride;
        var step = mat.Step();
        if (stride == step)
        {
            var length = stride * bitmap.PixelHeight;
            Buffer.MemoryCopy(mat.Data.ToPointer(), bitmap.BackBuffer.ToPointer(), length, length);
        }
        else
        {
            var length = Math.Min(stride, step);
            for (var i = 0; i < bitmap.PixelHeight; i++)
            {
                Buffer.MemoryCopy((void*)(mat.Data + i * step), (void*)(bitmap.BackBuffer + i * stride), length, length);
            }
        }
        bitmap.AddDirtyRect(new Int32Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
        bitmap.Unlock();
    }

    private void Loop(object? sender, EventArgs e)
    {
        var sw = new Stopwatch();
        sw.Start();
        using var mat = _capture?.Capture();
        sw.Stop();
        Debug.WriteLine("截图耗时:" + sw.ElapsedMilliseconds);
        _captureTime += sw.ElapsedMilliseconds;

        if (mat != null)
        {
            Debug.WriteLine($"Bitmap:{mat.Width}x{mat.Height}");
            _captureCount++;
            sw.Reset();
            sw.Start();
            if (_cacheSize != mat.Size())
            {
                DisplayCaptureResultImage.Source = ToWriteableBitmap(mat);
                _cacheSize = mat.Size();
            }
            else
            {
                UpdateWriteableBitmap((WriteableBitmap)DisplayCaptureResultImage.Source, mat);
            }
            sw.Stop();
            Debug.WriteLine("转换耗时:" + sw.ElapsedMilliseconds);
            _transferTime += sw.ElapsedMilliseconds;
        }
        else
        {
            Debug.WriteLine("截图失败");
        }
    }
}
