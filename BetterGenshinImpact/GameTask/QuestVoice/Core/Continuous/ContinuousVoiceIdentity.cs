// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

namespace BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;

/// <summary>ContinuousVoiceIdentityKind：原神自动配音的业务类型。</summary>
public enum ContinuousVoiceIdentityKind { Canonical, UnknownSpeaker, FixedVoice }

/// <summary>Speaker identity fields remain separate, independent of the selected audio voice.
/// Identity equality follows routing's Trim/FormC/OrdinalIgnoreCase rules; dialogue text is not part of this type.</summary>
public sealed record ContinuousVoiceIdentity
{
    /// <summary>Kind：当前业务对象的配置、身份或状态值。</summary>
    public ContinuousVoiceIdentityKind Kind { get; }
    /// <summary>CanonicalId：当前业务对象的配置、身份或状态值。</summary>
    public string? CanonicalId { get; }
    /// <summary>只采用通过 OCR 接受判定的姓名。</summary>
    public string? AcceptedName { get; }

    /// <summary>建立 ContinuousVoiceIdentity 的业务依赖与初始状态。</summary>
    public ContinuousVoiceIdentity(ContinuousVoiceIdentityKind kind, string? canonicalId = null, string? acceptedName = null)
    {
        Kind = kind;
        CanonicalId = Normalize(canonicalId);
        AcceptedName = Normalize(acceptedName);
    }

    /// <summary>Equals：执行ContinuousVoiceIdentity中的对应校验或计算步骤。</summary>
    public bool Equals(ContinuousVoiceIdentity? other) => other is not null && Kind == other.Kind
        && StringComparer.OrdinalIgnoreCase.Equals(CanonicalId, other.CanonicalId)
        && StringComparer.OrdinalIgnoreCase.Equals(AcceptedName, other.AcceptedName);

    /// <summary>GetHashCode：执行ContinuousVoiceIdentity中的对应校验或计算步骤。</summary>
    public override int GetHashCode() => HashCode.Combine(Kind,
        CanonicalId is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(CanonicalId),
        AcceptedName is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(AcceptedName));

    /// <summary>执行规范化与边缘空白处理，不作模糊角色猜测。</summary>
    private static string? Normalize(string? value) => value?.Trim().Normalize(NormalizationForm.FormC);
}
