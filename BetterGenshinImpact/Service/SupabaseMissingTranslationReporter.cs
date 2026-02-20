using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BetterGenshinImpact.Helpers.Http;
using BetterGenshinImpact.Service.Interface;

namespace BetterGenshinImpact.Service;

public sealed class SupabaseMissingTranslationReporter : IMissingTranslationReporter, IDisposable
{
    private readonly Channel<MissingTranslationEvent> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;

    public SupabaseMissingTranslationReporter()
    {
        _channel = Channel.CreateBounded<MissingTranslationEvent>(
            new BoundedChannelOptions(10_000)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropWrite
            });

        _worker = Task.Run(() => WorkerAsync(_cts.Token), _cts.Token);
    }

    public bool TryEnqueue(string language, string key, TranslationSourceInfo sourceInfo)
    {
        if (!MissingTranslationCollectionSettings.IsValid)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(language) || string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        return _channel.Writer.TryWrite(
            new MissingTranslationEvent(
                language,
                key,
                SourceToCompactString(sourceInfo.Source),
                NormalizeSourceInfoForMissing(sourceInfo)));
    }

    private async Task WorkerAsync(CancellationToken token)
    {
        var pending = new Dictionary<string, MissingTranslationUpsertRow>(StringComparer.Ordinal);

        using var timer = new PeriodicTimer(MissingTranslationCollectionSettings.FlushInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                while (_channel.Reader.TryRead(out var ev))
                {
                    var k = MakeKey(ev.Language, ev.Key);
                    if (!pending.TryGetValue(k, out var existing))
                    {
                        pending[k] = new MissingTranslationUpsertRow(ev.Language, ev.Key, ev.Source, ev.SourceInfo);
                        continue;
                    }

                    pending[k] = existing.Merge(ev.Source, ev.SourceInfo);
                }

                if (!MissingTranslationCollectionSettings.IsValid)
                {
                    pending.Clear();
                    continue;
                }

                if (pending.Count > 0)
                {
                    await FlushAsync(pending, token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private async Task FlushAsync(Dictionary<string, MissingTranslationUpsertRow> pending, CancellationToken token)
    {
        while (pending.Count > 0 && MissingTranslationCollectionSettings.IsValid && !token.IsCancellationRequested)
        {
            var batch = pending.Values.Take(MissingTranslationCollectionSettings.BatchSize).ToList();
            if (batch.Count == 0)
            {
                return;
            }

            foreach (var row in batch)
            {
                pending.Remove(MakeKey(row.Language, row.Key));
            }

            var ok = await TryUpsertBatchAsync(batch, token).ConfigureAwait(false);
            if (!ok)
            {
                foreach (var row in batch)
                {
                    pending[MakeKey(row.Language, row.Key)] = row;
                }

                return;
            }
        }
    }

    private async Task<bool> TryUpsertBatchAsync(IReadOnlyList<MissingTranslationUpsertRow> batch, CancellationToken token)
    {
        try
        {
            if (!MissingTranslationCollectionSettings.IsValid)
            {
                return false;
            }

            var client = HttpClientFactory.GetClient(
                "SupabaseMissingTranslation",
                () =>
                {
                    var http = new HttpClient
                    {
                        Timeout = TimeSpan.FromSeconds(10)
                    };
                    http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    return http;
                });

            var url = $"{MissingTranslationCollectionSettings.SupabaseUrl.TrimEnd('/')}/rest/v1/{MissingTranslationCollectionSettings.Table}";
            var requestUri = $"{url}?on_conflict=language,key";

            var payload = JsonSerializer.Serialize(
                batch.Select(r => new SupabaseMissingRowSnake(r.Language, r.Key, r.Source, r.SourceInfo)),
                SupabaseJsonOptions);

            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };

            request.Headers.TryAddWithoutValidation("apikey", MissingTranslationCollectionSettings.SupabaseApiKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MissingTranslationCollectionSettings.SupabaseApiKey);
            request.Headers.TryAddWithoutValidation("Prefer", "resolution=merge-duplicates,return=minimal");

            using var response = await client.SendAsync(request, token).ConfigureAwait(false);
            var responseText = string.Empty;
            try
            {
                responseText = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            }
            catch
            {
            }

            Debug.WriteLine(
                $"[MissingTranslation][Supabase] status={(int)response.StatusCode} {response.StatusCode}, batch={batch.Count}, table={MissingTranslationCollectionSettings.Table}, body={TruncateForLog(responseText, 2000)}");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _channel.Writer.TryComplete();
        _cts.Cancel();
        try
        {
            _worker.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
        }
        _cts.Dispose();
    }

    private static string MakeKey(string language, string key)
    {
        return $"{language}\u001F{key}";
    }

    private static string TruncateForLog(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || maxLength <= 0)
        {
            return string.Empty;
        }

        if (text.Length <= maxLength)
        {
            return text;
        }

        return text.Substring(0, maxLength) + "...(truncated)";
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

    private static readonly JsonSerializerOptions SupabaseJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly record struct MissingTranslationEvent(
        string Language,
        string Key,
        string Source,
        TranslationSourceInfo SourceInfo);

    private sealed record MissingTranslationUpsertRow(string Language, string Key, string Source, TranslationSourceInfo SourceInfo)
    {
        public MissingTranslationUpsertRow Merge(string source, TranslationSourceInfo sourceInfo)
        {
            var mergedSource = string.Equals(Source, SourceToCompactString(MissingTextSource.Unknown), StringComparison.Ordinal) ? source : Source;
            var mergedSourceInfo = MergeSourceInfo(SourceInfo, sourceInfo);
            return new MissingTranslationUpsertRow(Language, Key, mergedSource, mergedSourceInfo);
        }
    }

    private readonly record struct SupabaseMissingRowSnake(
        [property: JsonPropertyName("language")] string Language,
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("source_info"), JsonConverter(typeof(TranslationSourceInfoWithoutSourceJsonConverter))] TranslationSourceInfo SourceInfo);

    private sealed class TranslationSourceInfoWithoutSourceJsonConverter : JsonConverter<TranslationSourceInfo>
    {
        public override TranslationSourceInfo Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return TranslationSourceInfo.From(MissingTextSource.Unknown);
            }

            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            return new TranslationSourceInfo
            {
                Source = MissingTextSource.Unknown,
                ViewXamlPath = root.TryGetProperty("ViewXamlPath", out var p1) ? p1.GetString() : null,
                ViewType = root.TryGetProperty("ViewType", out var p2) ? p2.GetString() : null,
                ElementType = root.TryGetProperty("ElementType", out var p3) ? p3.GetString() : null,
                ElementName = root.TryGetProperty("ElementName", out var p4) ? p4.GetString() : null,
                PropertyName = root.TryGetProperty("PropertyName", out var p5) ? p5.GetString() : null,
                BindingPath = root.TryGetProperty("BindingPath", out var p6) ? p6.GetString() : null,
                Notes = root.TryGetProperty("Notes", out var p7) ? p7.GetString() : null
            };
        }

        public override void Write(Utf8JsonWriter writer, TranslationSourceInfo value, JsonSerializerOptions options)
        {
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

        private static void WriteIfNotNull(Utf8JsonWriter writer, string propertyName, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            writer.WriteString(propertyName, value);
        }
    }

    private static string SourceToCompactString(MissingTextSource source)
    {
        return ((int)source).ToString(CultureInfo.InvariantCulture);
    }
}
