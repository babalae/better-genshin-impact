using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BetterGenshinImpact.GameTask.QuestVoice.Domain;

public static class QuestLanguages
{
    public static string Normalize(string language) => language.ToLowerInvariant() switch
    { "chs" => "chs", "jp" or "jpn" => "jpn", _ => throw new ArgumentException("Only CHS / JP are supported.") };
    public static string Yatta(string language) => Normalize(language) == "chs" ? "CHS" : "JP";
    public static string Ocr(string language) => Normalize(language) == "chs" ? "zh-Hans" : "ja";
}
public sealed record LiveLanguageOptions(string RecognitionLanguage = "jpn", string SpeechLanguage = "jpn");
public enum MappingStatus { Matched, MissingTarget, MissingSource, Ambiguous, ContextMismatch, OptionMismatch, BranchMismatch, VersionMismatch, NotSpeakable }
public sealed record DialogueMapping(QuestNode? Chs, QuestNode? Jp, MappingStatus Status, string Reason)
{
    public string DialogueId => (Chs ?? Jp)!.Id;
    public QuestNode? Node(string language) => QuestLanguages.Normalize(language) == "chs" ? Chs : Jp;
}

/// <summary>Exact structural pairing. Source graph edges remain authoritative for recognition.</summary>
public sealed class DialogueLanguageMap
{
    public QuestDocument Chs { get; }
    public QuestDocument Jp { get; }
    public string VersionState { get; }
    public string Fingerprint { get; }
    public IReadOnlyList<DialogueMapping> Entries { get; }
    private readonly Dictionary<string, DialogueMapping> byNode;
    public DialogueLanguageMap(QuestDocument chs, QuestDocument jp, string? chsVersion = null, string? jpVersion = null,
        string sourceKey = "yatta")
    {
        if (chs.ChapterId != jp.ChapterId || QuestLanguages.Normalize(chs.Language) != "chs" || QuestLanguages.Normalize(jp.Language) != "jpn")
            throw new ArgumentException("Pair requires same ChapterId and CHS / JP documents.");
        Chs = chs; Jp = jp;
        VersionState = chsVersion != jpVersion ? "VersionMismatch" : chsVersion is null ? "VersionUnknown" : "VersionMatched:" + chsVersion;
        Fingerprint = SpeechHash.Of(JsonSerializer.Serialize(new[] { sourceKey, chs.ChapterId, chsVersion, jpVersion,
            SpeechHash.Of(chs.Raw.GetRawText()), SpeechHash.Of(jp.Raw.GetRawText()) }));
        var left = chs.Nodes.GroupBy(Scope).ToDictionary(g => g.Key, g => g.ToArray());
        var right = jp.Nodes.GroupBy(Scope).ToDictionary(g => g.Key, g => g.ToArray());
        // Validate speakable nodes directly; the legacy experimental QuestGraph is not required.
        var entries = new List<DialogueMapping>();
        foreach (var scope in left.Keys.Union(right.Keys).Order(StringComparer.Ordinal))
        {
            var c = left.GetValueOrDefault(scope) ?? []; var j = right.GetValueOrDefault(scope) ?? [];
            MappingStatus status; string reason;
            if (VersionState == "VersionMismatch") { status = MappingStatus.VersionMismatch; reason = "Source vh differs."; }
            else if (c.Length > 1 || j.Length > 1) { status = MappingStatus.Ambiguous; reason = $"Exact scope occurrences CHS={c.Length}, JP={j.Length}; no first-match fallback."; }
            else if (c.Length == 0 || j.Length == 0)
            {
                var other = c.Length == 0 ? chs : jp; var present = (c.FirstOrDefault() ?? j[0]);
                status = other.Nodes.Any(n => n.Id == present.Id) ? MappingStatus.ContextMismatch
                    : c.Length == 0 ? MappingStatus.MissingSource : MappingStatus.MissingTarget;
                reason = "Exact Chapter/Story/Task/Dialogue scope absent; candidate IDs are retained in the other document.";
            }
            else if (c[0].Type != j[0].Type || !c[0].Texts.Select(t => t.OptionNumber).SequenceEqual(j[0].Texts.Select(t => t.OptionNumber)))
            { status = MappingStatus.OptionMismatch; reason = "Node type or OptionNumber set differs."; }
            else if (!Branches(chs, c[0]).SequenceEqual(Branches(jp, j[0])))
            { status = MappingStatus.BranchMismatch; reason = "OptionNumber / next DialogueId structure differs."; }
            else if (c[0].Type != "SingleDialog" || !CanActivate(chs, c[0]) || !CanActivate(jp, j[0]))
            { status = MappingStatus.NotSpeakable; reason = "Choice, empty, control or unsafe node; structural pair retained."; }
            else { status = MappingStatus.Matched; reason = "Unique exact Chapter/Story/Task/Dialogue identity and branch structure."; }
            // Retain every occurrence, including ambiguous groups, without selecting a target for them.
            if (c.Length <= 1 && j.Length <= 1) entries.Add(new(c.FirstOrDefault(), j.FirstOrDefault(), status, reason));
            else
            {
                entries.AddRange(c.Select(n => new DialogueMapping(n, null, status, reason)));
                entries.AddRange(j.Select(n => new DialogueMapping(null, n, status, reason)));
            }
        }
        Entries = entries.AsReadOnly();
        byNode = entries.SelectMany(e => new[] { e.Chs, e.Jp }.Where(n => n is not null).Select(n => (n!.Key, Entry: e)))
            .ToDictionary(x => x.Key, x => x.Entry, StringComparer.Ordinal);
    }
    /// <summary>Preserve the original conservative graph safety check without its unused playback engine.</summary>
    private static bool CanActivate(QuestDocument document, QuestNode node)
    {
        if (node.Type is not ("SingleDialog" or "MultiDialog") || node.Texts.Count == 0 ||
            node.Texts.Any(text => string.IsNullOrWhiteSpace(text.Text)) ||
            node.Type == "SingleDialog" && node.Texts.Count != 1) return false;

        var edges = document.Edges.Where(edge => edge.FromKey == node.Key).ToArray();
        if (edges.Length != node.Texts.Count ||
            edges.Select(edge => edge.OptionNumber).Distinct().Count() != edges.Length ||
            node.Texts.Any(text => !edges.Any(edge => edge.OptionNumber == text.OptionNumber))) return false;

        return edges.All(edge => edge.Status == "terminal"
            ? edge.TargetKey is null && edge.TargetId is null
            : edge.Status == "resolved" && edge.TargetKey is not null &&
                document.Nodes.FirstOrDefault(target => target.Key == edge.TargetKey) is { } target &&
                target.TaskContext == node.TaskContext && target.Id == edge.TargetId);
    }

    public QuestDocument Document(string language) => QuestLanguages.Normalize(language) == "chs" ? Chs : Jp;
    public DialogueMapping? ForNode(string nodeKey) => byNode.GetValueOrDefault(nodeKey);
    private static string Scope(QuestNode n) => JsonSerializer.Serialize(new[] { n.ChapterId, n.StoryId, n.TaskId, n.Id });
    private static IEnumerable<string> Branches(QuestDocument d, QuestNode n) => d.Edges.Where(e => e.FromKey == n.Key)
        .Select(e => JsonSerializer.Serialize(new object?[] { e.OptionNumber, e.TargetId, e.Status })).Order(StringComparer.Ordinal);
}

public static class SpeechHash
{
    private sealed record AssetVersion(long Length, long Modified, long Created, string Hash);
    private static readonly Dictionary<string, AssetVersion> assets = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object assetGate = new();
    public static string Of(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public static string FileHash(string path)
    { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    /// <summary>Reuse content hashes only while the local file version (size/write/creation) is unchanged.</summary>
    public static string AssetHash(string path)
    {
        var full = Path.GetFullPath(path);
        lock (assetGate)
        {
            var info = new FileInfo(full);
            if (assets.TryGetValue(full, out var prior) && prior.Length == info.Length && prior.Modified == info.LastWriteTimeUtc.Ticks && prior.Created == info.CreationTimeUtc.Ticks)
                return prior.Hash;
            var hash = FileHash(full); var after = new FileInfo(full);
            if (info.Length != after.Length || info.LastWriteTimeUtc != after.LastWriteTimeUtc)
                throw new IOException("Voice asset changed during fingerprinting.");
            assets[full] = new(after.Length, after.LastWriteTimeUtc.Ticks, after.CreationTimeUtc.Ticks, hash); return hash;
        }
    }
}

public static class QuestLanguagePairLoader
{
    public static async Task<DialogueLanguageMap> LoadAsync(QuestDataLoader loader, string chapterId, bool refresh = false,
        string? version = null, CancellationToken cancellationToken = default)
    {
        var chs = await loader.LoadAsync(chapterId, QuestLanguages.Yatta("chs"), refresh, cancellationToken).ConfigureAwait(false);
        var jp = await loader.LoadAsync(chapterId, QuestLanguages.Yatta("jpn"), refresh, cancellationToken).ConfigureAwait(false);
        // Compare actual request version, including stale-cache metadata, rather than trusting the UI argument.
        static string? Version(QuestSourceMetadata m) => string.IsNullOrEmpty(m.RequestUri.Query) ? null : m.RequestUri.Query;
        return new(chs.Document, jp.Document, Version(chs.Metadata), Version(jp.Metadata), chs.Metadata.SourceKey);
    }
}
