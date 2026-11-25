using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
namespace BetterGenshinImpact.GameTask.QuestVoice.Domain;

/// <summary>A source owns its endpoint and schema mapping into the existing quest model.</summary>
public interface IQuestDataSource
{
    string SourceKey { get; }
    Uri GetQuestUri(string questId, string language);
    QuestDocument Parse(ReadOnlyMemory<byte> json, string questId, string language, string sourcePath);
}

public sealed record QuestSourceMetadata(
    int SchemaVersion, string SourceKey, string QuestId, string Language,
    Uri RequestUri, Uri ResponseUri, DateTimeOffset FetchedUtc, string Sha256,
    string RawFileName, long RawBytes, string SourcePath)
{
    public string RequestedId => QuestId;
}

public enum QuestLoadFailureCode
{
    HttpStatus, Transport, Timeout, ResponseTooLarge, InvalidJson, InvalidSchema,
    IdentityMismatch, CacheInvalid, CacheWriteFailed
}

public sealed record QuestLoadFailure(QuestLoadFailureCode Code, string Message,
    int Attempts = 0, int? HttpStatus = null, string? CacheReadFailure = null);

public sealed class QuestDataLoadException : Exception
{
    public QuestLoadFailure Failure { get; }
    public QuestDataLoadException(QuestLoadFailure failure, Exception? innerException = null)
        : base(failure.Message, innerException) => Failure = failure;
}

public sealed record QuestDataLoadResult(QuestDocument Document,
    QuestSourceMetadata SourceMetadata, bool FromCache, bool IsStale, QuestLoadFailure? Failure)
{
    public QuestSourceMetadata Metadata => SourceMetadata;
}

public sealed record QuestDataLoaderOptions
{
    public int MaxAttempts { get; init; } = 3;
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public int MaxResponseBytes { get; init; } = 16 * 1024 * 1024;
    public TimeSpan InitialRetryDelay { get; init; } = TimeSpan.FromMilliseconds(200);
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan MaxRetryAfter { get; init; } = TimeSpan.FromSeconds(5);

    public void Validate()
    {
        if (MaxAttempts is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(MaxAttempts));
        if (RequestTimeout <= TimeSpan.Zero || RequestTimeout > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(RequestTimeout));
        if (MaxResponseBytes is < 1 or > 128 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(MaxResponseBytes));
        if (InitialRetryDelay < TimeSpan.Zero || MaxRetryDelay < InitialRetryDelay ||
            MaxRetryDelay > TimeSpan.FromSeconds(30) || MaxRetryAfter < TimeSpan.Zero ||
            MaxRetryAfter > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(InitialRetryDelay), "Retry delays must be finite and bounded.");
    }
}

public static class QuestLoadIdentity
{
    public static string Id(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length is < 1 or > 20 || value[0] is < '1' or > '9' ||
            value.Any(c => c is < '0' or > '9'))
            throw new ArgumentException("Quest ID must be a canonical positive numeric source chapter ID.", nameof(value));
        return value;
    }

    public static string Language(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var normalized = value.ToLowerInvariant() switch { "jp" or "jpn" => "jpn", "chs" => "chs", _ => value.ToLowerInvariant() };
        if (normalized.Length is < 2 or > 12 || normalized.Any(c => c is < 'a' or > 'z'))
            throw new ArgumentException("Language must be a safe language code.", nameof(value));
        return normalized;
    }

    public static string Source(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length is < 1 or > 64 || value[0] is < 'a' or > 'z' ||
            value.Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')) ||
            value is "con" or "prn" or "aux" or "nul" ||
            (value.Length == 4 && (value.StartsWith("com", StringComparison.Ordinal) || value.StartsWith("lpt", StringComparison.Ordinal)) && value[3] is >= '0' and <= '9'))
            throw new ArgumentException("SourceKey must be a safe lowercase source identifier.", nameof(value));
        return value;
    }

    public static Uri PublicUri(Uri value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!value.IsAbsoluteUri || value.Scheme != Uri.UriSchemeHttps || value.UserInfo.Length != 0 || value.Fragment.Length != 0)
            throw new ArgumentException("Quest endpoints must be absolute HTTPS URIs without credentials or fragments.", nameof(value));
        return value;
    }
}
