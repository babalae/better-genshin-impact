using OpenCvSharp;
using Vanara.PInvoke;

namespace Fischless.GameCapture;

public sealed class GameCaptureFrame : IDisposable
{
    public GameCaptureFrame(Mat frame, RECT? captureRect = null)
    {
        Frame = frame;
        CaptureRect = captureRect;
    }

    public Mat Frame { get; }

    public RECT? CaptureRect { get; }

    // WGC 的 SystemRelativeTime 和 Stopwatch 都以系统性能计数器为时基。
    // 缓存帧必须保留采集时刻，不能用调用 Capture 的时刻代替。
    public TimeSpan? SystemRelativeTime { get; init; }

    public void Dispose()
    {
        Frame.Dispose();
        GC.SuppressFinalize(this);
    }
}
