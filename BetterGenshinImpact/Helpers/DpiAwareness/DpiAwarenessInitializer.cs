using System;
using System.Diagnostics;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Helpers.DpiAwareness;

internal static class DpiAwarenessInitializer
{
    private static bool _initialized;

    public static void InitializeSystemAware()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        try
        {
            SHCore
                .SetProcessDpiAwareness(SHCore.PROCESS_DPI_AWARENESS.PROCESS_SYSTEM_DPI_AWARE)
                .ThrowIfFailed();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to set system DPI awareness: {ex.Message}");
        }
    }
}
