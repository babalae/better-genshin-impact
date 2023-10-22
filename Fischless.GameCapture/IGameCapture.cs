namespace Fischless.GameCapture;

public interface IGameCapture : IDisposable
{
    public CaptureModes Mode { get; }
    public bool IsCapturing { get; }

    public void Start(nint hWnd);

    public Bitmap? Capture();

    public void Stop();
}
