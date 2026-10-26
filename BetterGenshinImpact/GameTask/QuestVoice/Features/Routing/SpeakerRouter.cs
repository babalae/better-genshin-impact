// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Routing;

/// <summary>SpeakerRouter：原神自动配音的业务类型。</summary>
public sealed class SpeakerRouter : ISpeakerRouter
{
    /// <summary>依次按主角、旁白、规范姓名、别名、纠错与默认 NPC 解析声线。</summary>
    public SpeakerRoute Route(SpeakerRoutingInput input, RoutingCatalog catalog)
    {
        if (!Enum.IsDefined(input.NameStatus) || !Enum.IsDefined(input.Context))
            throw RoutingPath.Invalid("Input", "Undefined name status or dialogue context.");
        RoutingCatalogValidation.Validate(catalog);
        var name = input.NameStatus == SpeakerNameStatus.Accepted && !string.IsNullOrWhiteSpace(input.AcceptedName)
            ? RoutingCatalogValidation.Normalize(input.AcceptedName, "Input.AcceptedName") : null;
        var playerName = name is not null && catalog.PlayerNames.Any(player => RoutingCatalogValidation.Same(player, name));
        if (input.Context == DialogueContext.Protagonist || playerName)
        {
            if (catalog.ProtagonistId is null)
                return new(RoutingLayer.Protagonist, "protagonist", null, "ProtagonistNotConfigured");
            var protagonist = catalog.Speakers.Single(speaker => RoutingCatalogValidation.Same(speaker.Id, catalog.ProtagonistId));
            return new(RoutingLayer.Protagonist, protagonist.Id, protagonist.VoiceId,
                input.Context == DialogueContext.Protagonist ? "ProtagonistContext" : "PlayerName");
        }
        if (input.Context == DialogueContext.Narration)
            return new(RoutingLayer.Narrator, "narrator", catalog.NarratorVoiceId, "NarrationContext");
        if (name is not null)
        {
            var canonical = Match(catalog, name, speaker => speaker.Names, RoutingLayer.Canonical, "ExactName");
            if (canonical is not null) return canonical;
            var alias = Match(catalog, name, speaker => speaker.Aliases, RoutingLayer.Alias, "ExactAlias");
            if (alias is not null) return alias;
            var correction = Match(catalog, name, speaker => speaker.Corrections, RoutingLayer.Correction, "ExactCorrection");
            if (correction is not null) return correction;
        }
        var reason = input.NameStatus == SpeakerNameStatus.Rejected ? "RejectedName" : name is null ? "MissingName" : "UnknownName";
        return new(RoutingLayer.Fallback, "default_npc", catalog.FallbackVoiceId, reason);
    }

    /// <summary>Match：执行SpeakerRouter中的对应校验或计算步骤。</summary>
    private static SpeakerRoute? Match(RoutingCatalog catalog, string name,
        Func<SpeakerDefinition, IReadOnlyList<string>> names, RoutingLayer layer, string reason)
    {
        var speaker = catalog.Speakers.FirstOrDefault(candidate => names(candidate).Any(value => RoutingCatalogValidation.Same(value, name)));
        return speaker is null ? null : new(layer, speaker.Id, speaker.VoiceId, reason);
    }
}
