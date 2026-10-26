// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using System.Text.Json;
namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Continuous;
/// <summary>ContinuousSettingsLoader：原神自动配音的业务类型。</summary>
public sealed class ContinuousSettingsLoader
{
    /// <summary>解析并拒绝未知、缺失或重复的配置字段。</summary>
    public static ContinuousSettings Parse(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            if (bytes.Length > 16384) throw new ArgumentException("Continuous settings exceed 16 KiB.");
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
            var root = json.RootElement;
            var names = typeof(ContinuousSettings).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            if (root.ValueKind != JsonValueKind.Object) throw new ArgumentException("Settings must be an object.");
            foreach (var property in root.EnumerateObject())
                if (!names.Remove(property.Name)) throw new ArgumentException($"Unknown or duplicate setting: {property.Name}");
            if (names.Count != 0) throw new ArgumentException("Missing continuous settings: " + string.Join(", ", names));
            var settings = JsonSerializer.Deserialize<ContinuousSettings>(bytes.Span) ?? throw new ArgumentException("Empty settings.");
            Validate(settings);
            return settings;
        }
        catch (JsonException exception) { throw new ArgumentException("Invalid continuous settings JSON.", exception); }
    }
    /// <summary>校验配置的边界、引用和参数关系。</summary>
    public static void Validate(ContinuousSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.StableMilliseconds is < 1 or > 60000 || settings.MinimumStableFrames is < 2 or > 1000
            || settings.MaximumObservationGapMilliseconds <= settings.StableMilliseconds
            || settings.MaximumObservationGapMilliseconds > 300000
            || settings.LeaveDialogueMilliseconds is < 1 or > 300000
            || settings.LeaveDialogueMilliseconds > settings.MaximumObservationGapMilliseconds
            || settings.RecheckMilliseconds < settings.StableMilliseconds || settings.RecheckMilliseconds > 300000
            || settings.SignatureColumns is < 8 or > 1024 || settings.SignatureRows is < 4 or > 128
            || settings.SignatureColumns * settings.SignatureRows > 65536
            || !double.IsFinite(settings.SignatureDifferenceThreshold)
            || settings.SignatureDifferenceThreshold is <= 0 or > 1)
            throw new ArgumentException("Invalid continuous timing or signature bounds/relationships.", nameof(settings));
    }
}
