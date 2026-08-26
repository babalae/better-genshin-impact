namespace Fischless.GameCapture;

public interface IGameCapture : IDisposable
{
    public bool IsCapturing { get; }

    /// <summary>
    /// 截图器当前实际使用的色彩管线。
    /// </summary>
    public CaptureColorMode ColorMode { get; }

    public void Start(nint hWnd, Dictionary<string, object>? settings = null);

    public GameCaptureFrame? Capture();

    public void Stop();
}
