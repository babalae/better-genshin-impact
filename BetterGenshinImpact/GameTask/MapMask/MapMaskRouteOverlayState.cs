using BetterGenshinImpact.GameTask.AutoPathing.Model;

namespace BetterGenshinImpact.GameTask.MapMask;

public static class MapMaskRouteOverlayState
{
    private static readonly object LockObject = new();
    private static PathingTask? _currentTask;

    public static void Set(PathingTask? task)
    {
        lock (LockObject)
        {
            _currentTask = task;
        }
    }

    public static PathingTask? Get()
    {
        lock (LockObject)
        {
            return _currentTask;
        }
    }
}
