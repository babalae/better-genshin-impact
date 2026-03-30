using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service;

public class CollectedPointsService : ICollectedPointsService
{
    private static readonly string FilePath = Global.Absolute(@"User\collected_points.json");

    private readonly ILogger<CollectedPointsService> _logger;
    private readonly HashSet<string> _collected;
    private readonly object _lock = new();

    public IReadOnlySet<string> CollectedIds
    {
        get
        {
            lock (_lock)
            {
                return new HashSet<string>(_collected);
            }
        }
    }

    public CollectedPointsService(ILogger<CollectedPointsService> logger)
    {
        _logger = logger;
        _collected = Load();
    }

    public bool IsCollected(string pointId)
    {
        lock (_lock)
        {
            return _collected.Contains(pointId);
        }
    }

    public bool Toggle(string pointId)
    {
        lock (_lock)
        {
            if (!_collected.Remove(pointId))
            {
                _collected.Add(pointId);
                Save();
                return true;
            }

            Save();
            return false;
        }
    }

    public void Save()
    {
        try
        {
            HashSet<string> snapshot;
            lock (_lock)
            {
                snapshot = new HashSet<string>(_collected);
            }

            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save collected points");
        }
    }

    private HashSet<string> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new HashSet<string>();
            }

            var json = File.ReadAllText(FilePath);
            var set = JsonSerializer.Deserialize<HashSet<string>>(json);
            return set ?? new HashSet<string>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load collected points, starting fresh");
            return new HashSet<string>();
        }
    }
}
