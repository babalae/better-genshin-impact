using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoPathing.Telemetry;

public sealed class RouteHealthStore
{
    private readonly object _syncRoot = new();
    private readonly string _filePath;
    private readonly Dictionary<string, RouteHealthEntry> _entries;
    private int _isSaving;
    private volatile bool _hasPendingSave;

    public RouteHealthStore(string saveDir)
    {
        Directory.CreateDirectory(saveDir);
        _filePath = Path.Combine(saveDir, "route_health.json");
        _entries = LoadEntries(_filePath);
    }

    public void RecordSuccess(RouteSegmentIdentity identity, double routeDistance, TimeSpan duration)
    {
        Update(identity, entry =>
        {
            entry.SuccessCount++;
            entry.LastSuccessUtc = DateTime.UtcNow;
            entry.LastFailureReason = string.Empty;
            entry.AverageDistance = CalculateRunningAverage(entry.AverageDistance, entry.SuccessCount, routeDistance);
            entry.AverageDurationMs = CalculateRunningAverage(entry.AverageDurationMs, entry.SuccessCount, duration.TotalMilliseconds);
        });
    }

    public void RecordFailure(RouteSegmentIdentity identity, string reason)
    {
        Update(identity, entry =>
        {
            entry.FailureCount++;
            entry.LastFailureUtc = DateTime.UtcNow;
            entry.LastFailureReason = reason;
        });
    }

    public IReadOnlyCollection<RouteHealthEntry> GetSnapshot()
    {
        lock (_syncRoot)
        {
            var snapshot = new List<RouteHealthEntry>(_entries.Count);
            foreach (var entry in _entries.Values)
            {
                snapshot.Add(entry.Clone());
            }

            return snapshot;
        }
    }

    private void Update(RouteSegmentIdentity identity, Action<RouteHealthEntry> update)
    {
        lock (_syncRoot)
        {
            if (!_entries.TryGetValue(identity.SegmentId, out var entry))
            {
                entry = RouteHealthEntry.Create(identity);
                _entries[identity.SegmentId] = entry;
            }
            else
            {
                entry.RefreshIdentity(identity);
            }

            update(entry);
            entry.LastSeenUtc = DateTime.UtcNow;
            entry.Status = RouteHealthStatus.FromCounts(entry.SuccessCount, entry.FailureCount);
        }

        ScheduleSave();
    }

    private void ScheduleSave()
    {
        _hasPendingSave = true;
        if (Interlocked.CompareExchange(ref _isSaving, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(SaveLoop);
    }

    private void SaveLoop()
    {
        try
        {
            do
            {
                _hasPendingSave = false;
                SaveEntries();
            }
            while (_hasPendingSave);
        }
        finally
        {
            Interlocked.Exchange(ref _isSaving, 0);
            if (_hasPendingSave && Interlocked.CompareExchange(ref _isSaving, 1, 0) == 0)
            {
                _ = Task.Run(SaveLoop);
            }
        }
    }

    private void SaveEntries()
    {
        try
        {
            List<RouteHealthEntry> snapshot;
            lock (_syncRoot)
            {
                snapshot = new List<RouteHealthEntry>(_entries.Values);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            var tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(snapshot, options));
            File.Copy(tempPath, _filePath, true);
            File.Delete(tempPath);
        }
        catch
        {
            // Route health should never interrupt the active pathing task.
        }
    }

    private static Dictionary<string, RouteHealthEntry> LoadEntries(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return new Dictionary<string, RouteHealthEntry>(StringComparer.OrdinalIgnoreCase);
            }

            var json = File.ReadAllText(filePath);
            var entries = JsonSerializer.Deserialize<List<RouteHealthEntry>>(json) ?? [];
            var result = new Dictionary<string, RouteHealthEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                if (!string.IsNullOrWhiteSpace(entry.SegmentId))
                {
                    result[entry.SegmentId] = entry;
                }
            }

            return result;
        }
        catch
        {
            return new Dictionary<string, RouteHealthEntry>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static double CalculateRunningAverage(double currentAverage, int newCount, double value)
    {
        if (newCount <= 1)
        {
            return value;
        }

        return currentAverage + ((value - currentAverage) / newCount);
    }
}

public sealed class RouteHealthEntry
{
    public string SegmentId { get; set; } = string.Empty;

    public string MapName { get; set; } = string.Empty;

    public string AnchorId { get; set; } = string.Empty;

    public string SegmentKey { get; set; } = string.Empty;

    public string MoveMode { get; set; } = string.Empty;

    public string WaypointType { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string ActionParams { get; set; } = string.Empty;

    public int SuccessCount { get; set; }

    public int FailureCount { get; set; }

    public double AverageDistance { get; set; }

    public double AverageDurationMs { get; set; }

    public DateTime FirstSeenUtc { get; set; }

    public DateTime LastSeenUtc { get; set; }

    public DateTime? LastSuccessUtc { get; set; }

    public DateTime? LastFailureUtc { get; set; }

    public string LastFailureReason { get; set; } = string.Empty;

    public string Status { get; set; } = RouteHealthStatus.Unknown;

    public double SuccessRate => SuccessCount + FailureCount == 0
        ? 0
        : Math.Round((double)SuccessCount / (SuccessCount + FailureCount), 4);

    public static RouteHealthEntry Create(RouteSegmentIdentity identity)
    {
        var now = DateTime.UtcNow;
        var entry = new RouteHealthEntry
        {
            FirstSeenUtc = now,
            LastSeenUtc = now
        };
        entry.RefreshIdentity(identity);
        return entry;
    }

    public void RefreshIdentity(RouteSegmentIdentity identity)
    {
        SegmentId = identity.SegmentId;
        MapName = identity.MapName;
        AnchorId = identity.AnchorId;
        SegmentKey = identity.SegmentKey;
        MoveMode = identity.MoveMode;
        WaypointType = identity.WaypointType;
        Action = identity.Action;
        ActionParams = identity.ActionParams;
    }

    public RouteHealthEntry Clone()
    {
        return new RouteHealthEntry
        {
            SegmentId = SegmentId,
            MapName = MapName,
            AnchorId = AnchorId,
            SegmentKey = SegmentKey,
            MoveMode = MoveMode,
            WaypointType = WaypointType,
            Action = Action,
            ActionParams = ActionParams,
            SuccessCount = SuccessCount,
            FailureCount = FailureCount,
            AverageDistance = AverageDistance,
            AverageDurationMs = AverageDurationMs,
            FirstSeenUtc = FirstSeenUtc,
            LastSeenUtc = LastSeenUtc,
            LastSuccessUtc = LastSuccessUtc,
            LastFailureUtc = LastFailureUtc,
            LastFailureReason = LastFailureReason,
            Status = Status
        };
    }
}

public static class RouteHealthStatus
{
    public const string Unknown = "unknown";
    public const string Verified = "verified";
    public const string Risky = "risky";
    public const string Disabled = "disabled";

    public static string FromCounts(int successCount, int failureCount)
    {
        var total = successCount + failureCount;
        if (total == 0)
        {
            return Unknown;
        }

        var successRate = (double)successCount / total;
        if (successCount >= 3 && successRate >= 0.8)
        {
            return Verified;
        }

        if (failureCount >= 3 && successRate < 0.5)
        {
            return Disabled;
        }

        return Risky;
    }
}
