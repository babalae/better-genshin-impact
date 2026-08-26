using OpenCvSharp;
using Vanara.PInvoke;

namespace Fischless.GameCapture;

public sealed class GameCaptureFrame : IDisposable
{
    public GameCaptureFrame(
        Mat frame,
        RECT? captureRect = null,
        CaptureColorMode colorMode = CaptureColorMode.Sdr)
    {
        Frame = frame;
        CaptureRect = captureRect;
        ColorMode = colorMode;
    }

    public Mat Frame { get; }

    public RECT? CaptureRect { get; }

    /// <summary>
    /// 当前帧实际使用的色彩管线；例如目标显示器未启用 HDR 时，HDR 请求会按 SDR 管线输出。
    /// </summary>
    public CaptureColorMode ColorMode { get; }

    public void Dispose()
    {
        Frame.Dispose();
        GC.SuppressFinalize(this);
    }
}
