using BetterGenshinImpact.GameTask;
using Fischless.GameCapture;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Extensions;
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

            Debug.WriteLine(Lang.S["View_12146_3702ca"] + _captureTime * 1.0 / _captureCount);
            Debug.WriteLine(Lang.S["View_12145_f9eccf"] + _transferTime * 1.0 / _captureCount);
            Debug.WriteLine(Lang.S["View_12144_63a833"] + (_captureTime + _transferTime) * 1.0 / _captureCount);
        };
    }

    public void StartCapture(IntPtr hWnd, CaptureModes captureMode)
    {
        if (hWnd == IntPtr.Zero)
        {
            Toast.Warning(Lang.S["View_12143_5dde72"]);
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
        using var mat = _capture?.Capture();
        sw.Stop();
        Debug.WriteLine(Lang.S["View_12142_5b8e41"] + sw.ElapsedMilliseconds);
        _captureTime += sw.ElapsedMilliseconds;

        if (mat != null)
        {
            Debug.WriteLine($"Bitmap:{mat.Width}x{mat.Height}");
            _captureCount++;
            sw.Reset();
            sw.Start();
            if (_cacheSize != mat.Size())
            {
                DisplayCaptureResultImage.Source = mat.ToWriteableBitmap();
                _cacheSize = mat.Size();
            }
            else
            {
                mat.UpdateWriteableBitmap((WriteableBitmap)DisplayCaptureResultImage.Source);
            }
            sw.Stop();
            Debug.WriteLine(Lang.S["View_12141_bceaef"] + sw.ElapsedMilliseconds);
            _transferTime += sw.ElapsedMilliseconds;
        }
        else
        {
            Debug.WriteLine(Lang.S["GameTask_10694_4dad2c"]);
        }
    }
}
