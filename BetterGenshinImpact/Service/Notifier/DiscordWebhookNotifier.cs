using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;
using System.Net.Http;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notification.Model;
using System.Collections.Generic;
using System.IO;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;

namespace BetterGenshinImpact.Service.Notifier;

public class DiscordWebhookNotifier : INotifier
{
    private static readonly ILogger<DiscordWebhookNotifier> Logger = App.GetLogger<DiscordWebhookNotifier>();

    private readonly HttpClient _httpClient;
    private readonly string _webhookUrl;
    private readonly string _username;
    private readonly string _avatarUrl;
    private readonly string _imageFormat;
    private readonly IImageEncoder _imageEncoder;

    public enum ImageEncoderEnum
    {
        Png,
        Jpeg,
        WebP
    }

    public DiscordWebhookNotifier(HttpClient httpClient, string webhookUrl, string username, string avatarUrl,
        string imageFormat)
    {
        _httpClient = httpClient;
        _webhookUrl = webhookUrl;
        _username = username;
        _avatarUrl = avatarUrl;
        _imageFormat = imageFormat.ToLower();
        _imageEncoder = imageFormat switch
        {
            nameof(ImageEncoderEnum.Png) => new PngEncoder(),
            nameof(ImageEncoderEnum.WebP) => new WebpEncoder(),
            _ => new JpegEncoder()
        };
    }

    public string Name { get; set; } = "Discord Webhook";

    public async Task SendAsync(BaseNotificationData content)
    {
        // ref: https://discord.com/developers/docs/resources/webhook#execute-webhook
        if (string.IsNullOrEmpty(_webhookUrl)) throw new NotifierException("Discord webhook URL is not set");

        var url = $"{_webhookUrl}?with_components=true";
        var data = new MultipartFormDataContent("boundary");

        var payloadJson = new
        {
            flags = 1 << 15,
            components = new List<object>(),
            username = _username,
            avatar_url = _avatarUrl,
            attachments = new List<object>(),
        };

        var components = new List<object>([
            new
            {
                type = 10,
                content = content.Message
            },
            new
            {
                type = 10,
                content = $"-# {content.Event} | {content.Result}\n-# {content.Timestamp}"
            }
        ]);

        if (content.Screenshot != null)
        {
            var fileName = $"screenshot.{_imageFormat}";
            ;
            payloadJson.attachments.Add(new
            {
                id = 0,
                filename = fileName,
            });
            components = new List<object>([
                new
                {
                    type = 9,
                    components,
                    accessory = new
                    {
                        type = 11,
                        media = new { url = $"attachment://screenshot.{_imageFormat}" },
                        description = "Screenshot"
                    }
                }
            ]);

            using (var ms = new MemoryStream())
            {
                await content.Screenshot.SaveAsync(ms, _imageEncoder);
                var imageContent = new ByteArrayContent(ms.ToArray());
                imageContent.Headers.ContentType =
                    MediaTypeHeaderValue.Parse($"image/{_imageFormat}");
                data.Add(imageContent, "files[0]", fileName);
            }
        }

        payloadJson.components.Add(new
        {
            type = 17,
            components
        });

        data.Add(JsonContent.Create(payloadJson), "payload_json");

        try
        {
            var response = await _httpClient.PostAsync(url, data);
            response.EnsureSuccessStatusCode();
        }
        catch (System.Exception ex)
        {
            Logger.LogDebug("Failed to send message to Discord: {ex}", ex.Message);
            throw new System.Exception("Failed to send message to Discord", ex);
        }
    }
}