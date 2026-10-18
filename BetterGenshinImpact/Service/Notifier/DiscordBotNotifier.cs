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
/// Discord 机器人消息格式。
/// </summary>
public static class DiscordBotMessageFormats
{
    /// <summary>
    /// 纯文本消息
    /// </summary>
    public const string Plain = "Plain";

    /// <summary>
    /// 嵌入（embed）卡片
    /// </summary>
    public const string Embed = "Embed";

    /// <summary>
    /// 判断配置值是否为嵌入卡片格式。配置可能来自旧版本或被手工修改，因此忽略大小写。
    /// </summary>
    public static bool IsEmbed(string? format)
    {
        return string.Equals(format, Embed, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Discord 机器人通知器：使用 Bot Token 调用 Discord REST API，把消息推送到多个目标。
/// 目标可以是频道，也可以是用户私信，两者可以混用。
/// 与 <see cref="DiscordWebhookNotifier"/> 的区别是使用机器人身份，
/// 可推送到机器人有权限的任意频道，且支持私信。
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

    /// <summary>
    /// Discord 嵌入描述长度上限（字符）。
    /// </summary>
    public const int MaxEmbedDescriptionLength = 4096;

    /// <summary>
    /// Discord 嵌入页脚长度上限（字符）。
    /// </summary>
    public const int MaxEmbedFooterLength = 2048;

    /// <summary>
    /// 嵌入卡片左侧色条颜色。
    /// </summary>
    private const int EmbedColor = 0x3B82F6;

    private const string TruncatedSuffix = "…";

    private readonly HttpClient _httpClient;
    private readonly string _botToken;
    private readonly List<TargetEntry> _targets;
    private readonly string _imageFormat;
    private readonly IImageEncoder _imageEncoder;
    private readonly DiscordBotApiClient _apiClient;

    /// <summary>
    /// 是否使用嵌入卡片发送。嵌入与纯文本用不同的字段，不能同时使用。
    /// </summary>
    private readonly bool _useEmbed;

    /// <summary>
    /// 用户 ID → 私信频道 ID 缓存。Discord 对同一用户只会有一个私信频道，可以长期复用。
    /// </summary>
    private readonly Dictionary<string, string> _directMessageChannels = new(StringComparer.Ordinal);

    private readonly SemaphoreSlim _directMessageLock = new(1, 1);

    /// <summary>
    /// 推送目标快照。通知器重建时重新读取配置，因此这里只保存必要字段。
    /// </summary>
    private sealed record TargetEntry(string Type, string Id, string DisplayName);

    /// <summary>
    /// 按配置创建通知器：过滤掉没有 ID 的目标，并按截图编码选项准备编码器。
    /// </summary>
    public DiscordBotNotifier(
        HttpClient httpClient,
        string botToken,
        IEnumerable<DiscordBotTarget> targets,
        string messageFormat,
        string imageFormat
    )
    {
        _httpClient = httpClient;
        _botToken = botToken?.Trim() ?? string.Empty;
        _useEmbed = DiscordBotMessageFormats.IsEmbed(messageFormat);
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

        // 截图只编码一次，多个目标共用，避免重复压缩
        var fileName = content.Screenshot == null ? null : $"screenshot.{_imageFormat}";
        var attachment = content.Screenshot == null ? null : await EncodeScreenshotAsync(content.Screenshot);
        var payload = BuildPayload(content, fileName);

        var successCount = 0;
        var lastError = string.Empty;

        foreach (var target in _targets)
        {
            try
            {
                var channelId = DiscordBotTargetTypes.IsDirectMessage(target.Type)
                    ? await ResolveDirectMessageChannelAsync(target.Id)
                    : target.Id;
                await PostMessageAsync(channelId, payload, attachment, fileName);
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
    /// 解析用户的私信频道 ID，带缓存与并发保护（双重检查 + 信号量）。
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
                throw new NotifierException($"无法解析用户 {userId} 的私信频道");
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
    /// 把消息发送到指定频道（频道 ID 或私信频道 ID 都适用）。
    /// </summary>
    private async Task PostMessageAsync(string channelId, Dictionary<string, object> payload, byte[]? attachment, string? fileName)
    {
        HttpContent requestContent;

        if (attachment != null && fileName != null)
        {
            var multipart = new MultipartFormDataContent("boundary");
            var imageContent = new ByteArrayContent(attachment);
            imageContent.Headers.ContentType = MediaTypeHeaderValue.Parse($"image/{_imageFormat}");
            multipart.Add(imageContent, "files[0]", fileName);
            multipart.Add(JsonContent.Create(payload), "payload_json");
            requestContent = multipart;
        }
        else
        {
            requestContent = JsonContent.Create(payload);
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

    /// <summary>
    /// 按配置的编码器把截图编码为字节数组。
    /// </summary>
    private async Task<byte[]> EncodeScreenshotAsync(Image<Rgb24> screenshot)
    {
        using var ms = new MemoryStream();
        await screenshot.SaveAsync(ms, _imageEncoder);
        return ms.ToArray();
    }

    /// <summary>
    /// 日志里使用的目标名称：优先显示名，没有则退回 ID。
    /// </summary>
    private static string DescribeTarget(TargetEntry target)
    {
        return string.IsNullOrWhiteSpace(target.DisplayName) ? target.Id : target.DisplayName;
    }

    /// <summary>
    /// 组装消息体。纯文本用 content 字段，嵌入用 embeds 字段，两者不能同时出现在同一条消息里。
    /// </summary>
    private Dictionary<string, object> BuildPayload(BaseNotificationData content, string? fileName)
    {
        var payload = _useEmbed
            ? BuildEmbedPayload(content, fileName)
            : new Dictionary<string, object> { ["content"] = BuildContent(content) };

        if (fileName != null)
        {
            payload["attachments"] = new List<object> { new { id = 0, filename = fileName } };
        }

        return payload;
    }

    /// <summary>
    /// 组装嵌入卡片。嵌入有独立的长度上限，正文与页脚各自截断。
    /// </summary>
    private static Dictionary<string, object> BuildEmbedPayload(BaseNotificationData content, string? fileName)
    {
        var embed = new Dictionary<string, object>
        {
            ["description"] = BuildEmbedDescription(content),
            ["color"] = EmbedColor,
            ["footer"] = new { text = BuildFooter(content) },
            ["timestamp"] = content.Timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
        };

        // 嵌入的图片通过 attachment:// 引用同一条消息里的附件
        if (fileName != null)
        {
            embed["image"] = new { url = $"attachment://{fileName}" };
        }

        return new Dictionary<string, object> { ["embeds"] = new List<object> { embed } };
    }

    /// <summary>
    /// 嵌入正文。嵌入至少要有一个非空字段，正文为空时用事件/结果兜底，避免被 Discord 拒绝。
    /// </summary>
    private static string BuildEmbedDescription(BaseNotificationData content)
    {
        var message = content.Message?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(message))
        {
            return BuildFooter(content);
        }

        return message.Length > MaxEmbedDescriptionLength
            ? message[..(MaxEmbedDescriptionLength - TruncatedSuffix.Length)] + TruncatedSuffix
            : message;
    }

    /// <summary>
    /// 组装页脚文本（事件 | 结果），超出上限时截断。
    /// </summary>
    private static string BuildFooter(BaseNotificationData content)
    {
        var footer = $"{content.Event} | {content.Result}";
        return footer.Length > MaxEmbedFooterLength ? footer[..MaxEmbedFooterLength] : footer;
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
