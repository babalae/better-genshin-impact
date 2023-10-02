using System.Diagnostics;

namespace Fischless.WindowCapture;

public class WindowCaptureBenchmark
{
    public static void Action()
    {
        foreach (CaptureModes mode in Enum.GetValues(typeof(CaptureModes)))
        {
            _ = Task.Run(async () =>
            {
                IWindowCapture capture = WindowCaptureFactory.Create(mode);

                capture.Start(GetHwnd());
                await Task.Delay(1234);
                using Bitmap frame = capture.Capture();
                frame?.Save($"Benchmark_{mode}_{frame.Width}x{frame.Height}_DPI{DpiHelper.ScaleY * 100f:F0}.jpg");
            });
        }
    }

    private static nint GetHwnd()
    {
        Process[] processes = Process.GetProcessesByName("YuanShen");

        if (processes.Length <= 0)
        {
            processes = Process.GetProcessesByName("Genshin Impact");
        }
        if (processes.Length <= 0)
        {
            processes = Process.GetProcessesByName("GenshinImpact");
        }
        if (processes.Length > 0)
        {
            foreach (Process? process in processes)
            {
                return process.MainWindowHandle;
            }
        }
        return IntPtr.Zero;
    }
}
