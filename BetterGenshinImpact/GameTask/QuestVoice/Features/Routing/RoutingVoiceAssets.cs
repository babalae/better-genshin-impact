// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Routing;

/// <summary>RoutingVoiceAssets：原神自动配音的业务类型。</summary>
internal static class RoutingVoiceAssets
{
    /// <summary>把固定声线设置转换为路由兜底声线。</summary>
    public static RoutingVoice Legacy(VoiceSettings voice) => new(RoutingCatalog.ConfiguredVoiceId, voice.Name,
        voice.GptWeightsPath, voice.SovitsWeightsPath, voice.ReferenceAudioPath, voice.PromptText,
        voice.RequestedEmotion, voice.ReferenceEmotion, voice.Provider?.ProviderId ?? "gpt-sovits",
        voice.Provider?.InstanceId, voice.Provider?.VoiceId);

    /// <summary>读取当前不可变状态快照。</summary>
    public static VoiceBinding Snapshot(RoutingVoice voice) => new(voice.Name, voice.GptWeightsPath, voice.SovitsWeightsPath,
        Array.AsReadOnly(new[] { new ReferenceAudio(voice.ReferenceAudioPath, voice.PromptText, voice.ReferenceEmotion) }),
        new(voice.ProviderId, voice.InstanceId ?? "", voice.ProviderVoiceId ?? ""));

    /// <summary>校验声线资产可读且非空，并返回具体不可用原因。</summary>
    public static async Task<string?> UnavailableAsync(RoutingVoice voice, CancellationToken cancellationToken,
        bool serverSidePaths = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(voice.Name)) return "Voice.Name: Nonempty text is required.";
        if (!Tts.TtsProviderRegistry.GetInfo(voice.ProviderId).Capabilities.VoiceAssets)
            return string.IsNullOrWhiteSpace(voice.ProviderVoiceId) ? "ProviderVoiceId: A voice ID is required." : null;
        if (voice.InstanceId is not null) serverSidePaths = voice.InstanceId == "remote";
        if (string.IsNullOrWhiteSpace(voice.PromptText)) return "Voice.PromptText: Nonempty reference text is required.";
        foreach (var (field, path) in new[] { ("GptWeightsPath", voice.GptWeightsPath),
            ("SovitsWeightsPath", voice.SovitsWeightsPath), ("ReferenceAudioPath", voice.ReferenceAudioPath) })
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (serverSidePaths)
            {
                if (string.IsNullOrWhiteSpace(path)) return $"{field}: Server path is required.";
                continue;
            }
            try
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                if (stream.Length == 0 || await stream.ReadAsync(new byte[1], cancellationToken).ConfigureAwait(false) == 0)
                    return $"{field} '{path}': File is empty.";
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return $"{field} '{path}': {exception.Message}";
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        return null;
    }
}
