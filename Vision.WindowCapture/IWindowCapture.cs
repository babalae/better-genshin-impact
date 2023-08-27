using System.Drawing;

namespace Vision.WindowCapture
{
    public interface IWindowCapture
    {
        bool IsCapturing { get; }
        
        void Start(IntPtr hWnd);

        Bitmap? Capture();

        void Stop();
    }
}