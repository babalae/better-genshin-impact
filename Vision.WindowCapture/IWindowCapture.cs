using System.Drawing;
using Windows.Win32.Foundation;

namespace Vision.WindowCapture;

public interface IWindowCapture
{
    bool IsCapturing { get; }
    
    Task StartAsync(HWND hWnd);

    Bitmap? Capture();

    Task StopAsync();
}