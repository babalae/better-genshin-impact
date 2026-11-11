using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
namespace BetterGenshinImpact.GameTask.QuestVoice.Domain;

public enum SpeakerIdentityStatus { Unresolved, DisplayNameOnly, Missing, Concealed, Resolved }
public sealed record SpeakerIdentity(string SpeakerKey, string DisplayName, IReadOnlyList<string> Aliases,
    SpeakerIdentityStatus IdentityStatus, string IdentitySource);
public interface ISpeakerIdentityResolver
{
    SpeakerIdentity Resolve(QuestNode node);
}
/// <summary>The caller declares a verified source field; no guessed field names are enabled by default.</summary>
public sealed class DeclaredSpeakerIdentityResolver(string idField, string sourceNamespace) : ISpeakerIdentityResolver
{
    public SpeakerIdentity Resolve(QuestNode node)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(IdField);
        ArgumentException.ThrowIfNullOrWhiteSpace(SourceNamespace);
        if (node.Raw.ValueKind != System.Text.Json.JsonValueKind.Object || !node.Raw.TryGetProperty(IdField, out var rawId))
            return QuestSpeakerIdentities.DisplayOnly(node);
        var id = rawId.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => rawId.GetString(),
            System.Text.Json.JsonValueKind.Number => rawId.GetRawText(),
            _ => null
        };
        if (string.IsNullOrWhiteSpace(id)) return QuestSpeakerIdentities.DisplayOnly(node);
        // JSON tuple prevents collisions between source namespace and source identity.
        var key = System.Text.Json.JsonSerializer.Serialize(new[] { SourceNamespace, IdField, id });
        return new(key, node.Role, [], QuestSpeakerIdentities.IsUnknown(node.Role)
            ? SpeakerIdentityStatus.Concealed : SpeakerIdentityStatus.Resolved, "declared-source-field:" + IdField);
    }
    public string IdField { get; } = idField;
    public string SourceNamespace { get; } = sourceNamespace;
}

internal static class QuestSpeakerIdentities
{
    public static bool IsUnknown(string name) => name.Trim().Length > 0 && name.Trim().All(c => c is '?' or '？');

    public static SpeakerIdentity DisplayOnly(QuestNode node) => new("occurrence:" + node.Key, node.Role, [],
        string.IsNullOrWhiteSpace(node.Role) ? SpeakerIdentityStatus.Missing
            : IsUnknown(node.Role) ? SpeakerIdentityStatus.Unresolved : SpeakerIdentityStatus.DisplayNameOnly,
        "node.role:display-only");

    public static IReadOnlyList<QuestNode> Resolve(IReadOnlyList<QuestNode> nodes, ISpeakerIdentityResolver? resolver)
    {
        var resolved = nodes.Select(node => node with { Speaker = resolver?.Resolve(node) ?? DisplayOnly(node) }).ToArray();
        var aliases = resolved.Where(n => n.Speaker!.IdentityStatus is SpeakerIdentityStatus.Resolved or SpeakerIdentityStatus.Concealed)
            .GroupBy(n => n.Speaker!.SpeakerKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)Array.AsReadOnly(g.SelectMany(n =>
                n.Speaker!.Aliases.Append(n.Speaker.DisplayName)).Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.Ordinal).ToArray()), StringComparer.Ordinal);
        return Array.AsReadOnly(resolved.Select(node => aliases.TryGetValue(node.Speaker!.SpeakerKey, out var names)
            ? node with { Speaker = node.Speaker with { Aliases = names } } : node).ToArray());
    }
}
