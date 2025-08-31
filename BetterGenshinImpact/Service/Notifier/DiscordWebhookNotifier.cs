using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Web;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;

namespace BetterGenshinImpact.Service.Notifier;

public class DiscordWebhookNotifier : INotifier
{
    private static readonly ILogger<DiscordWebhookNotifier> Logger =
        App.GetLogger<DiscordWebhookNotifier>();

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
        WebP,
    }

    public DiscordWebhookNotifier(
        HttpClient httpClient,
        string webhookUrl,
        string username,
        string avatarUrl,
        string imageFormat
    )
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
            _ => new JpegEncoder(),
        };
    }

    public string Name { get; set; } = "Discord Webhook";

    public async Task SendAsync(BaseNotificationData content)
    {
        // ref: https://discord.com/developers/docs/resources/webhook#execute-webhook
        if (string.IsNullOrEmpty(_webhookUrl))
            throw new NotifierException("Discord webhook URL is not set");

        var uriBuilder = new UriBuilder(_webhookUrl);
        var query = HttpUtility.ParseQueryString(uriBuilder.Query);
        query["with_components"] = "true";
        uriBuilder.Query = query.ToString();
        var url = uriBuilder.ToString();

        var payloadJson = new Dictionary<string, object> { ["flags"] = 1 << 15 };

        if (!string.IsNullOrWhiteSpace(_username))
            payloadJson["username"] = _username;
        if (!string.IsNullOrWhiteSpace(_avatarUrl))
            payloadJson["avatar_url"] = _avatarUrl;

        HttpContent requestContent;

        var components = new List<object>
        {
            new { type = 10, content = content.Message },
            new
            {
                type = 10,
                content = $"-# {content.Event} | {content.Result}\n-# {content.Timestamp}",
            },
        };

        if (content.Screenshot != null)
        {
            var fileName = $"screenshot.{_imageFormat}";
            payloadJson["attachments"] = new List<object> { new { id = 0, filename = fileName } };
            components = new List<object>
            {
                new
                {
                    type = 9,
                    components,
                    accessory = new
                    {
                        type = 11,
                        media = new { url = $"attachment://{fileName}" },
                        description = "Screenshot",
                    },
                },
            };
            payloadJson["components"] = new List<object> { new { type = 17, components } };

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
            payloadJson["components"] = new List<object> { new { type = 17, components } };
            requestContent = JsonContent.Create(payloadJson);
        }

        try
        {
            var response = await _httpClient.PostAsync(url, requestContent);
            response.EnsureSuccessStatusCode();
        }
        catch (System.Exception ex)
        {
            Logger.LogDebug("Failed to send message to Discord: {ex}", ex.Message);
            throw new System.Exception("Failed to send message to Discord", ex);
        }
    }
}
