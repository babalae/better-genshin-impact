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
    public string Name { get; } = "QQ";

    private const string TokenUrl = "https://bots.qq.com/app/getAppAccessToken";
    private const string ApiBase = "https://api.sgroup.qq.com/v2/users/{openid}";
    private const int MaxRetry = 3;

    private readonly HttpClient _httpClient;
    private readonly string _appId;
    private readonly string _clientSecret;
    private readonly string _openId;

    public QqNotifier(HttpClient httpClient, string appId, string clientSecret, string openId)
    {
        _httpClient = httpClient;
        _appId = appId;
        _clientSecret = clientSecret;
        _openId = openId;
    }

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
            // 1. Send text message
            var text = GenerateMessage(content);
            await SendTextAsync(text);

            // 2. Send image message if screenshot exists
            if (content.Screenshot != null)
            {
                try
                {
                    await SendImageAsync(content.Screenshot);
                }
                catch (System.Exception ex)
                {
                    // Image failure should not block the whole notification (text already sent)
                    throw new NotifierException($"QQ image send failed (text already sent): {ex.Message}");
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

    // ==================== Auth ====================

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

    private async Task<HttpRequestMessage> BuildAuthedRequest(HttpMethod method, string url, HttpContent? body = null)
    {
        var token = await GetAccessTokenAsync();
        var request = new HttpRequestMessage(method, url) { Content = body };
        request.Headers.Add("Authorization", $"QQBot {token}");
        return request;
    }

    // ==================== Text message ====================

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

    // ==================== Image message (chunked upload) ====================

    private async Task SendImageAsync(Image<Rgb24> screenshot)
    {
        // Convert to JPEG bytes
        byte[] imageBytes;
        using (var ms = new MemoryStream())
        {
            screenshot.SaveAsJpeg(ms);
            imageBytes = ms.ToArray();
        }

        // 1. Upload to get file_info
        var fileInfo = await UploadImageChunkedAsync(imageBytes);

        // 2. Send rich media message msg_type=7
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

    private async Task<string> UploadImageChunkedAsync(byte[] imageBytes)
    {
        var baseUrl = ApiBase.Replace("{openid}", _openId);
        var fileName = "screenshot.jpg";
        var md5 = Convert.ToHexString(MD5.HashData(imageBytes)).ToLower();
        var sha1 = Convert.ToHexString(SHA1.HashData(imageBytes)).ToLower();
        var md5First10m = Convert.ToHexString(MD5.HashData(imageBytes.AsSpan(0, Math.Min(imageBytes.Length, 10002432)))).ToLower();

        // 1. upload_prepare
        string uploadId;
        int blockSize;
        List<ChunkPart> parts;
        using (var request = await BuildAuthedRequest(HttpMethod.Post, $"{baseUrl}/upload_prepare", new StringContent(
            JsonSerializer.Serialize(new
            {
                file_type = 1,
                file_size = imageBytes.Length.ToString(),
                file_name = fileName,
                md5,
                sha1,
                md5_10m = md5First10m
            }), Encoding.UTF8, "application/json")))
        {
            using var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            uploadId = doc.RootElement.GetProperty("upload_id").GetString()!;
            blockSize = int.Parse(doc.RootElement.GetProperty("block_size").GetString()!);
            parts = new List<ChunkPart>();
            foreach (var part in doc.RootElement.GetProperty("parts").EnumerateArray())
            {
                parts.Add(new ChunkPart(
                    part.GetProperty("index").GetInt32(),
                    part.GetProperty("presigned_url").GetString()!));
            }
        }

        // 2. Chunk PUT + part_finish (index starts from 1)
        foreach (var part in parts)
        {
            var start = (part.Index - 1) * blockSize;
            var end = Math.Min(start + blockSize, imageBytes.Length);
            var chunk = imageBytes[start..end];
            var chunkMd5 = Convert.ToHexString(MD5.HashData(chunk)).ToLower();

            // PUT to presigned URL
            using (var putContent = new ByteArrayContent(chunk))
            {
                putContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                using var putResponse = await _httpClient.PutAsync(part.PresignedUrl, putContent);
                putResponse.EnsureSuccessStatusCode();
            }

            // part_finish
            using (var req = await BuildAuthedRequest(HttpMethod.Post, $"{baseUrl}/upload_part_finish", new StringContent(
                JsonSerializer.Serialize(new
                {
                    upload_id = uploadId,
                    part_index = part.Index,
                    block_size = chunk.Length.ToString(),
                    md5 = chunkMd5
                }), Encoding.UTF8, "application/json")))
            {
                using var response = await _httpClient.SendAsync(req);
                response.EnsureSuccessStatusCode();
            }
        }

        // 3. Merge to get file_info
        using (var req = await BuildAuthedRequest(HttpMethod.Post, $"{baseUrl}/files", new StringContent(
            JsonSerializer.Serialize(new { file_type = 1, upload_id = uploadId }), Encoding.UTF8, "application/json")))
        {
            using var response = await _httpClient.SendAsync(req);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("file_info").GetString()!;
        }
    }

    private readonly record struct ChunkPart(int Index, string PresignedUrl);

    // ==================== Retry helper ====================

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
}
