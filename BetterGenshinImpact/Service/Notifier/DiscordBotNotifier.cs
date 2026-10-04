using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace BetterGenshinImpact.Service.Notifier;

/// <summary>
/// Discord 机器人通知器：使用 Bot Token 调用 Discord REST API，把消息推送到多个目标。
/// 目标可以是频道，也可以是使用者私讯，两者可以混用。
/// 与 <see cref="DiscordWebhookNotifier"/> 的区别是使用机器人身份，
/// 可推送到机器人有权限的任意频道，且支援私讯。
/// ref: https://discord.com/developers/docs/resources/message#create-message
/// </summary>
public class DiscordBotNotifier : INotifier
{
    private static readonly ILogger<DiscordBotNotifier> Logger = App.GetLogger<DiscordBotNotifier>();

    private const string ApiBase = "https://discord.com/api/v10";

    /// <summary>
    /// Discord 单条消息正文长度上限（字符），超出会被 API 拒绝。
    /// </summary>
    public const int MaxContentLength = 2000;

    private const string TruncatedSuffix = "…";

    private readonly HttpClient _httpClient;
    private readonly string _botToken;
    private readonly List<TargetEntry> _targets;
    private readonly string _imageFormat;
    private readonly IImageEncoder _imageEncoder;
    private readonly DiscordBotApiClient _apiClient;

    /// <summary>
    /// 使用者 ID → 私讯频道 ID 缓存。Discord 对同一使用者只会有一个私讯频道，可以长期复用。
    /// </summary>
    private readonly Dictionary<string, string> _directMessageChannels = new(StringComparer.Ordinal);

    private readonly SemaphoreSlim _directMessageLock = new(1, 1);

    /// <summary>
    /// 推送目标快照。通知器重建时重新读取配置，因此这里只保存必要字段。
    /// </summary>
    private sealed record TargetEntry(string Type, string Id, string DisplayName);

    public DiscordBotNotifier(
        HttpClient httpClient,
        string botToken,
        IEnumerable<DiscordBotTarget> targets,
        string imageFormat
    )
    {
        _httpClient = httpClient;
        _botToken = botToken?.Trim() ?? string.Empty;
        _targets = targets?
            .Where(target => !string.IsNullOrWhiteSpace(target?.Id))
            .Select(target => new TargetEntry(target.Type, target.Id.Trim(), target.DisplayName))
            .ToList() ?? [];

        // 与 DiscordWebhookNotifier 保持一致：截图编码选项来自同一个下拉框，未识别时回落到 JPEG
        var format = string.IsNullOrWhiteSpace(imageFormat)
            ? nameof(DiscordWebhookNotifier.ImageEncoderEnum.Jpeg)
            : imageFormat;
        _imageFormat = format.ToLower();
        _imageEncoder = format switch
        {
            nameof(DiscordWebhookNotifier.ImageEncoderEnum.Png) => new PngEncoder(),
            nameof(DiscordWebhookNotifier.ImageEncoderEnum.WebP) => new WebpEncoder(),
            _ => new JpegEncoder(),
        };

        _apiClient = new DiscordBotApiClient(httpClient, _botToken);
    }

    public string Name { get; set; } = "Discord Bot";

    /// <summary>
    /// 逐个目标发送。单个目标失败只记警告并继续，全部失败才向调用方抛异常，
    /// 避免通知管理器误报成功，也避免一个坏目标阻断其他目标。
    /// </summary>
    public async Task SendAsync(BaseNotificationData content)
    {
        if (string.IsNullOrWhiteSpace(_botToken))
            throw new NotifierException("Discord 机器人 Token 未设置");

        if (_targets.Count == 0)
            throw new NotifierException("尚未设置 Discord 推送目标");

        var contentJson = BuildContent(content);

        // 截图只编码一次，多个目标共用，避免重复压缩
        var attachment = content.Screenshot == null ? null : await EncodeScreenshotAsync(content.Screenshot);

        var successCount = 0;
        var lastError = string.Empty;

        foreach (var target in _targets)
        {
            try
            {
                var channelId = DiscordBotTargetTypes.IsDirectMessage(target.Type)
                    ? await ResolveDirectMessageChannelAsync(target.Id)
                    : target.Id;
                await PostMessageAsync(channelId, contentJson, attachment);
                successCount++;
            }
            catch (System.Exception ex)
            {
                lastError = ex.Message;
                Logger.LogWarning("Discord 推送失败（目标 {target}），跳过该目标: {ex}", DescribeTarget(target), ex.Message);
            }
        }

        if (successCount == 0)
            throw new NotifierException($"Discord 推送失败：{_targets.Count} 个目标全部失败，最后错误：{lastError}");
    }

    /// <summary>
    /// 解析使用者的私讯频道 ID，带缓存与并发保护（双重检查 + 信号量）。
    /// </summary>
    private async Task<string> ResolveDirectMessageChannelAsync(string userId)
    {
        if (_directMessageChannels.TryGetValue(userId, out var cached))
        {
            return cached;
        }

        await _directMessageLock.WaitAsync();
        try
        {
            if (_directMessageChannels.TryGetValue(userId, out cached))
            {
                return cached;
            }

            var channelId = await _apiClient.CreateDirectMessageChannelAsync(userId);
            if (string.IsNullOrEmpty(channelId))
            {
                throw new NotifierException($"无法解析使用者 {userId} 的私讯频道");
            }

            _directMessageChannels[userId] = channelId;
            return channelId;
        }
        finally
        {
            _directMessageLock.Release();
        }
    }

    /// <summary>
    /// 把消息发送到指定频道（频道 ID 或私讯频道 ID 都适用）。
    /// </summary>
    private async Task PostMessageAsync(string channelId, string contentJson, byte[]? attachment)
    {
        var payloadJson = new Dictionary<string, object>
        {
            ["content"] = contentJson,
        };

        HttpContent requestContent;

        if (attachment != null)
        {
            var fileName = $"screenshot.{_imageFormat}";
            payloadJson["attachments"] = new List<object> { new { id = 0, filename = fileName } };

            var multipart = new MultipartFormDataContent("boundary");
            var imageContent = new ByteArrayContent(attachment);
            imageContent.Headers.ContentType = MediaTypeHeaderValue.Parse($"image/{_imageFormat}");
            multipart.Add(imageContent, "files[0]", fileName);
            multipart.Add(JsonContent.Create(payloadJson), "payload_json");
            requestContent = multipart;
        }
        else
        {
            requestContent = JsonContent.Create(payloadJson);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/channels/{channelId}/messages")
        {
            Content = requestContent,
        };
        request.Headers.TryAddWithoutValidation("Authorization", $"Bot {_botToken}");

        using var response = await _httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        var detail = DiscordBotApiClient.ExtractErrorMessage(body) ?? response.ReasonPhrase ?? "未知错误";
        throw new NotifierException($"Discord 推送失败 ({(int)response.StatusCode})：{detail}");
    }

    private async Task<byte[]> EncodeScreenshotAsync(Image<Rgb24> screenshot)
    {
        using var ms = new MemoryStream();
        await screenshot.SaveAsync(ms, _imageEncoder);
        return ms.ToArray();
    }

    private static string DescribeTarget(TargetEntry target)
    {
        return string.IsNullOrWhiteSpace(target.DisplayName) ? target.Id : target.DisplayName;
    }

    /// <summary>
    /// 组装消息正文：正文、事件/结果、时间戳三段。
    /// Discord 对单条消息正文有 2000 字符上限，正文过长时截断正文并保留页脚信息。
    /// </summary>
    public static string BuildContent(BaseNotificationData content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var footer = $"-# {content.Event} | {content.Result}\n-# {content.Timestamp}";
        var message = content.Message?.Trim() ?? string.Empty;

        // 预留页脚长度，正文超长时截断，保证整条消息不超过 Discord 上限
        var budget = MaxContentLength - footer.Length - 1;
        if (budget < 0)
        {
            budget = 0;
        }

        if (message.Length > budget)
        {
            message = budget > TruncatedSuffix.Length
                ? message[..(budget - TruncatedSuffix.Length)] + TruncatedSuffix
                : message[..budget];
        }

        var body = string.IsNullOrEmpty(message) ? footer : $"{message}\n{footer}";
        return body.Length > MaxContentLength ? body[..MaxContentLength] : body;
    }
}
