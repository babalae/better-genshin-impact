// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;
using Serilog;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Routing;

/// <summary>VoiceRoutingService：原神自动配音的业务类型。</summary>
public sealed class VoiceRoutingService(SpeakerCatalogLoader loader, ISpeakerRouter router, ILogger? logger = null) : IVoiceRoutingService
{
    /// <summary>按接受姓名解析角色和实际声线，保留兜底原因。</summary>
    public async Task<VoiceRoutingResult> ResolveAsync(VoiceRoutingRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = await loader.LoadAsync(request.Settings.CatalogPath, cancellationToken, request.Settings.ServerSidePaths).ConfigureAwait(false);
        var catalog = snapshot.Catalog;
        var speaker = router.Route(request.Input, catalog);
        cancellationToken.ThrowIfCancellationRequested();
        string? fallbackReason = null;
        RoutingVoice? voice = null;
        // An ordinary unknown NPC deliberately selects the generic voice under either policy.
        if (speaker.Layer == RoutingLayer.Fallback) fallbackReason = speaker.Reason;
        else if (speaker.VoiceId is null)
            fallbackReason = speaker.Reason == "ProtagonistNotConfigured" ? speaker.Reason
                : speaker.Layer == RoutingLayer.Narrator ? "NarratorVoiceNotConfigured" : "VoiceNotBound";
        else
        {
            voice = Find(catalog, speaker.VoiceId);
            var unavailable = await RoutingVoiceAssets.UnavailableAsync(voice, cancellationToken, request.Settings.ServerSidePaths).ConfigureAwait(false);
            if (unavailable is not null) fallbackReason = "VoiceAssetsUnavailable: " + unavailable;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (fallbackReason is not null)
        {
            if (speaker.Layer != RoutingLayer.Fallback && catalog.MissingVoicePolicy == MissingVoicePolicy.Fail)
                throw RoutingPath.Invalid($"Catalog '{snapshot.CatalogPath}' / {speaker.CanonicalId}", fallbackReason);
            voice = catalog.FallbackVoiceId is null ? RoutingVoiceAssets.Legacy(request.FallbackVoice) : Find(catalog, catalog.FallbackVoiceId);
            var unavailable = await RoutingVoiceAssets.UnavailableAsync(voice, cancellationToken, request.Settings.ServerSidePaths).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (unavailable is not null)
                throw RoutingPath.Invalid($"Catalog '{snapshot.CatalogPath}' / FallbackVoice '{voice.Id}'", unavailable);
        }
        var binding = RoutingVoiceAssets.Snapshot(voice!);
        var canonicalName = catalog.Speakers.FirstOrDefault(candidate =>
            RoutingCatalogValidation.Same(candidate.Id, speaker.CanonicalId))?.Names[0];
        var result = new VoiceRoutingResult(request.Input, speaker, snapshot.CatalogSha256, speaker.VoiceId,
            voice!.Id, binding, voice.RequestedEmotion, fallbackReason, canonicalName);
        logger?.Information("SpeakerRouted {Layer} {CanonicalId} {RequestedVoiceId} {VoiceId} {FallbackReason} {CatalogSha256}",
            speaker.Layer, speaker.CanonicalId, speaker.VoiceId, voice.Id, fallbackReason, snapshot.CatalogSha256);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    /// <summary>Find：执行VoiceRoutingService中的对应校验或计算步骤。</summary>
    private static RoutingVoice Find(RoutingCatalog catalog, string id) =>
        catalog.Voices.SingleOrDefault(voice => RoutingCatalogValidation.Same(voice.Id, id))
        ?? throw RoutingPath.Invalid("VoiceId", $"Undefined reference '{id}'.");
}
