// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Routing;

/// <summary>RoutingCatalogJson：原神自动配音的业务类型。</summary>
internal static class RoutingCatalogJson
{
    /// <summary>解析并拒绝未知、缺失或重复的配置字段。</summary>
    public static RoutingCatalog Parse(JsonElement root, string directory)
    {
        Object(root, "Catalog", ["Version", "Game", "ProtagonistId", "PlayerNames", "NarratorVoiceId",
            "FallbackVoiceId", "MissingVoicePolicy", "Speakers", "Voices"]);
        var version = Required(root, "Version", "Version");
        if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number))
            throw RoutingPath.Invalid("Version", "Integer required.");
        var policy = MissingVoicePolicy.Fallback;
        if (root.TryGetProperty("MissingVoicePolicy", out _))
        {
            var text = String(root, "MissingVoicePolicy", "MissingVoicePolicy");
            policy = text switch
            {
                "Fallback" => MissingVoicePolicy.Fallback,
                "Fail" => MissingVoicePolicy.Fail,
                _ => throw RoutingPath.Invalid("MissingVoicePolicy", "Expected Fallback or Fail.")
            };
        }
        var speakers = Array(root, "Speakers", "Speakers").Select((item, index) => Speaker(item, $"Speakers[{index}]"));
        var voices = Array(root, "Voices", "Voices").Select((item, index) => Voice(item, directory, $"Voices[{index}]"));
        return new(number, String(root, "Game", "Game"), Optional(root, "ProtagonistId", "ProtagonistId"),
            Strings(root, "PlayerNames", "PlayerNames"), Optional(root, "NarratorVoiceId", "NarratorVoiceId"),
            Optional(root, "FallbackVoiceId", "FallbackVoiceId"), policy, speakers, voices);
    }

    /// <summary>Speaker：执行RoutingCatalogJson中的对应校验或计算步骤。</summary>
    private static SpeakerDefinition Speaker(JsonElement element, string field)
    {
        Object(element, field, ["Id", "Names", "Aliases", "Corrections", "VoiceId"]);
        return new(String(element, "Id", field + ".Id"), Strings(element, "Names", field + ".Names"),
            Strings(element, "Aliases", field + ".Aliases"), Strings(element, "Corrections", field + ".Corrections"),
            Optional(element, "VoiceId", field + ".VoiceId"));
    }

    /// <summary>Voice：执行RoutingCatalogJson中的对应校验或计算步骤。</summary>
    private static RoutingVoice Voice(JsonElement element, string directory, string field)
    {
        Object(element, field, ["Id", "Name", "GptWeightsPath", "SovitsWeightsPath", "ReferenceAudioPath",
            "PromptText", "RequestedEmotion", "ReferenceEmotion"]);
        string PathField(string key) => RoutingPath.Resolve(String(element, key, field + "." + key), directory, field + "." + key);
        return new(String(element, "Id", field + ".Id"), String(element, "Name", field + ".Name"),
            PathField("GptWeightsPath"), PathField("SovitsWeightsPath"), PathField("ReferenceAudioPath"),
            String(element, "PromptText", field + ".PromptText"), Optional(element, "RequestedEmotion", field + ".RequestedEmotion"),
            Optional(element, "ReferenceEmotion", field + ".ReferenceEmotion"));
    }

    /// <summary>Object：执行RoutingCatalogJson中的对应校验或计算步骤。</summary>
    private static void Object(JsonElement element, string field, string[] allowed)
    {
        if (element.ValueKind != JsonValueKind.Object) throw RoutingPath.Invalid(field, "Object required.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name)) throw RoutingPath.Invalid(field + "." + property.Name, "Duplicate JSON field.");
            if (!allowed.Contains(property.Name, StringComparer.Ordinal))
                throw RoutingPath.Invalid(field + "." + property.Name, "Unknown JSON field.");
        }
    }

    /// <summary>Required：执行RoutingCatalogJson中的对应校验或计算步骤。</summary>
    private static JsonElement Required(JsonElement element, string key, string field) =>
        element.TryGetProperty(key, out var value) ? value : throw RoutingPath.Invalid(field, "Required field is missing.");

    /// <summary>String：执行RoutingCatalogJson中的对应校验或计算步骤。</summary>
    private static string String(JsonElement element, string key, string field)
    {
        var value = Required(element, key, field);
        if (value.ValueKind != JsonValueKind.String) throw RoutingPath.Invalid(field, "String required.");
        var text = value.GetString()!;
        RoutingCatalogValidation.Text(text, field);
        return text;
    }

    /// <summary>Optional：执行RoutingCatalogJson中的对应校验或计算步骤。</summary>
    private static string? Optional(JsonElement element, string key, string field) =>
        !element.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null ? null : String(element, key, field);

    /// <summary>Array：执行RoutingCatalogJson中的对应校验或计算步骤。</summary>
    private static JsonElement.ArrayEnumerator Array(JsonElement element, string key, string field)
    {
        var value = Required(element, key, field);
        if (value.ValueKind != JsonValueKind.Array) throw RoutingPath.Invalid(field, "Array required.");
        return value.EnumerateArray();
    }

    /// <summary>Strings：执行RoutingCatalogJson中的对应校验或计算步骤。</summary>
    private static IEnumerable<string> Strings(JsonElement element, string key, string field)
    {
        foreach (var value in Array(element, key, field))
        {
            if (value.ValueKind != JsonValueKind.String) throw RoutingPath.Invalid(field, "Each entry must be a string.");
            var text = value.GetString()!;
            RoutingCatalogValidation.Text(text, field);
            yield return text;
        }
    }
}
