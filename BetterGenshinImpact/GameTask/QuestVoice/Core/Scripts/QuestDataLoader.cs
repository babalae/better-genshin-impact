using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Net;

namespace BetterGenshinImpact.GameTask.QuestVoice.Domain;

/// <summary>Reusable-client HTTP loading, bounded transient retry and validated local fallback.</summary>
public sealed class QuestDataLoader
{
    // Shared by all loader instances. Entries stay alive to avoid removing a lock with waiting callers.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> KeyLocks =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly IQuestDataSource source;
    private readonly HttpClient client;
    private readonly QuestJSONStore store;
    private readonly QuestDataLoaderOptions options;

    public QuestDataLoader(IQuestDataSource source, HttpClient client, QuestJSONStore store,
        QuestDataLoaderOptions? options = null)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.options = options ?? new();
        this.options.Validate();
        _ = QuestLoadIdentity.Source(source.SourceKey);
    }

    public async Task<QuestDataLoadResult> LoadAsync(string questId, string language,
        bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = QuestLoadIdentity.Id(questId);
        var normalizedLanguage = QuestLoadIdentity.Language(language);
        var uri = QuestLoadIdentity.PublicUri(source.GetQuestUri(id, normalizedLanguage));
        var key = store.GetManifestPath(source.SourceKey, normalizedLanguage, id, uri);
        var gate = KeyLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            QuestJSONStore.CachedQuest? cached = null;
            string? cacheReadFailure = null;
            try { cached = await store.ReadAsync(source, id, normalizedLanguage, options.MaxResponseBytes, cancellationToken).ConfigureAwait(false); }
            catch (QuestDataLoadException e) when (e.Failure.Code == QuestLoadFailureCode.CacheInvalid)
            { cacheReadFailure = e.Message; }
            cancellationToken.ThrowIfCancellationRequested();
            if (cached is not null && !forceRefresh)
                return new(cached.Document, cached.Metadata, FromCache: true, IsStale: false, Failure: null);
            try
            {
                var fetched = await FetchAsync(uri, id, normalizedLanguage, forceRefresh, cancellationToken).ConfigureAwait(false);
                QuestSourceMetadata metadata;
                try
                {
                    metadata = await store.WriteAsync(source, id, normalizedLanguage, uri,
                        fetched.ResponseUri, fetched.Raw, fetched.FetchedUtc, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
                {
                    throw new QuestDataLoadException(new(QuestLoadFailureCode.CacheWriteFailed,
                        $"Validated quest JSON could not be published to the cache: {e.Message}", fetched.Attempts), e);
                }
                cancellationToken.ThrowIfCancellationRequested();
                return new(fetched.Document with { SourceMetadata = metadata }, metadata, FromCache: false, IsStale: false, Failure: null);
            }
            catch (QuestDataLoadException e)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var failure = e.Failure with { CacheReadFailure = cacheReadFailure };
                if (cached is not null)
                    return new(cached.Document, cached.Metadata, FromCache: true, IsStale: true, failure);
                throw new QuestDataLoadException(failure, e);
            }
        }
        finally { gate.Release(); }
    }

    private async Task<FetchedQuest> FetchAsync(Uri uri, string questId, string language, bool forceRefresh, CancellationToken cancellationToken)
    {
        QuestDataLoadException? last = null;
        for (var attempt = 1; attempt <= options.MaxAttempts; attempt++)
        {
            var retryDelay = Backoff(attempt);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.RequestTimeout);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Accept.ParseAdd("application/json");
                if (forceRefresh)
                {
                    request.Headers.CacheControl = new() { NoCache = true };
                    request.Headers.Pragma.ParseAdd("no-cache");
                }
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    var failure = new QuestLoadFailure(QuestLoadFailureCode.HttpStatus,
                        $"Quest endpoint returned HTTP {(int)response.StatusCode} ({response.StatusCode}).", attempt, (int)response.StatusCode);
                    var error = new QuestDataLoadException(failure);
                    if (!RetryStatus(response.StatusCode) || attempt == options.MaxAttempts) throw error;
                    last = error;
                    retryDelay = RetryAfter(response) ?? retryDelay;
                }
                else
                {
                    if (response.Content.Headers.ContentType?.MediaType is "text/html" or "application/xhtml+xml")
                        throw new QuestDataLoadException(new(QuestLoadFailureCode.InvalidJson, "Quest endpoint returned HTML instead of JSON.", attempt));
                    if (response.Content.Headers.ContentLength is long length && length > options.MaxResponseBytes)
                        throw new QuestDataLoadException(new(QuestLoadFailureCode.ResponseTooLarge,
                            $"Quest response exceeds {options.MaxResponseBytes} bytes.", attempt));
                    await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                    var raw = await QuestJSONStore.ReadBoundedAsync(stream, options.MaxResponseBytes, timeout.Token).ConfigureAwait(false);
                    timeout.Token.ThrowIfCancellationRequested();
                    var document = source.Parse(raw, questId, language, store.GetSourcePath(source.SourceKey, language, questId, uri));
                    timeout.Token.ThrowIfCancellationRequested();
                    var responseUri = QuestLoadIdentity.PublicUri(response.RequestMessage?.RequestUri ?? uri);
                    return new(document, raw, responseUri, DateTimeOffset.UtcNow, attempt);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (OperationCanceledException e)
            {
                last = new(new(QuestLoadFailureCode.Timeout, $"Quest HTTP attempt timed out (limit {options.RequestTimeout}).", attempt), e);
                if (attempt == options.MaxAttempts) throw last;
            }
            catch (HttpRequestException e)
            {
                var failure = e.StatusCode is HttpStatusCode status
                    ? new QuestLoadFailure(QuestLoadFailureCode.HttpStatus, $"Quest HTTP request failed: {e.Message}", attempt, (int)status)
                    : new QuestLoadFailure(QuestLoadFailureCode.Transport, $"Quest transport failed: {e.Message}", attempt);
                last = new(failure, e);
                if (attempt == options.MaxAttempts || (e.StatusCode.HasValue && !RetryStatus(e.StatusCode.Value))) throw last;
            }
            catch (IOException e)
            {
                last = new(new(QuestLoadFailureCode.Transport, $"Quest response stream failed: {e.Message}", attempt), e);
                if (attempt == options.MaxAttempts) throw last;
            }
            catch (QuestDataLoadException e)
            {
                // Invalid bodies, identities and permanent statuses are never retried.
                throw new QuestDataLoadException(e.Failure with { Attempts = attempt }, e);
            }
            catch (ArgumentException e)
            {
                throw new QuestDataLoadException(new(QuestLoadFailureCode.InvalidSchema,
                    $"Quest response metadata is invalid: {e.Message}", attempt), e);
            }
            await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
        }
        throw (Exception?)last ?? new InvalidOperationException("No HTTP attempt was made.");
    }

    private TimeSpan Backoff(int attempt) => TimeSpan.FromTicks((long)Math.Min(
        options.MaxRetryDelay.Ticks, options.InitialRetryDelay.Ticks * Math.Pow(2, attempt - 1)));

    private TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header is null) return null;
        var delay = header.Delta ?? (header.Date.HasValue ? header.Date.Value - DateTimeOffset.UtcNow : (TimeSpan?)null);
        if (!delay.HasValue) return null;
        return TimeSpan.FromTicks(Math.Clamp(delay.Value.Ticks, 0, options.MaxRetryAfter.Ticks));
    }

    private static bool RetryStatus(HttpStatusCode status) => (int)status is 408 or 429 or 500 or 502 or 503 or 504;
    private sealed record FetchedQuest(QuestDocument Document, byte[] Raw, Uri ResponseUri, DateTimeOffset FetchedUtc, int Attempts);
}
