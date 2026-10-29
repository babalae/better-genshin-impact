// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Ocr;

/// <summary>OcrResultAssembler：原神自动配音的业务类型。</summary>
public static class OcrResultAssembler
{
    /// <summary>Accept：执行OcrResultAssembler中的对应校验或计算步骤。</summary>
    public static IReadOnlyList<OcrRegionResult> Accept(IEnumerable<OcrRegionResult> regions, double nameThreshold, double dialogueThreshold) =>
        regions.Select(region =>
        {
            var trimmed = region.RawText.Trim();
            var threshold = region.Region.Kind == OcrRegionKind.Speaker ? nameThreshold : dialogueThreshold;
            var reason = trimmed.Length == 0 ? OcrRejectionReason.Empty
                : !double.IsFinite(region.Confidence) || region.Confidence < threshold ? OcrRejectionReason.LowConfidence : OcrRejectionReason.None;
            return region with { AcceptedText = reason == OcrRejectionReason.None ? trimmed : "", RejectionReason = reason };
        }).ToArray();
    /// <summary>Dialogue：执行OcrResultAssembler中的对应校验或计算步骤。</summary>
    public static string Dialogue(IEnumerable<OcrRegionResult> regions) => string.Concat(regions
        .Where(r=>r.Region.Kind == OcrRegionKind.Dialogue).OrderBy(r=>r.Region.Order).Select(r=>r.AcceptedText));
    /// <summary>Speaker：执行OcrResultAssembler中的对应校验或计算步骤。</summary>
    public static string? Speaker(IEnumerable<OcrRegionResult> regions) => regions
        .FirstOrDefault(r=>r.Region.Kind == OcrRegionKind.Speaker && r.AcceptedText.Length > 0)?.AcceptedText;
}
