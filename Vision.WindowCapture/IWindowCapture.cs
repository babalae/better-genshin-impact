using System.Drawing;
using Windows.Win32.Foundation;

namespace Vision.WindowCapture;

public interface IWindowCapture
{
    bool IsCapturing { get; }
    
    void Start(HWND hWnd);

    Bitmap? Capture();

    void Stop();
}