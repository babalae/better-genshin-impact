using MicaSetup.Helper;

namespace MicaSetup.Natives;

public static class DpiAwareHelper
{
    public static bool SetProcessDpiAwareness()
    {
        if (OsVersionHelper.IsWindows81_OrGreater)
        {
            if (SHCore.SetProcessDpiAwareness(PROCESS_DPI_AWARENESS.PROCESS_PER_MONITOR_DPI_AWARE) == 0)
            {
                return true;
            }
        }
        return false;
    }
}
