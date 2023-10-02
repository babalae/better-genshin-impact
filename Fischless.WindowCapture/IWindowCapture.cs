namespace Fischless.WindowCapture;

public interface IWindowCapture : IDisposable
{
    public bool IsCapturing { get; }
    public bool IsClientEnabled { get; set; }

    public void Start(nint hWnd);

    public Bitmap? Capture();
    public Bitmap? Capture(int x, int y, int width, int height);

    public void Stop();
}
