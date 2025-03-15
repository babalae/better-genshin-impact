using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers.Extensions;
using Fischless.GameCapture;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using BetterGenshinImpact.Helpers;
using OpenCvSharp.WpfExtensions;
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

    private void Loop(object? sender, EventArgs e)
    {
        var sw = new Stopwatch();
        sw.Start();
        var bitmap = _capture?.Capture();
        sw.Stop();
        Debug.WriteLine("截图耗时:" + sw.ElapsedMilliseconds);
        _captureTime += sw.ElapsedMilliseconds;

        if (bitmap != null)
        {
            Debug.WriteLine($"Bitmap:{bitmap.Width}x{bitmap.Height}");
            _captureCount++;
            sw.Reset();
            sw.Start();
            DisplayCaptureResultImage.Source = bitmap.ToBitmapSource();
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
