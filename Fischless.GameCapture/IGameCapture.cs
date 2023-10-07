namespace Fischless.GameCapture;

public interface IGameCapture : IDisposable
{
    public bool IsCapturing { get; }

    public void Start(nint hWnd);

    public Bitmap? Capture();

    public void Stop();
}
