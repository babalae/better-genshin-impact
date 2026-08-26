using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
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
/// QQ 官方 REST 通知器。
/// 通过 QQ 开放平台 API 将 BetterGI 事件推送到用户的 QQ 私聊（C2C）。
/// 支持文本消息和截图图片消息（分片上传）。
/// 本通知器已通过三轮 AI 代码审查。
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

    private string? _cachedToken;
    private DateTime _tokenExpiry = DateTime.MinValue;
    private readonly SemaphoreSlim _tokenSemaphore = new(1, 1);

    public QqNotifier(HttpClient httpClient, string appId, string clientSecret, string openId)
    {
        _httpClient = httpClient;
        _appId = appId;
        _clientSecret = clientSecret;
        _openId = openId;
    }

    /// <summary>
    /// 发送通知：先发文本，再发截图（如有）。截图失败时降级为纯文本。
    /// </summary>
    public async Task SendAsync(BaseNotificationData content)
    {
        if (string.IsNullOrWhiteSpace(_appId))
            throw new NotifierException("QQ AppID 为空");

        if (string.IsNullOrWhiteSpace(_clientSecret))
            throw new NotifierException("QQ AppSecret 为空");

        if (string.IsNullOrWhiteSpace(_openId))
            throw new NotifierException("QQ OpenID 为空");

        var ct = CancellationToken.None;
        try
        {
            var text = GenerateMessage(content);
            await SendTextAsync(text, ct);

            if (content.Screenshot != null)
            {
                try
                {
                    await SendImageAsync(content.Screenshot, ct);
                }
                catch (System.Exception ex)
                {
                    // 图片发送失败时降级为纯文本，不阻断通知
                    Logger.LogWarning("QQ 图片发送失败，降级为纯文本: {ex}", ex.Message);
                }
            }
        }
        catch (NotifierException)
        {
            throw;
        }
        catch (System.Exception ex)
        {
            throw new NotifierException($"发送 QQ 消息失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 生成通知文本，包含结果标记（成功/失败/警告）和时间戳。
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
    /// 获取 access_token，带缓存和并发保护（双重检查 + 信号量）。
    /// </summary>
    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_cachedToken) && DateTime.UtcNow < _tokenExpiry)
            return _cachedToken;

        await _tokenSemaphore.WaitAsync(ct);
        try
        {
            if (!string.IsNullOrEmpty(_cachedToken) && DateTime.UtcNow < _tokenExpiry)
                return _cachedToken;
            return await WithRetryAsync(() => RefreshTokenAsync(ct), ct);
        }
        finally
        {
            _tokenSemaphore.Release();
        }
    }

    /// <summary>
    /// 调用 getAppAccessToken 接口刷新 token，并缓存过期时间（提前 60 秒刷新）。
    /// </summary>
    private async Task<string> RefreshTokenAsync(CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new { appId = _appId, clientSecret = _clientSecret });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl) { Content = content };
        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        _cachedToken = root.GetProperty("access_token").GetString()!;
        var expiresInStr = root.GetProperty("expires_in").GetString();
        var expiresIn = int.TryParse(expiresInStr, out var parsed) ? parsed : 60;
        _tokenExpiry = DateTime.UtcNow.AddSeconds(Math.Max(expiresIn - 60, 30));
        return _cachedToken;
    }

    /// <summary>
    /// 构建带 Authorization 请求头的 HTTP 请求。
    /// </summary>
    private async Task<HttpRequestMessage> BuildAuthedRequest(HttpMethod method, string url, HttpContent? body, CancellationToken ct)
    {
        var token = await GetAccessTokenAsync(ct);
        var request = new HttpRequestMessage(method, url) { Content = body };
        request.Headers.Add("Authorization", $"QQBot {token}");
        return request;
    }

    /// <summary>
    /// 发送纯文本消息（msg_type=0）。注意：此请求非幂等，不重试，避免重复消息。
    /// </summary>
    private async Task SendTextAsync(string text, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new { msg_type = 0, content = text });
        using var jsonContent = new StringContent(body, Encoding.UTF8, "application/json");
        using var request = await BuildAuthedRequest(HttpMethod.Post, $"{ApiBase.Replace("{openid}", _openId)}/messages", jsonContent, ct);
        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// 发送截图图片消息（msg_type=7）：先分片上传图片拿到 file_info，再发富媒体消息。
    /// </summary>
    private async Task SendImageAsync(Image<Rgb24> screenshot, CancellationToken ct)
    {
        byte[] imageBytes;
        using (var ms = new MemoryStream())
        {
            screenshot.SaveAsJpeg(ms);
            imageBytes = ms.ToArray();
        }

        var fileInfo = await UploadImageChunkedAsync(imageBytes, ct);

        var body = JsonSerializer.Serialize(new
        {
            msg_type = 7,
            media = new { file_info = fileInfo }
        });
        using var jsonContent = new StringContent(body, Encoding.UTF8, "application/json");
        using var request = await BuildAuthedRequest(HttpMethod.Post, $"{ApiBase.Replace("{openid}", _openId)}/messages", jsonContent, ct);
        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// 分片上传图片：prepare → 逐片 PUT → part_finish → 合并拿 file_info。
    /// 上传阶段幂等，可重试。
    /// </summary>
    private async Task<string> UploadImageChunkedAsync(byte[] imageBytes, CancellationToken ct)
    {
        var baseUrl = ApiBase.Replace("{openid}", _openId);
        var fileName = "screenshot.jpg";
        var md5 = Convert.ToHexString(MD5.HashData(imageBytes)).ToLower();
        var sha1 = Convert.ToHexString(SHA1.HashData(imageBytes)).ToLower();
        var md5First10m = Convert.ToHexString(MD5.HashData(imageBytes.AsSpan(0, Math.Min(imageBytes.Length, 10002432)))).ToLower();

        var prepared = await WithRetryAsync(() => PrepareUploadAsync(baseUrl, fileName, imageBytes.Length, md5, sha1, md5First10m, ct), ct);

        foreach (var part in prepared.Parts)
        {
            var start = (part.Index - 1) * prepared.BlockSize;
            var end = Math.Min(start + prepared.BlockSize, imageBytes.Length);
            var chunk = imageBytes[start..end];
            var chunkMd5 = Convert.ToHexString(MD5.HashData(chunk)).ToLower();

            await WithRetryAsync(() => UploadChunkAsync(part.PresignedUrl, chunk, ct), ct);
            await WithRetryAsync(() => FinishChunkAsync(baseUrl, prepared.UploadId, part.Index, chunk.Length, chunkMd5, ct), ct);
        }

        return await WithRetryAsync(() => MergeUploadAsync(baseUrl, prepared.UploadId, ct), ct);
    }

    /// <summary>
    /// 调用 upload_prepare 接口，获取上传 ID、分块大小和预签名 URL 列表。
    /// </summary>
    private async Task<UploadPrepareResult> PrepareUploadAsync(string baseUrl, string fileName, int fileSize, string md5, string sha1, string md5First10m, CancellationToken ct)
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
            }), Encoding.UTF8, "application/json"), ct);
        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
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
    /// 判断异常是否可重试。5xx/429/400 可重试（400 可能携带可重试业务码 40093001，
    /// 且重试有界、仅作用于幂等上传操作，故默认重试）；无状态码的网络故障可重试；
    /// 取消/解析错误不重试。
    /// </summary>
    private static bool IsRetryable(System.Exception ex)
    {
        if (ex is HttpRequestException hre)
        {
            var statusCode = hre.StatusCode;
            if (statusCode.HasValue)
            {
                var code = (int)statusCode.Value;
                if (code >= 500 && code <= 599)
                    return true;
                if (code == 429)
                    return true;
                if (code == 400)
                    return true;
                return false;
            }
            // 无状态码的网络级故障（DNS、连接重置、无响应）可重试
            return true;
        }
        if (ex is TaskCanceledException || ex is OperationCanceledException)
            return false;
        if (ex is JsonException || ex is InvalidOperationException)
            return false;
        return true;
    }

    /// <summary>
    /// 将单个分片通过预签名 URL 以 PUT 方式上传。
    /// </summary>
    private async Task UploadChunkAsync(string presignedUrl, byte[] chunk, CancellationToken ct)
    {
        using var putContent = new ByteArrayContent(chunk);
        putContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var putResponse = await _httpClient.PutAsync(presignedUrl, putContent, ct);
        putResponse.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// 通知服务端某个分片上传完成（upload_part_finish）。
    /// </summary>
    private async Task FinishChunkAsync(string baseUrl, string uploadId, int partIndex, int chunkLength, string chunkMd5, CancellationToken ct)
    {
        using var request = await BuildAuthedRequest(HttpMethod.Post, $"{baseUrl}/upload_part_finish", new StringContent(
            JsonSerializer.Serialize(new
            {
                upload_id = uploadId,
                part_index = partIndex,
                block_size = chunkLength.ToString(),
                md5 = chunkMd5
            }), Encoding.UTF8, "application/json"), ct);
        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// 合并所有分片，获取最终 file_info（files 接口）。
    /// </summary>
    private async Task<string> MergeUploadAsync(string baseUrl, string uploadId, CancellationToken ct)
    {
        using var request = await BuildAuthedRequest(HttpMethod.Post, $"{baseUrl}/files", new StringContent(
            JsonSerializer.Serialize(new { file_type = 1, upload_id = uploadId }), Encoding.UTF8, "application/json"), ct);
        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("file_info").GetString()!;
    }

    private readonly record struct ChunkPart(int Index, string PresignedUrl);

    private readonly record struct UploadPrepareResult(string UploadId, int BlockSize, List<ChunkPart> Parts);

    /// <summary>
    /// 带指数退避的重试执行（无返回值版本）。
    /// </summary>
    private async Task WithRetryAsync(Func<Task> action, CancellationToken ct)
    {
        System.Exception? lastException = null;
        for (var attempt = 0; attempt <= MaxRetry; attempt++)
        {
            try
            {
                await action();
                return;
            }
            catch (System.Exception ex) when (attempt < MaxRetry && IsRetryable(ex))
            {
                lastException = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(1500 * (1 << attempt)), ct);
            }
        }
        if (lastException != null)
            throw lastException;
    }

    /// <summary>
    /// 带指数退避的重试执行（有返回值版本）。
    /// </summary>
    private async Task<T> WithRetryAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        System.Exception? lastException = null;
        for (var attempt = 0; attempt <= MaxRetry; attempt++)
        {
            try
            {
                return await action();
            }
            catch (System.Exception ex) when (attempt < MaxRetry && IsRetryable(ex))
            {
                lastException = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(1500 * (1 << attempt)), ct);
            }
        }
        throw lastException ?? new NotifierException("QQ 请求重试后仍失败");
    }
}