using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.GameTask.Common.Resin;

/// <summary>按树脂种类存放的一组值，支持嵌套值的结构相等比较。</summary>
/// <typeparam name="T">每类树脂对应的值类型。</typeparam>
public sealed record ResinKindValues<T>
{
    /// <summary>浓缩树脂对应的值。</summary>
    public T Condensed { get; set; } = default!;

    /// <summary>原粹树脂对应的值。</summary>
    public T Original { get; set; } = default!;

    /// <summary>须臾树脂对应的值。</summary>
    public T Transient { get; set; } = default!;

    /// <summary>脆弱树脂对应的值。</summary>
    public T Fragile { get; set; } = default!;

    /// <summary>按树脂种类读写；索引器不参与 JSON 序列化。</summary>
    [JsonIgnore]
    public T this[ResinKind kind]
    {
        get => kind switch
        {
            ResinKind.Condensed => Condensed,
            ResinKind.Original => Original,
            ResinKind.Transient => Transient,
            ResinKind.Fragile => Fragile,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        set
        {
            switch (kind)
            {
                case ResinKind.Condensed: Condensed = value; break;
                case ResinKind.Original: Original = value; break;
                case ResinKind.Transient: Transient = value; break;
                case ResinKind.Fragile: Fragile = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
    }
}

/// <summary>所有任务共用的树脂使用顺序。</summary>
public static class ResinKinds
{
    /// <summary>浓缩、原粹、须臾、脆弱的只读顺序表。</summary>
    public static IReadOnlyList<ResinKind> InOrder { get; } = Array.AsReadOnly(new[]
    {
        ResinKind.Condensed, ResinKind.Original, ResinKind.Transient, ResinKind.Fragile
    });
}
