using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text.Json;

namespace BetterGenshinImpact.GameTask.QuestVoice.Domain;

/// <summary>Immutable raw generations, with one atomically published metadata manifest per key.</summary>
public sealed class QuestJSONStore
{
    internal const int MetadataSchemaVersion = 1;
    private const int MaxMetadataBytes = 64 * 1024;
    private readonly PathPolicy policy;
    public string DirectoryPath { get; }

    public QuestJSONStore(string experimentRoot, string directory = "cache/QuestJson")
        : this(new PathPolicy(experimentRoot), directory) { }

    public QuestJSONStore(PathPolicy policy, string directory = "cache/QuestJson")
    {
        this.policy = policy ?? throw new ArgumentNullException(nameof(policy));
        DirectoryPath = policy.RequireOutput(directory);
        if (!Within(DirectoryPath, Path.Combine(policy.Root, "cache")) &&
            !Within(DirectoryPath, Path.Combine(policy.Root, "artifacts")))
            throw new ArgumentException("Quest JSON storage must be under the experiment cache or artifacts directory.", nameof(directory));
    }

    public string GetManifestPath(string sourceKey, string language, string questId, Uri? requestUri = null) =>
        policy.RequireOutput(Path.Combine(KeyDirectory(sourceKey, language, questId, requestUri), "metadata.json"));

    /// <summary>Stable parser identity. Raw generations are recorded separately in metadata.</summary>
    public string GetSourcePath(string sourceKey, string language, string questId, Uri? requestUri = null) =>
        policy.RequireOutput(Path.Combine(KeyDirectory(sourceKey, language, questId, requestUri), "quest.json"));

    public string GetRawPath(QuestSourceMetadata metadata)
    {
        var versionPath = GetSourcePath(metadata.SourceKey, metadata.Language, metadata.QuestId, metadata.RequestUri);
        var legacyPath = Path.Combine(KeyBase(metadata.SourceKey, metadata.Language, metadata.QuestId), "quest.json");
        if (!string.Equals(metadata.SourcePath, versionPath, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(metadata.SourcePath, legacyPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Cache source path does not match its version or legacy location.");
        return policy.RequireOutput(Path.Combine(Path.GetDirectoryName(metadata.SourcePath)!, RawName(metadata)));
    }

    internal async Task<CachedQuest?> ReadAsync(IQuestDataSource source, string questId, string language,
        int maxBytes, CancellationToken cancellationToken)
    {
        var expectedUri = QuestLoadIdentity.PublicUri(source.GetQuestUri(questId, language));
        var manifest = GetManifestPath(source.SourceKey, language, questId, expectedUri);
        var legacy = false;
        if (!File.Exists(manifest))
        {
            // Old manifests are read-only compatibility inputs. A new version never overwrites them.
            manifest = policy.RequireOutput(Path.Combine(KeyBase(source.SourceKey, language, questId), "metadata.json"));
            legacy = true;
            if (!File.Exists(manifest)) return null;
        }
        try
        {
            var metadataBytes = await ReadFileAsync(manifest, MaxMetadataBytes, cancellationToken).ConfigureAwait(false);
            var metadata = JsonSerializer.Deserialize<QuestSourceMetadata>(metadataBytes)
                ?? throw new InvalidDataException("Cache metadata is null.");
            ValidateMetadata(metadata, source, questId, language, legacy);
            if (metadata.RawBytes > maxBytes) throw new InvalidDataException("Cached JSON exceeds the configured response limit.");
            var raw = await ReadFileAsync(GetRawPath(metadata), maxBytes, cancellationToken).ConfigureAwait(false);
            if (raw.LongLength != metadata.RawBytes || Convert.ToHexString(SHA256.HashData(raw)) != metadata.Sha256)
                throw new InvalidDataException("Cache raw JSON length or SHA256 does not match metadata.");
            var document = source.Parse(raw, questId, language, metadata.SourcePath) with { SourceMetadata = metadata };
            return new(document, metadata);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or QuestDataLoadException)
        {
            throw new QuestDataLoadException(new(QuestLoadFailureCode.CacheInvalid, $"Quest cache is invalid: {e.Message}"), e);
        }
    }

    internal async Task<QuestSourceMetadata> WriteAsync(IQuestDataSource source, string questId,
        string language, Uri requestUri, Uri responseUri, ReadOnlyMemory<byte> raw,
        DateTimeOffset fetchedUtc, CancellationToken cancellationToken)
    {
        var keyDirectory = KeyDirectory(source.SourceKey, language, questId, requestUri);
        Directory.CreateDirectory(policy.RequireOutput(keyDirectory));
        var hash = Convert.ToHexString(SHA256.HashData(raw.Span));
        var generation = $"{hash.ToLowerInvariant()}-{Guid.NewGuid():N}.json";
        var rawPath = policy.RequireOutput(Path.Combine(keyDirectory, generation));
        var manifest = GetManifestPath(source.SourceKey, language, questId, requestUri);
        var staging = policy.RequireOutput(Path.Combine(keyDirectory, $".metadata-{Guid.NewGuid():N}.tmp"));
        var metadata = new QuestSourceMetadata(MetadataSchemaVersion, source.SourceKey, questId,
            language, QuestLoadIdentity.PublicUri(requestUri), QuestLoadIdentity.PublicUri(responseUri),
            fetchedUtc.ToUniversalTime(), hash, generation, raw.Length, GetSourcePath(source.SourceKey, language, questId, requestUri));
        // An unreferenced generation may remain after failure. Never overwrite a prior generation.
        await WriteDurableAsync(rawPath, raw, cancellationToken).ConfigureAwait(false);
        var metadataBytes = JsonSerializer.SerializeToUtf8Bytes(metadata, new JsonSerializerOptions { WriteIndented = true });
        await WriteDurableAsync(staging, metadataBytes, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        policy.RequireOutput(manifest);
        File.Move(staging, manifest, overwrite: true);
        return metadata;
    }

    private string KeyBase(string sourceKey, string language, string questId) =>
        policy.RequireOutput(Path.Combine(DirectoryPath, QuestLoadIdentity.Source(sourceKey),
            QuestLoadIdentity.Language(language), QuestLoadIdentity.Id(questId)));

    private string KeyDirectory(string sourceKey, string language, string questId, Uri? requestUri = null)
    {
        var query = requestUri is null ? "" : QuestLoadIdentity.PublicUri(requestUri).Query;
        var version = query.Length == 0 ? "no-query" : "query-" + Convert.ToHexString(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(query))).ToLowerInvariant();
        return policy.RequireOutput(Path.Combine(KeyBase(sourceKey, language, questId), "versions", version));
    }

    private void ValidateMetadata(QuestSourceMetadata metadata, IQuestDataSource source, string questId, string language, bool legacy)
    {
        var expected = QuestLoadIdentity.PublicUri(source.GetQuestUri(questId, language));
        var expectedPath = legacy ? Path.Combine(KeyBase(source.SourceKey, language, questId), "quest.json")
            : GetSourcePath(source.SourceKey, language, questId, expected);
        if (metadata.SchemaVersion != MetadataSchemaVersion || metadata.SourceKey != source.SourceKey ||
            metadata.QuestId != questId || metadata.Language != language || metadata.RawBytes < 1 ||
            metadata.FetchedUtc == default || metadata.FetchedUtc.Offset != TimeSpan.Zero ||
            metadata.Sha256 is null || metadata.Sha256.Length != 64 ||
            metadata.Sha256.Any(c => !(c is >= '0' and <= '9' or >= 'A' and <= 'F')) ||
            !string.Equals(metadata.SourcePath, expectedPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Cache metadata schema or identity is invalid.");
        _ = RawName(metadata);
        var request = QuestLoadIdentity.PublicUri(metadata.RequestUri);
        _ = QuestLoadIdentity.PublicUri(metadata.ResponseUri);
        if (!string.Equals(request.AbsoluteUri, expected.AbsoluteUri, StringComparison.Ordinal))
            throw new InvalidDataException("Cache request URL (including version query) does not match its source chapter and language.");
    }

    private static string RawName(QuestSourceMetadata metadata)
    {
        var name = metadata.RawFileName;
        if (name is null || name.Length != 102 || !name.EndsWith(".json", StringComparison.Ordinal) ||
            name[64] != '-' || name[..64] != metadata.Sha256?.ToLowerInvariant() ||
            name[65..97].Any(c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new InvalidDataException("Cache raw generation filename is invalid.");
        return name;
    }

    private static async Task<byte[]> ReadFileAsync(string path, int maxBytes, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length < 1 || stream.Length > maxBytes) throw new InvalidDataException("Cache file has an invalid size.");
        return await ReadBoundedAsync(stream, maxBytes, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<byte[]> ReadBoundedAsync(Stream stream, int maxBytes, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream(Math.Min(maxBytes, 64 * 1024));
        var buffer = new byte[Math.Min(maxBytes, 8192)];
        while (true)
        {
            var remaining = maxBytes - (int)output.Length;
            // One extra byte proves an over-limit chunked stream without buffering the full body.
            var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining + 1)), cancellationToken).ConfigureAwait(false);
            if (read == 0) return output.ToArray();
            if (read > remaining) throw new QuestDataLoadException(new(QuestLoadFailureCode.ResponseTooLarge, $"Quest response exceeds {maxBytes} bytes."));
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WriteDurableAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            8192, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static bool Within(string path, string directory) =>
        path.Equals(directory, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    internal sealed record CachedQuest(QuestDocument Document, QuestSourceMetadata Metadata);
}
