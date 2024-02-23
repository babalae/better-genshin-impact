namespace Fischless.GameCapture;

public interface IGameCapture : IDisposable
{
    public CaptureModes Mode { get; }
    public bool IsCapturing { get; }

    public void Start(nint hWnd, Dictionary<string, object>? settings = null);

    public Bitmap? Capture();

    public void Stop();
}
