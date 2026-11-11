using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.TTS;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.PreSynthesis;

/// <summary>
/// P0 与 P1 共用的严格音频缓存指纹。文本、语言、角色、引擎、模型与参考音频任何一项
/// 不同时都必须缓存未命中，避免播放上一次模型或其他角色生成的声音。
/// </summary>
public static class QuestVoiceCacheIdentity
{
    /// <summary>返回安全文件名使用的 SHA-256 小写十六进制摘要。</summary>
    public static string ComputeHash(AppSettings settings, VoiceBinding binding,
        string speaker, string text, string speechLanguage)
    {
        // Preserve existing CHS cache keys (the previous default was zh), but invalidate
        // JP caches generated with the old, incorrect zh reference-prompt language.
        var promptLanguage = settings.Protocol?.PromptLanguage ?? "zh";
        var cacheLanguage = string.Equals(promptLanguage, "zh", StringComparison.Ordinal)
            ? speechLanguage : speechLanguage + "|prompt:" + promptLanguage;
        var fingerprint = string.Join("\n", new[]
        {
            cacheLanguage, speaker, text, settings.TtsServiceUrl.AbsoluteUri,
            settings.Provider?.ProviderId ?? "", settings.Provider?.InstanceId ?? "",
            settings.Provider?.VoiceId ?? "", settings.Extra?.Model ?? "",
            settings.Extra?.Language ?? "", settings.Extra?.Format ?? "",
            binding.Name, VersionOf(binding.GptWeightsPath), VersionOf(binding.SovitsWeightsPath),
            VersionOf(binding.References.Count > 0 ? binding.References[0].Path : ""),
            binding.References.Count > 0 ? binding.References[0].PromptText : ""
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)))
            .ToLowerInvariant();
    }

    /// <summary>仅使用模型文件尺寸、时间戳和路径，不能逐句对 GB 级模型重新计算 SHA。</summary>
    private static string VersionOf(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}" : path;
        }
        catch (Exception) { return path; }
    }
}
