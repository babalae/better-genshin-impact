// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Routing;

/// <summary>RoutingCatalogValidation：原神自动配音的业务类型。</summary>
internal static class RoutingCatalogValidation
{
    /// <summary>执行规范化与边缘空白处理，不作模糊角色猜测。</summary>
    public static string Normalize(string value, string field)
    {
        try { return value.Trim().Normalize(NormalizationForm.FormC); }
        catch (ArgumentException) { throw RoutingPath.Invalid(field, "Invalid Unicode text."); }
    }

    /// <summary>Same：执行RoutingCatalogValidation中的对应校验或计算步骤。</summary>
    public static bool Same(string left, string right) =>
        StringComparer.OrdinalIgnoreCase.Equals(Normalize(left, "Name"), Normalize(right, "Name"));

    /// <summary>Text：执行RoutingCatalogValidation中的对应校验或计算步骤。</summary>
    public static string Text(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) throw RoutingPath.Invalid(field, "Nonempty text is required.");
        return Normalize(value, field);
    }

    /// <summary>校验配置的边界、引用和参数关系。</summary>
    public static void Validate(RoutingCatalog catalog)
    {
        if (catalog.Version != 1) throw RoutingPath.Invalid("Version", "Expected version 1.");
        if (catalog.Game != "genshin") throw RoutingPath.Invalid("Game", "Expected genshin.");
        if (!Enum.IsDefined(catalog.MissingVoicePolicy))
            throw RoutingPath.Invalid("MissingVoicePolicy", "Expected Fallback or Fail.");
        var speakerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var voiceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var players = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in catalog.PlayerNames) Add(players, name, "PlayerNames");
        for (var index = 0; index < catalog.Voices.Count; index++)
        {
            var field = $"Voices[{index}]";
            var voice = catalog.Voices[index] ?? throw RoutingPath.Invalid(field, "Voice object is required.");
            Add(voiceIds, voice.Id, field + ".Id");
            if (Same(voice.Id, RoutingCatalog.ConfiguredVoiceId))
                throw RoutingPath.Invalid(field + ".Id", "The configured voice ID is reserved.");
            Text(voice.Name, field + ".Name");
            Text(voice.GptWeightsPath, field + ".GptWeightsPath");
            Text(voice.SovitsWeightsPath, field + ".SovitsWeightsPath");
            Text(voice.ReferenceAudioPath, field + ".ReferenceAudioPath");
            Text(voice.PromptText, field + ".PromptText");
            if (voice.RequestedEmotion is not null) Text(voice.RequestedEmotion, field + ".RequestedEmotion");
            if (voice.ReferenceEmotion is not null) Text(voice.ReferenceEmotion, field + ".ReferenceEmotion");
        }
        for (var index = 0; index < catalog.Speakers.Count; index++)
        {
            var field = $"Speakers[{index}]";
            var speaker = catalog.Speakers[index] ?? throw RoutingPath.Invalid(field, "Speaker object is required.");
            Add(speakerIds, speaker.Id, field + ".Id");
            if (new[] { "protagonist", "narrator", "default_npc" }.Any(id => Same(speaker.Id, id)))
                throw RoutingPath.Invalid(field + ".Id", "This speaker identity is reserved.");
            if (speaker.Names.Count == 0) throw RoutingPath.Invalid(field + ".Names", "At least one name is required.");
            foreach (var name in speaker.Names) Add(labels, name, field + ".Names");
            foreach (var name in speaker.Aliases) Add(labels, name, field + ".Aliases");
            foreach (var name in speaker.Corrections) Add(labels, name, field + ".Corrections");
            Reference(voiceIds, speaker.VoiceId, field + ".VoiceId");
        }
        Reference(speakerIds, catalog.ProtagonistId, "ProtagonistId");
        Reference(voiceIds, catalog.NarratorVoiceId, "NarratorVoiceId");
        Reference(voiceIds, catalog.FallbackVoiceId, "FallbackVoiceId");
    }

    /// <summary>Add：执行RoutingCatalogValidation中的对应校验或计算步骤。</summary>
    private static void Add(HashSet<string> set, string? value, string field)
    {
        if (!set.Add(Text(value, field))) throw RoutingPath.Invalid(field, "Duplicate or ambiguous normalized value.");
    }

    /// <summary>Reference：执行RoutingCatalogValidation中的对应校验或计算步骤。</summary>
    private static void Reference(HashSet<string> ids, string? id, string field)
    {
        if (id is not null && !ids.Contains(Text(id, field)))
            throw RoutingPath.Invalid(field, $"Undefined reference '{id}'.");
    }
}
