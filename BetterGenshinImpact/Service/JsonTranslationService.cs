using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using BetterGenshinImpact.Service.Interface;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service;

public sealed class JsonTranslationService : ITranslationService, IDisposable
{
    private readonly IConfigService _configService;
    private readonly object _sync = new();
    private readonly ConcurrentDictionary<string, MissingTextSource> _missingKeys = new(StringComparer.Ordinal);
    private readonly Timer _flushTimer;

    private string _loadedCultureName = string.Empty;
    private IReadOnlyDictionary<string, string> _map = new Dictionary<string, string>(StringComparer.Ordinal);
    private int _dirtyMissing;

    public JsonTranslationService(IConfigService configService)
    {
        _configService = configService;
        _flushTimer = new Timer(_ => FlushMissingIfDirty(), null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
    }

    public CultureInfo GetCurrentCulture()
    {
        var name = _configService.Get().OtherConfig.UiCultureInfoName;
        if (string.IsNullOrWhiteSpace(name))
        {
            return CultureInfo.InvariantCulture;
        }

        try
        {
            return new CultureInfo(name);
        }
        catch
        {
            return CultureInfo.InvariantCulture;
        }
    }

    public string Translate(string text)
    {
        return Translate(text, MissingTextSource.Unknown);
    }

    public string Translate(string text, MissingTextSource source)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        if (!ContainsCjk(text))
        {
            return text;
        }

        var culture = GetCurrentCulture();
        if (IsChineseCulture(culture))
        {
            return text;
        }

        EnsureMapLoaded(culture.Name);

        if (_map.TryGetValue(text, out var translated) && !string.IsNullOrWhiteSpace(translated))
        {
            return translated;
        }

        _missingKeys.AddOrUpdate(
            text,
            source,
            (_, existingSource) => existingSource == MissingTextSource.Unknown ? source : existingSource);
        Interlocked.Exchange(ref _dirtyMissing, 1);
        return text;
    }

    private void EnsureMapLoaded(string cultureName)
    {
        if (string.Equals(_loadedCultureName, cultureName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (_sync)
        {
            if (string.Equals(_loadedCultureName, cultureName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            FlushMissingIfDirty();
            _map = LoadMap(cultureName);
            _loadedCultureName = cultureName;
            _missingKeys.Clear();
            Interlocked.Exchange(ref _dirtyMissing, 0);
        }
    }

    private IReadOnlyDictionary<string, string> LoadMap(string cultureName)
    {
        var path = GetMapFilePath(cultureName);
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            var dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
            return new Dictionary<string, string>(dict, StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private void FlushMissingIfDirty()
    {
        if (Interlocked.Exchange(ref _dirtyMissing, 0) == 0)
        {
            return;
        }

        try
        {
            FlushMissing();
        }
        catch
        {
            Interlocked.Exchange(ref _dirtyMissing, 1);
        }
    }

    private void FlushMissing()
    {
        var culture = GetCurrentCulture();
        if (IsChineseCulture(culture))
        {
            return;
        }

        var missingSnapshot = _missingKeys.ToArray();
        if (missingSnapshot.Length == 0)
        {
            return;
        }

        var filePath = GetMissingFilePath(culture.Name);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        Dictionary<string, MissingItem> existing;
        try
        {
            existing = File.Exists(filePath) ? LoadMissing(filePath) : new Dictionary<string, MissingItem>(StringComparer.Ordinal);
        }
        catch
        {
            existing = new Dictionary<string, MissingItem>(StringComparer.Ordinal);
        }

        var updated = false;
        foreach (var pair in missingSnapshot)
        {
            var key = pair.Key;
            var source = pair.Value;

            if (!existing.TryGetValue(key, out var existingItem))
            {
                existing[key] = new MissingItem(key, string.Empty, SourceToString(source));
                updated = true;
                continue;
            }

            if (string.IsNullOrWhiteSpace(existingItem.Source) || string.Equals(existingItem.Source, SourceToString(MissingTextSource.Unknown), StringComparison.Ordinal))
            {
                existing[key] = new MissingItem(key, existingItem.Value ?? string.Empty, SourceToString(source));
                updated = true;
            }
        }

        if (!updated)
        {
            return;
        }

        var items = existing.Values
            .OrderBy(i => i.Key, StringComparer.Ordinal)
            .Select(i => new MissingItem(i.Key, i.Value ?? string.Empty, i.Source ?? SourceToString(MissingTextSource.Unknown)))
            .ToList();
        var jsonOut = JsonConvert.SerializeObject(items, Formatting.Indented);
        WriteAtomically(filePath, jsonOut);
    }

    private static Dictionary<string, MissingItem> LoadMissing(string filePath)
    {
        var json = File.ReadAllText(filePath, Encoding.UTF8);
        var list = JsonConvert.DeserializeObject<List<MissingItem>>(json) ?? [];

        var dict = new Dictionary<string, MissingItem>(StringComparer.Ordinal);
        foreach (var item in list)
        {
            if (string.IsNullOrWhiteSpace(item.Key))
            {
                continue;
            }

            var normalized = new MissingItem(
                item.Key,
                item.Value ?? string.Empty,
                string.IsNullOrWhiteSpace(item.Source) ? SourceToString(MissingTextSource.Unknown) : item.Source);
            dict[item.Key] = normalized;
        }

        return dict;
    }

    private static string SourceToString(MissingTextSource source)
    {
        return source switch
        {
            MissingTextSource.Log => "Log",
            MissingTextSource.UiStaticLiteral => "UiStaticLiteral",
            MissingTextSource.UiDynamicBinding => "UiDynamicBinding",
            _ => "Unknown"
        };
    }

    private static void WriteAtomically(string filePath, string content)
    {
        var directory = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(directory);
        var tmp = Path.Combine(directory, $"{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tmp, content, Encoding.UTF8);

        if (File.Exists(filePath))
        {
            File.Replace(tmp, filePath, null);
        }
        else
        {
            File.Move(tmp, filePath);
        }
    }

    private static bool IsChineseCulture(CultureInfo culture)
    {
        if (culture == CultureInfo.InvariantCulture)
        {
            return false;
        }

        var name = culture.Name;
        return name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsCjk(string text)
    {
        foreach (var ch in text)
        {
            if ((ch >= 0x4E00 && ch <= 0x9FFF) || (ch >= 0x3400 && ch <= 0x4DBF))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetI18nDirectory()
    {
        return Path.Combine(AppContext.BaseDirectory, "i18n");
    }

    private static string GetMapFilePath(string cultureName)
    {
        return Path.Combine(GetI18nDirectory(), $"{cultureName}.json");
    }

    private static string GetMissingFilePath(string cultureName)
    {
        return Path.Combine(GetI18nDirectory(), $"missing.{cultureName}.json");
    }

    public void Dispose()
    {
        _flushTimer.Dispose();
        FlushMissingIfDirty();
    }

    private sealed record MissingItem(string Key, string Value, string Source);
}
