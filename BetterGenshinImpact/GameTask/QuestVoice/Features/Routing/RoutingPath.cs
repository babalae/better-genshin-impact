// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Routing;

/// <summary>RoutingPath：原神自动配音的业务类型。</summary>
internal static class RoutingPath
{
    /// <summary>Resolve：执行RoutingPath中的对应校验或计算步骤。</summary>
    public static string Resolve(string relativePath, string directory, string field)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw Invalid(field, "A relative file path is required.");
        foreach (var segment in relativePath.Split(['/', '\\']))
        {
            if (segment is "." or "..") continue;
            var stem = segment.Split('.')[0].ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(segment) || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || segment.EndsWith('.') || segment.EndsWith(' ')
                || stem is "CON" or "PRN" or "AUX" or "NUL"
                || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal)
                    || stem.StartsWith("LPT", StringComparison.Ordinal)) && "123456789¹²³".Contains(stem[3])))
                throw Invalid(field, "Invalid relative file path.");
        }
        try { return Path.GetFullPath(relativePath, directory); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        { throw Invalid(field, "Invalid relative file path."); }
    }

    /// <summary>Invalid：执行RoutingPath中的对应校验或计算步骤。</summary>
    internal static VoiceException Invalid(string field, string message) =>
        new(VoiceErrorCode.Configuration, $"{field}: {message}");
}
