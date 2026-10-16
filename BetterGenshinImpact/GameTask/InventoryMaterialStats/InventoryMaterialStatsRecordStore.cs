using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Helpers;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace BetterGenshinImpact.GameTask.InventoryMaterialStats;

/// <summary>
/// 每次扫描追加一条 json；隔日做差按 serverDate 取昨日最后一条。
/// </summary>
public static class InventoryMaterialStatsRecordStore
{
    public static string RootDirectory => Global.Absolute(@"log\InventoryMaterialStats");

    public static string GetCopyDirectory(string scriptFolderName)
    {
        return Path.Combine(RootDirectory, SanitizeFolderName(scriptFolderName));
    }

    public static InventoryMaterialStatsRecord Save(
        string scriptFolderName,
        IReadOnlyDictionary<string, int> counts,
        InventoryMaterialStatsRecord? baseline)
    {
        var now = ServerTimeHelper.GetServerTimeNow();
        var record = new InventoryMaterialStatsRecord
        {
            ServerDate = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            RecordedAt = now,
            ScriptFolderName = scriptFolderName,
            Counts = new Dictionary<string, int>(counts),
        };

        if (baseline != null)
        {
            var (gained, lost) = ComputeDelta(counts, baseline.Counts);
            record.BaselineServerDate = baseline.ServerDate;
            record.BaselineRecordedAt = baseline.RecordedAt;
            record.BaselineIsYesterday = baseline.ServerDate == now.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            record.Gained = gained;
            record.Lost = lost;
        }

        var dir = GetCopyDirectory(scriptFolderName);
        Directory.CreateDirectory(dir);
        var fileName = now.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture) + ".json";
        var path = Path.Combine(dir, fileName);
        if (File.Exists(path))
        {
            path = Path.Combine(dir, now.ToString("yyyy-MM-dd_HHmmss_fff", CultureInfo.InvariantCulture) + ".json");
        }

        File.WriteAllText(path, JsonConvert.SerializeObject(record, Formatting.Indented));
        return record;
    }

    /// <summary>
    /// 按记录时间从新到旧读取某拷贝下的全部快照。
    /// </summary>
    public static List<InventoryMaterialStatsRecord> LoadRecords(string scriptFolderName)
    {
        var dir = GetCopyDirectory(scriptFolderName);
        if (!Directory.Exists(dir))
        {
            return [];
        }

        var records = new List<InventoryMaterialStatsRecord>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var json = File.ReadAllText(file);
                var record = JsonConvert.DeserializeObject<InventoryMaterialStatsRecord>(json);
                if (record?.Counts == null || string.IsNullOrEmpty(record.ServerDate))
                {
                    continue;
                }

                records.Add(record);
            }
            catch
            {
                // 损坏文件跳过
            }
        }

        return records.OrderByDescending(r => r.RecordedAt).ToList();
    }

    /// <summary>
    /// 日志分析用：至少有 2 条记录的拷贝（只有 1 条时没有前一次，不展示）。
    /// </summary>
    public static List<(string FolderName, List<InventoryMaterialStatsRecord> Records)> ListCopiesWithHistory()
    {
        if (!Directory.Exists(RootDirectory))
        {
            return [];
        }

        var result = new List<(string FolderName, List<InventoryMaterialStatsRecord> Records)>();
        foreach (var dir in Directory.EnumerateDirectories(RootDirectory))
        {
            var folderName = Path.GetFileName(dir);
            var records = LoadRecords(folderName);
            if (records.Count >= 2)
            {
                result.Add((folderName, records));
            }
        }

        return result.OrderBy(x => x.FolderName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static InventoryMaterialStatsRecord? FindBaseline(string scriptFolderName)
    {
        var records = LoadRecords(scriptFolderName);
        if (records.Count == 0)
        {
            return null;
        }

        var today = ServerTimeHelper.GetServerTimeNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var yesterday = ServerTimeHelper.GetServerTimeNow().AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var yesterdayBest = records
            .Where(r => r.ServerDate == yesterday)
            .OrderByDescending(r => r.RecordedAt)
            .FirstOrDefault();
        if (yesterdayBest != null)
        {
            return yesterdayBest;
        }

        return records
            .Where(r => string.CompareOrdinal(r.ServerDate, today) < 0)
            .OrderByDescending(r => r.RecordedAt)
            .FirstOrDefault();
    }

    public static (Dictionary<string, int> Gained, Dictionary<string, int> Lost) ComputeDelta(
        IReadOnlyDictionary<string, int> today,
        IReadOnlyDictionary<string, int>? baseline)
    {
        var gained = new Dictionary<string, int>();
        var lost = new Dictionary<string, int>();
        baseline ??= new Dictionary<string, int>();

        var names = today.Keys.Union(baseline.Keys);
        foreach (var name in names)
        {
            today.TryGetValue(name, out var t);
            baseline.TryGetValue(name, out var b);
            if (t < 0 || b < 0)
            {
                continue;
            }

            var diff = t - b;
            if (diff > 0)
            {
                gained[name] = diff;
            }
            else if (diff < 0)
            {
                lost[name] = -diff;
            }
        }

        return (gained, lost);
    }

    private static string SanitizeFolderName(string folderName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = folderName.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var name = new string(chars).Trim();
        return string.IsNullOrEmpty(name) ? "default" : name;
    }
}

/// <summary>
/// 单次扫描落盘记录。
/// </summary>
public class InventoryMaterialStatsRecord
{
    public string ServerDate { get; set; } = string.Empty;

    public DateTimeOffset RecordedAt { get; set; }

    public string ScriptFolderName { get; set; } = string.Empty;

    public Dictionary<string, int> Counts { get; set; } = new();

    public string? BaselineServerDate { get; set; }

    public DateTimeOffset? BaselineRecordedAt { get; set; }

    public bool BaselineIsYesterday { get; set; }

    public Dictionary<string, int> Gained { get; set; } = new();

    public Dictionary<string, int> Lost { get; set; } = new();
}
