using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;

namespace BetterGenshinImpact.Service.Notifier;

/// <summary>
/// Discord 机器人通知器：使用 Bot Token 调用 Discord REST API，把消息推送到指定频道。
/// 与 <see cref="DiscordWebhookNotifier"/> 的区别是使用机器人身份，
/// 可推送到机器人有权限的任意频道，不受 Webhook 是否存在的限制。
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
    private readonly string _channelId;
    private readonly string _imageFormat;
    private readonly IImageEncoder _imageEncoder;

    public DiscordBotNotifier(
        HttpClient httpClient,
        string botToken,
        string channelId,
        string imageFormat
    )
    {
        _httpClient = httpClient;
        _botToken = botToken;
        _channelId = channelId;
        _imageFormat = imageFormat.ToLower();
        // 与 DiscordWebhookNotifier 保持一致：截图编码选项来自同一个下拉框，未识别时回落到 JPEG
        _imageEncoder = imageFormat switch
        {
            nameof(DiscordWebhookNotifier.ImageEncoderEnum.Png) => new PngEncoder(),
            nameof(DiscordWebhookNotifier.ImageEncoderEnum.WebP) => new WebpEncoder(),
            _ => new JpegEncoder(),
        };
    }

    public string Name { get; set; } = "Discord Bot";

    public async Task SendAsync(BaseNotificationData content)
    {
        if (string.IsNullOrWhiteSpace(_botToken))
            throw new NotifierException("Discord 机器人 Token 未设置");

        if (string.IsNullOrWhiteSpace(_channelId))
            throw new NotifierException("Discord 机器人频道 ID 未设置");

        var url = $"{ApiBase}/channels/{_channelId.Trim()}/messages";

        var payloadJson = new Dictionary<string, object>
        {
            ["content"] = BuildContent(content),
        };

        HttpContent requestContent;

        if (content.Screenshot != null)
        {
            var fileName = $"screenshot.{_imageFormat}";
            payloadJson["attachments"] = new List<object> { new { id = 0, filename = fileName } };

            var multipart = new MultipartFormDataContent("boundary");
            using (var ms = new MemoryStream())
            {
                await content.Screenshot.SaveAsync(ms, _imageEncoder);
                var imageContent = new ByteArrayContent(ms.ToArray());
                imageContent.Headers.ContentType = MediaTypeHeaderValue.Parse(
                    $"image/{_imageFormat}"
                );
                multipart.Add(imageContent, "files[0]", fileName);
            }
            multipart.Add(JsonContent.Create(payloadJson), "payload_json");
            requestContent = multipart;
        }
        else
        {
            requestContent = JsonContent.Create(payloadJson);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = requestContent };
            request.Headers.TryAddWithoutValidation("Authorization", $"Bot {_botToken.Trim()}");
            using var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }
        catch (System.Exception ex)
        {
            Logger.LogDebug("Failed to send message to Discord via bot: {ex}", ex.Message);
            throw new System.Exception("Failed to send message to Discord via bot", ex);
        }
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
