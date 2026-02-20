using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.Interface;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Service;

public sealed class JsonTranslationService : ITranslationService, IDisposable
{
    private readonly IConfigService _configService;
    private readonly IMissingTranslationReporter _missingTranslationReporter;
    private readonly object _sync = new();
    private readonly ConcurrentDictionary<string, TranslationSourceInfo> _missingKeys = new(StringComparer.Ordinal);
    private readonly Timer _flushTimer;
    private readonly OtherConfig _otherConfig;

    private string _loadedCultureName = string.Empty;
    private IReadOnlyDictionary<string, string> _map = new Dictionary<string, string>(StringComparer.Ordinal);
    private volatile IReadOnlySet<string> _existingMissingKeys = new HashSet<string>(StringComparer.Ordinal);
    private int _dirtyMissing;

    public JsonTranslationService(IConfigService configService, IMissingTranslationReporter missingTranslationReporter)
    {
        _configService = configService;
        _missingTranslationReporter = missingTranslationReporter;
        _otherConfig = _configService.Get().OtherConfig;
        _otherConfig.PropertyChanged += OnOtherConfigPropertyChanged;
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

    public void Reload()
    {
        var cultureName = _otherConfig.UiCultureInfoName ?? string.Empty;
        string previousLoaded;
        string currentLoaded;

        lock (_sync)
        {
            previousLoaded = _loadedCultureName;
            FlushMissingIfDirty(previousLoaded);

            if (string.IsNullOrWhiteSpace(cultureName) || IsChineseCultureName(cultureName))
            {
                _loadedCultureName = string.Empty;
                _map = new Dictionary<string, string>(StringComparer.Ordinal);
                _existingMissingKeys = new HashSet<string>(StringComparer.Ordinal);
            }
            else
            {
                _loadedCultureName = cultureName;
                _map = LoadMap(cultureName);
                _existingMissingKeys = LoadExistingMissingKeys(cultureName);
            }

            _missingKeys.Clear();
            Interlocked.Exchange(ref _dirtyMissing, 0);
            currentLoaded = _loadedCultureName;
        }

        WeakReferenceMessenger.Default.Send(
            new PropertyChangedMessage<object>(this, nameof(OtherConfig.UiCultureInfoName), previousLoaded, currentLoaded));
    }

    public string Translate(string text)
    {
        return Translate(text, TranslationSourceInfo.From(MissingTextSource.Unknown));
    }

    public string Translate(string text, TranslationSourceInfo sourceInfo)
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

        if (string.IsNullOrWhiteSpace(culture.Name))
        {
            return text;
        }

        EnsureMapLoaded(culture.Name);

        if (_map.TryGetValue(text, out var translated) && !string.IsNullOrWhiteSpace(translated))
        {
            return translated;
        }

        var normalizedSource = NormalizeSourceInfo(sourceInfo);
        _missingKeys.AddOrUpdate(
            text,
            normalizedSource,
            (_, existingSource) => MergeSourceInfo(existingSource, normalizedSource));
        Interlocked.Exchange(ref _dirtyMissing, 1);
        if (!_existingMissingKeys.Contains(text))
        {
            _missingTranslationReporter.TryEnqueue(culture.Name, text, normalizedSource);
        }
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

            var previousCultureName = _loadedCultureName;
            FlushMissingIfDirty(previousCultureName);
            _map = LoadMap(cultureName);
            _loadedCultureName = cultureName;
            _existingMissingKeys = LoadExistingMissingKeys(cultureName);
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
        catch (Exception e)
        {
            Debug.WriteLine(e);
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private void FlushMissingIfDirty()
    {
        FlushMissingIfDirty(_loadedCultureName);
    }

    private void FlushMissingIfDirty(string cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
        {
            Interlocked.Exchange(ref _dirtyMissing, 0);
            return;
        }

        if (IsChineseCultureName(cultureName))
        {
            Interlocked.Exchange(ref _dirtyMissing, 0);
            return;
        }

        if (Interlocked.Exchange(ref _dirtyMissing, 0) == 0)
        {
            return;
        }

        try
        {
            FlushMissing(cultureName);
        }
        catch
        {
            Interlocked.Exchange(ref _dirtyMissing, 1);
        }
    }

    private void FlushMissing(string cultureName)
    {
        var missingSnapshot = _missingKeys.ToArray();
        if (missingSnapshot.Length == 0)
        {
            return;
        }

        var filePath = GetMissingFilePath(cultureName);
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
            var sourceInfo = pair.Value;
            var source = SourceToCompactString(sourceInfo.Source);
            var missingSourceInfo = StripSource(sourceInfo);

            if (!existing.TryGetValue(key, out var existingItem))
            {
                existing[key] = new MissingItem
                {
                    Key = key,
                    Value = string.Empty,
                    Source = source,
                    SourceInfo = missingSourceInfo
                };
                updated = true;
                continue;
            }

            if (string.IsNullOrWhiteSpace(existingItem.Source) || string.Equals(existingItem.Source, SourceToCompactString(MissingTextSource.Unknown), StringComparison.Ordinal))
            {
                existing[key] = new MissingItem
                {
                    Key = key,
                    Value = existingItem.Value ?? string.Empty,
                    Source = source,
                    SourceInfo = missingSourceInfo
                };
                updated = true;
                continue;
            }

            var mergedSourceInfo = MergeSourceInfo(existingItem.SourceInfo, missingSourceInfo);
            if (!ReferenceEquals(mergedSourceInfo, existingItem.SourceInfo))
            {
                existing[key] = new MissingItem
                {
                    Key = key,
                    Value = existingItem.Value ?? string.Empty,
                    Source = existingItem.Source ?? source,
                    SourceInfo = mergedSourceInfo
                };
                updated = true;
            }
        }

        if (!updated)
        {
            return;
        }

        var items = existing.Values
            .OrderBy(i => i.Key, StringComparer.Ordinal)
            .Select(i => new MissingItem
            {
                Key = i.Key,
                Value = i.Value ?? string.Empty,
                Source = string.IsNullOrWhiteSpace(i.Source) ? SourceToCompactString(MissingTextSource.Unknown) : i.Source,
                SourceInfo = NormalizeSourceInfoForMissing(i.SourceInfo)
            })
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

            var normalized = new MissingItem
            {
                Key = item.Key,
                Value = item.Value ?? string.Empty,
                Source = string.IsNullOrWhiteSpace(item.Source) ? SourceToCompactString(MissingTextSource.Unknown) : item.Source,
                SourceInfo = NormalizeSourceInfoForMissing(item.SourceInfo)
            };
            dict[item.Key] = normalized;
        }

        return dict;
    }

    private static IReadOnlySet<string> LoadExistingMissingKeys(string cultureName)
    {
        var filePath = GetMissingFilePath(cultureName);
        if (!File.Exists(filePath))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        try
        {
            return new HashSet<string>(LoadMissing(filePath).Keys, StringComparer.Ordinal);
        }
        catch
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private static TranslationSourceInfo NormalizeSourceInfo(TranslationSourceInfo? sourceInfo)
    {
        if (sourceInfo == null)
        {
            return TranslationSourceInfo.From(MissingTextSource.Unknown);
        }

        return new TranslationSourceInfo
        {
            Source = sourceInfo.Source,
            ViewXamlPath = sourceInfo.ViewXamlPath,
            ViewType = sourceInfo.ViewType,
            ElementType = sourceInfo.ElementType,
            ElementName = sourceInfo.ElementName,
            PropertyName = sourceInfo.PropertyName,
            BindingPath = sourceInfo.BindingPath,
            Notes = sourceInfo.Notes
        };
    }

    private static TranslationSourceInfo NormalizeSourceInfoForMissing(TranslationSourceInfo? sourceInfo)
    {
        return StripSource(NormalizeSourceInfo(sourceInfo));
    }

    private static TranslationSourceInfo StripSource(TranslationSourceInfo sourceInfo)
    {
        return new TranslationSourceInfo
        {
            Source = MissingTextSource.Unknown,
            ViewXamlPath = sourceInfo.ViewXamlPath,
            ViewType = sourceInfo.ViewType,
            ElementType = sourceInfo.ElementType,
            ElementName = sourceInfo.ElementName,
            PropertyName = sourceInfo.PropertyName,
            BindingPath = sourceInfo.BindingPath,
            Notes = sourceInfo.Notes
        };
    }

    private static TranslationSourceInfo MergeSourceInfo(TranslationSourceInfo? existing, TranslationSourceInfo? incoming)
    {
        if (incoming == null)
        {
            return NormalizeSourceInfo(existing);
        }

        if (existing == null)
        {
            return NormalizeSourceInfo(incoming);
        }

        if (existing.Source == MissingTextSource.Unknown && incoming.Source != MissingTextSource.Unknown)
        {
            return incoming;
        }

        if (existing.Source != MissingTextSource.Unknown && incoming.Source == MissingTextSource.Unknown)
        {
            return existing;
        }

        if (existing.Source != incoming.Source)
        {
            return existing;
        }

        var merged = new TranslationSourceInfo
        {
            Source = existing.Source,
            ViewXamlPath = string.IsNullOrWhiteSpace(existing.ViewXamlPath) ? incoming.ViewXamlPath : existing.ViewXamlPath,
            ViewType = string.IsNullOrWhiteSpace(existing.ViewType) ? incoming.ViewType : existing.ViewType,
            ElementType = string.IsNullOrWhiteSpace(existing.ElementType) ? incoming.ElementType : existing.ElementType,
            ElementName = string.IsNullOrWhiteSpace(existing.ElementName) ? incoming.ElementName : existing.ElementName,
            PropertyName = string.IsNullOrWhiteSpace(existing.PropertyName) ? incoming.PropertyName : existing.PropertyName,
            BindingPath = string.IsNullOrWhiteSpace(existing.BindingPath) ? incoming.BindingPath : existing.BindingPath,
            Notes = string.IsNullOrWhiteSpace(existing.Notes) ? incoming.Notes : existing.Notes
        };

        if (IsSameSourceInfo(merged, existing))
        {
            return existing;
        }

        return merged;
    }

    private static bool IsSameSourceInfo(TranslationSourceInfo left, TranslationSourceInfo right)
    {
        return left.Source == right.Source
               && string.Equals(left.ViewXamlPath, right.ViewXamlPath, StringComparison.Ordinal)
               && string.Equals(left.ViewType, right.ViewType, StringComparison.Ordinal)
               && string.Equals(left.ElementType, right.ElementType, StringComparison.Ordinal)
               && string.Equals(left.ElementName, right.ElementName, StringComparison.Ordinal)
               && string.Equals(left.PropertyName, right.PropertyName, StringComparison.Ordinal)
               && string.Equals(left.BindingPath, right.BindingPath, StringComparison.Ordinal)
               && string.Equals(left.Notes, right.Notes, StringComparison.Ordinal);
    }

    private static string SourceToCompactString(MissingTextSource source)
    {
        return ((int)source).ToString(CultureInfo.InvariantCulture);
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

    private static bool IsChineseCultureName(string cultureName)
    {
        return cultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
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
        return Global.Absolute(@"User\I18n");
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
        _otherConfig.PropertyChanged -= OnOtherConfigPropertyChanged;
        FlushMissingIfDirty();
    }

    private sealed class MissingItem
    {
        public string Key { get; set; } = string.Empty;
        public string? Value { get; set; }
        [JsonConverter(typeof(MissingSourceStringConverter))]
        public string? Source { get; set; }
        [JsonConverter(typeof(MissingSourceInfoWithoutSourceConverter))]
        public TranslationSourceInfo? SourceInfo { get; set; }
    }

    private sealed class MissingSourceStringConverter : JsonConverter<string?>
    {
        public override void WriteJson(JsonWriter writer, string? value, JsonSerializer serializer)
        {
            if (string.IsNullOrEmpty(value))
            {
                writer.WriteNull();
                return;
            }

            writer.WriteValue(value);
        }

        public override string? ReadJson(JsonReader reader, Type objectType, string? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return null;
            }

            if (reader.TokenType == JsonToken.Integer)
            {
                try
                {
                    return Convert.ToInt32(reader.Value).ToString(CultureInfo.InvariantCulture);
                }
                catch
                {
                    return SourceToCompactString(MissingTextSource.Unknown);
                }
            }

            if (reader.TokenType == JsonToken.String)
            {
                var s = reader.Value as string;
                if (string.IsNullOrWhiteSpace(s))
                {
                    return SourceToCompactString(MissingTextSource.Unknown);
                }

                if (int.TryParse(s, out var parsed))
                {
                    return parsed.ToString(CultureInfo.InvariantCulture);
                }

                return s switch
                {
                    "Log" => SourceToCompactString(MissingTextSource.Log),
                    "UiStaticLiteral" => SourceToCompactString(MissingTextSource.UiStaticLiteral),
                    "UiDynamicBinding" => SourceToCompactString(MissingTextSource.UiDynamicBinding),
                    _ => SourceToCompactString(MissingTextSource.Unknown)
                };
            }

            return SourceToCompactString(MissingTextSource.Unknown);
        }
    }

    private sealed class MissingSourceInfoWithoutSourceConverter : JsonConverter<TranslationSourceInfo?>
    {
        public override void WriteJson(JsonWriter writer, TranslationSourceInfo? value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            writer.WriteStartObject();
            WriteIfNotNull(writer, "ViewXamlPath", value.ViewXamlPath);
            WriteIfNotNull(writer, "ViewType", value.ViewType);
            WriteIfNotNull(writer, "ElementType", value.ElementType);
            WriteIfNotNull(writer, "ElementName", value.ElementName);
            WriteIfNotNull(writer, "PropertyName", value.PropertyName);
            WriteIfNotNull(writer, "BindingPath", value.BindingPath);
            WriteIfNotNull(writer, "Notes", value.Notes);
            writer.WriteEndObject();
        }

        public override TranslationSourceInfo? ReadJson(JsonReader reader, Type objectType, TranslationSourceInfo? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return null;
            }

            var obj = JObject.Load(reader);
            return new TranslationSourceInfo
            {
                Source = MissingTextSource.Unknown,
                ViewXamlPath = obj.Value<string>("ViewXamlPath") ?? obj.Value<string>("viewXamlPath"),
                ViewType = obj.Value<string>("ViewType") ?? obj.Value<string>("viewType"),
                ElementType = obj.Value<string>("ElementType") ?? obj.Value<string>("elementType"),
                ElementName = obj.Value<string>("ElementName") ?? obj.Value<string>("elementName"),
                PropertyName = obj.Value<string>("PropertyName") ?? obj.Value<string>("propertyName"),
                BindingPath = obj.Value<string>("BindingPath") ?? obj.Value<string>("bindingPath"),
                Notes = obj.Value<string>("Notes") ?? obj.Value<string>("notes")
            };
        }

        private static void WriteIfNotNull(JsonWriter writer, string propertyName, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            writer.WritePropertyName(propertyName);
            writer.WriteValue(value);
        }
    }

    private void OnOtherConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OtherConfig.UiCultureInfoName))
        {
            return;
        }

        var cultureName = _otherConfig.UiCultureInfoName ?? string.Empty;
        string previousLoaded;
        string currentLoaded;

        lock (_sync)
        {
            previousLoaded = _loadedCultureName;
            FlushMissingIfDirty(previousLoaded);

            if (string.IsNullOrWhiteSpace(cultureName) || IsChineseCultureName(cultureName))
            {
                _loadedCultureName = string.Empty;
                _map = new Dictionary<string, string>(StringComparer.Ordinal);
                _existingMissingKeys = new HashSet<string>(StringComparer.Ordinal);
            }
            else
            {
                _loadedCultureName = cultureName;
                _map = LoadMap(cultureName);
                _existingMissingKeys = LoadExistingMissingKeys(cultureName);
            }

            _missingKeys.Clear();
            Interlocked.Exchange(ref _dirtyMissing, 0);
            currentLoaded = _loadedCultureName;
        }

        if (!string.Equals(previousLoaded, currentLoaded, StringComparison.OrdinalIgnoreCase))
        {
            WeakReferenceMessenger.Default.Send(
                new PropertyChangedMessage<object>(this, nameof(OtherConfig.UiCultureInfoName), previousLoaded, currentLoaded));
        }
    }
}
