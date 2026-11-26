using System.Windows;

namespace BetterGenshinImpact.Helpers.DpiAwareness;

internal static class DpiAwarenessExtension
{
    public static void InitializeDpiAwareness(this Window window, bool applySuggestedBounds = true)
    {
        _ = new DpiAwarenessController(window, applySuggestedBounds);
    }
}
