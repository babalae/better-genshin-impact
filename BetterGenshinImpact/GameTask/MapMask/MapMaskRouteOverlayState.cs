using System;
using System.Diagnostics;
using System.Text.Json;
using BetterGenshinImpact.GameTask.AutoPathing;
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
            _currentTask = Clone(task);
        }
    }

    public static PathingTask? Get()
    {
        lock (LockObject)
        {
            return Clone(_currentTask);
        }
    }

    public static bool HasRoute
    {
        get
        {
            lock (LockObject)
            {
                return _currentTask?.Positions is { Count: > 0 };
            }
        }
    }

    private static PathingTask? Clone(PathingTask? task)
    {
        if (task == null)
        {
            return null;
        }

        try
        {
            var json = JsonSerializer.Serialize(task, PathRecorder.JsonOptions);
            return JsonSerializer.Deserialize<PathingTask>(json, PathRecorder.JsonOptions);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return null;
        }
    }
}
