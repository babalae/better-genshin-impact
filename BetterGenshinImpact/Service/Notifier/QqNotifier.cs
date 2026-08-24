using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace BetterGenshinImpact.Service.Notifier;

/// <summary>
/// QQ official REST notifier.
/// Pushes BetterGI events to the user's QQ private chat (C2C) via QQ Open Platform API.
/// Supports text messages and screenshot image messages (chunked upload).
/// </summary>
public sealed class QqNotifier : INotifier
{
    private static readonly ILogger<QqNotifier> Logger = App.GetLogger<QqNotifier>();

    public string Name { get; } = "QQ";

    private const string TokenUrl = "https://bots.qq.com/app/getAppAccessToken";
    private const string ApiBase = "https://api.sgroup.qq.com/v2/users/{openid}";
    private const int MaxRetry = 3;

    private readonly HttpClient _httpClient;
    private readonly string _appId;
    private readonly string _clientSecret;
    private readonly string _openId;

    /// <summary>
    /// Initializes a new instance of the <see cref="QqNotifier"/> class.
    /// </summary>
    /// <param name="httpClient">Shared HTTP client used to call the QQ Open Platform API.</param>
    /// <param name="appId">QQ Open Platform AppID.</param>
    /// <param name="clientSecret">QQ Open Platform AppSecret.</param>
    /// <param name="openId">The target user's C2C OpenID.</param>
    public QqNotifier(HttpClient httpClient, string appId, string clientSecret, string openId)
    {
        _httpClient = httpClient;
        _appId = appId;
        _clientSecret = clientSecret;
        _openId = openId;
    }

    /// <summary>
    /// Sends the notification to the user's QQ private chat.
    /// The text message is always sent first; if a screenshot is present, an image
    /// message is sent afterwards. A screenshot failure is logged and swallowed so
    /// that the already-delivered text notification is not reported as failed.
    /// </summary>
    /// <param name="content">The notification data to send.</param>
    public async Task SendAsync(BaseNotificationData content)
    {
        if (string.IsNullOrWhiteSpace(_appId))
            throw new NotifierException("QQ AppID is empty");

        if (string.IsNullOrWhiteSpace(_clientSecret))
            throw new NotifierException("QQ AppSecret is empty");

        if (string.IsNullOrWhiteSpace(_openId))
            throw new NotifierException("QQ OpenID is empty");

        try
        {
            var text = GenerateMessage(content);
            await SendTextAsync(text);

            if (content.Screenshot != null)
            {
                try
                {
                    await SendImageAsync(content.Screenshot);
                }
                catch (System.Exception ex)
                {
                    Logger.LogWarning("QQ image send failed, falling back to text-only: {ex}", ex.Message);
                }
            }
        }
        catch (NotifierException)
        {
            throw;
        }
        catch (System.Exception ex)
        {
            throw new NotifierException($"Error sending QQ message: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds the human-readable text message with a result mark and timestamp.
    /// </summary>
    private static string GenerateMessage(BaseNotificationData data)
    {
        var sb = new StringBuilder();
        var mark = data.Result switch
        {
            NotificationEventResult.Success => "\u2705",
            NotificationEventResult.Fail => "\u274C",
            _ => "\u26A0\uFE0F"
        };
        sb.Append($"[BetterGI] {mark} ");
        if (!string.IsNullOrWhiteSpace(data.Message))
            sb.Append(data.Message);
        sb.AppendLine();
        sb.Append($"\uD83D\uDD50 {data.Timestamp:yyyy-MM-dd HH:mm:ss}");
        return sb.ToString();
    }

    /// <summary>
    /// Obtains an access token from the QQ Open Platform.
    /// </summary>
    private async Task<string> GetAccessTokenAsync()
    {
        var body = JsonSerializer.Serialize(new { appId = _appId, clientSecret = _clientSecret });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl) { Content = content };
        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("access_token").GetString()!;
    }

    /// <summary>
    /// Builds an HTTP request with the QQ authorization header.
    /// </summary>
    private async Task<HttpRequestMessage> BuildAuthedRequest(HttpMethod method, string url, HttpContent? body = null)
    {
        var token = await GetAccessTokenAsync();
        var request = new HttpRequestMessage(method, url) { Content = body };
        request.Headers.Add("Authorization", $"QQBot {token}");
        return request;
    }

    /// <summary>
    /// Sends a plain text message (msg_type=0) with retry.
    /// </summary>
    private async Task SendTextAsync(string text)
    {
        await WithRetryAsync(async () =>
        {
            var body = JsonSerializer.Serialize(new { msg_type = 0, content = text });
            using var jsonContent = new StringContent(body, Encoding.UTF8, "application/json");
            using var request = await BuildAuthedRequest(HttpMethod.Post, $"{ApiBase.Replace("{openid}", _openId)}/messages", jsonContent);
            using var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
        });
    }

    /// <summary>
    /// Uploads the screenshot and sends it as a rich media image message (msg_type=7).
    /// </summary>
    private async Task SendImageAsync(Image<Rgb24> screenshot)
    {
        byte[] imageBytes;
        using (var ms = new MemoryStream())
        {
            screenshot.SaveAsJpeg(ms);
            imageBytes = ms.ToArray();
        }

        var fileInfo = await UploadImageChunkedAsync(imageBytes);

        await WithRetryAsync(async () =>
        {
            var body = JsonSerializer.Serialize(new
            {
                msg_type = 7,
                media = new { file_info = fileInfo }
            });
            using var jsonContent = new StringContent(body, Encoding.UTF8, "application/json");
            using var request = await BuildAuthedRequest(HttpMethod.Post, $"{ApiBase.Replace("{openid}", _openId)}/messages", jsonContent);
            using var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
        });
    }

    /// <summary>
    /// Uploads the image bytes via the QQ chunked upload flow and returns the file_info.
    /// The flow is: upload_prepare -> chunk PUT + part_finish -> files merge.
    /// </summary>
    private async Task<string> UploadImageChunkedAsync(byte[] imageBytes)
    {
        var baseUrl = ApiBase.Replace("{openid}", _openId);
        var fileName = "screenshot.jpg";
        var md5 = Convert.ToHexString(MD5.HashData(imageBytes)).ToLower();
        var sha1 = Convert.ToHexString(SHA1.HashData(imageBytes)).ToLower();
        var md5First10m = Convert.ToHexString(MD5.HashData(imageBytes.AsSpan(0, Math.Min(imageBytes.Length, 10002432)))).ToLower();

        var prepared = await WithRetryAsync(() => PrepareUploadAsync(baseUrl, fileName, imageBytes.Length, md5, sha1, md5First10m));

        foreach (var part in prepared.Parts)
        {
            var start = (part.Index - 1) * prepared.BlockSize;
            var end = Math.Min(start + prepared.BlockSize, imageBytes.Length);
            var chunk = imageBytes[start..end];
            var chunkMd5 = Convert.ToHexString(MD5.HashData(chunk)).ToLower();

            await WithRetryAsync(() => UploadChunkAsync(part.PresignedUrl, chunk));
            await WithRetryAsync(() => FinishChunkAsync(baseUrl, prepared.UploadId, part.Index, chunk.Length, chunkMd5));
        }

        return await WithRetryAsync(() => MergeUploadAsync(baseUrl, prepared.UploadId));
    }

    /// <summary>
    /// Calls upload_prepare to obtain the upload id, block size and presigned chunk URLs.
    /// </summary>
    private async Task<UploadPrepareResult> PrepareUploadAsync(string baseUrl, string fileName, int fileSize, string md5, string sha1, string md5First10m)
    {
        using var request = await BuildAuthedRequest(HttpMethod.Post, $"{baseUrl}/upload_prepare", new StringContent(
            JsonSerializer.Serialize(new
            {
                file_type = 1,
                file_size = fileSize.ToString(),
                file_name = fileName,
                md5,
                sha1,
                md5_10m = md5First10m
            }), Encoding.UTF8, "application/json"));
        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var uploadId = doc.RootElement.GetProperty("upload_id").GetString()!;
        var blockSize = int.Parse(doc.RootElement.GetProperty("block_size").GetString()!);
        var parts = new List<ChunkPart>();
        foreach (var part in doc.RootElement.GetProperty("parts").EnumerateArray())
        {
            parts.Add(new ChunkPart(
                part.GetProperty("index").GetInt32(),
                part.GetProperty("presigned_url").GetString()!));
        }
        return new UploadPrepareResult(uploadId, blockSize, parts);
    }

    /// <summary>
    /// Uploads a single chunk to its presigned URL.
    /// </summary>
    private async Task UploadChunkAsync(string presignedUrl, byte[] chunk)
    {
        using var putContent = new ByteArrayContent(chunk);
        putContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var putResponse = await _httpClient.PutAsync(presignedUrl, putContent);
        putResponse.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Notifies the QQ platform that a chunk has finished uploading.
    /// </summary>
    private async Task FinishChunkAsync(string baseUrl, string uploadId, int partIndex, int chunkLength, string chunkMd5)
    {
        using var request = await BuildAuthedRequest(HttpMethod.Post, $"{baseUrl}/upload_part_finish", new StringContent(
            JsonSerializer.Serialize(new
            {
                upload_id = uploadId,
                part_index = partIndex,
                block_size = chunkLength.ToString(),
                md5 = chunkMd5
            }), Encoding.UTF8, "application/json"));
        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Merges the uploaded chunks and returns the file_info.
    /// </summary>
    private async Task<string> MergeUploadAsync(string baseUrl, string uploadId)
    {
        using var request = await BuildAuthedRequest(HttpMethod.Post, $"{baseUrl}/files", new StringContent(
            JsonSerializer.Serialize(new { file_type = 1, upload_id = uploadId }), Encoding.UTF8, "application/json"));
        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("file_info").GetString()!;
    }

    private readonly record struct ChunkPart(int Index, string PresignedUrl);

    private readonly record struct UploadPrepareResult(string UploadId, int BlockSize, List<ChunkPart> Parts);

    /// <summary>
    /// Executes the given action with up to <see cref="MaxRetry"/> attempts and exponential backoff.
    /// </summary>
    private async Task WithRetryAsync(Func<Task> action)
    {
        System.Exception? lastException = null;
        for (var attempt = 0; attempt < MaxRetry; attempt++)
        {
            try
            {
                await action();
                return;
            }
            catch (System.Exception ex) when (attempt < MaxRetry - 1)
            {
                lastException = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(1500 * (attempt + 1)));
            }
        }
        if (lastException != null)
            throw lastException;
    }

    /// <summary>
    /// Executes the given function with up to <see cref="MaxRetry"/> attempts and exponential backoff.
    /// </summary>
    private async Task<T> WithRetryAsync<T>(Func<Task<T>> action)
    {
        System.Exception? lastException = null;
        for (var attempt = 0; attempt < MaxRetry; attempt++)
        {
            try
            {
                return await action();
            }
            catch (System.Exception ex) when (attempt < MaxRetry - 1)
            {
                lastException = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(1500 * (attempt + 1)));
            }
        }
        throw lastException ?? new NotifierException("QQ request failed after retries");
    }
}
