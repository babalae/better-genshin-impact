using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Service.Notifier;

public class FeishuNotifier : INotifier, IDisposable
{
    private const int DefaultTokenExpireSeconds = 7200;
    private const int TokenRefreshSafetySeconds = 300;

    private static readonly string _accessTokenUrl = "https://open.feishu.cn/open-apis/auth/v3/tenant_access_token/internal";
    private static readonly string _uploadImageUrl = "https://open.feishu.cn/open-apis/im/v1/images";

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _tokenSemaphore = new(1, 1);
    private string? _tenantAccessToken;
    private DateTimeOffset _tenantAccessTokenExpiresAt = DateTimeOffset.MinValue;

    public string Name { get; set; } = "Feishu";

    public string Endpoint { get; set; }

    public string AppId { get; set; }

    public string AppSecret { get; set; }

    public FeishuNotifier(HttpClient httpClient, string endpoint = "", string appId = "", string appSecret = "")
    {
        _httpClient = httpClient;
        Endpoint = endpoint;
        AppId = appId;
        AppSecret = appSecret;
    }

    public async Task SendAsync(BaseNotificationData content)
    {
        if (string.IsNullOrEmpty(Endpoint))
        {
            throw new NotifierException("Feishu webhook endpoint is not set");
        }

        try
        {
            string? imageKey = null;
            if (content.Screenshot != null && HasAppCredentials())
            {
                imageKey = await UploadImageAsync(content.Screenshot, await GetAccessTokenAsync());
            }

            using var requestContent = CreateCardContent(content, imageKey);
            using var response = await _httpClient.PostAsync(Endpoint, requestContent);
            await EnsureFeishuSuccessAsync(response, "Feishu webhook");
        }
        catch (NotifierException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new NotifierException($"Error sending Feishu webhook: {ex.Message}");
        }
    }

    private bool HasAppCredentials()
    {
        return !string.IsNullOrWhiteSpace(AppId) && !string.IsNullOrWhiteSpace(AppSecret);
    }

    private static StringContent CreateCardContent(BaseNotificationData notificationData, string? imageKey)
    {
        var (template, resultText) = GetResultStyle(notificationData.Result);
        var elements = new List<object>
        {
            new
            {
                tag = "markdown",
                content = CreateMarkdownContent(notificationData)
            }
        };

        if (!string.IsNullOrEmpty(imageKey))
        {
            elements.Add(new
            {
                tag = "img",
                img_key = imageKey,
                alt = new
                {
                    tag = "plain_text",
                    content = "BetterGI 通知截图"
                },
                scale_type = "fit_horizontal",
                preview = true
            });
        }

        var card = new
        {
            schema = "2.0",
            config = new
            {
                width_mode = "compact"
            },
            header = new
            {
                title = new
                {
                    tag = "plain_text",
                    content = GetEventTitle(notificationData.Event)
                },
                subtitle = new
                {
                    tag = "plain_text",
                    content = $"BetterGI · {notificationData.Timestamp:yyyy-MM-dd HH:mm:ss}"
                },
                template,
                text_tag_list = new[]
                {
                    new
                    {
                        tag = "text_tag",
                        text = new
                        {
                            tag = "plain_text",
                            content = resultText
                        },
                        color = template
                    }
                }
            },
            body = new
            {
                padding = "12px 12px 20px 12px",
                elements
            }
        };

        var payload = new
        {
            msg_type = "interactive",
            card
        };
        return new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
    }

    private static string CreateMarkdownContent(BaseNotificationData notificationData)
    {
        var message = string.IsNullOrWhiteSpace(notificationData.Message)
            ? "无详细内容"
            : EscapeHtml(notificationData.Message);
        return $"**时间：** {notificationData.Timestamp:yyyy-MM-dd HH:mm:ss}\n\n**内容**\n\n{message}";
    }

    private static string EscapeHtml(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&#60;")
            .Replace(">", "&#62;");
    }

    private static string GetEventTitle(string eventCode)
    {
        if (string.IsNullOrWhiteSpace(eventCode))
        {
            return "BetterGI 通知";
        }

        var notificationEvent = NotificationEvent.GetAll().FirstOrDefault(item =>
            string.Equals(item.Code, eventCode, StringComparison.OrdinalIgnoreCase));
        return notificationEvent?.Msg ?? eventCode;
    }

    private static (string Template, string ResultText) GetResultStyle(NotificationEventResult result)
    {
        return result switch
        {
            NotificationEventResult.Fail => ("red", "失败"),
            NotificationEventResult.PartialSuccess => ("orange", "部分成功"),
            _ => ("green", "正常")
        };
    }

    private async Task<string> GetAccessTokenAsync()
    {
        if (TryGetCachedAccessToken(out var cachedToken))
        {
            return cachedToken;
        }

        await _tokenSemaphore.WaitAsync();
        try
        {
            if (TryGetCachedAccessToken(out cachedToken))
            {
                return cachedToken;
            }

            using var requestContent = new StringContent(
                JsonSerializer.Serialize(new
                {
                    app_id = AppId,
                    app_secret = AppSecret
                }),
                Encoding.UTF8,
                "application/json");
            using var response = await _httpClient.PostAsync(_accessTokenUrl, requestContent);
            var responseBody = await EnsureFeishuSuccessAsync(response, "Feishu access token");

            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            if (!root.TryGetProperty("tenant_access_token", out var tokenElement) ||
                string.IsNullOrWhiteSpace(tokenElement.GetString()))
            {
                throw new NotifierException("Feishu access token not found");
            }

            var expireSeconds = root.TryGetProperty("expire", out var expireElement) &&
                                expireElement.TryGetInt32(out var configuredExpireSeconds)
                ? configuredExpireSeconds
                : DefaultTokenExpireSeconds;
            var cacheSeconds = Math.Max(60, expireSeconds - TokenRefreshSafetySeconds);
            _tenantAccessToken = tokenElement.GetString();
            _tenantAccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(cacheSeconds);
            return _tenantAccessToken!;
        }
        finally
        {
            _tokenSemaphore.Release();
        }
    }

    private bool TryGetCachedAccessToken(out string token)
    {
        if (!string.IsNullOrEmpty(_tenantAccessToken) && DateTimeOffset.UtcNow < _tenantAccessTokenExpiresAt)
        {
            token = _tenantAccessToken;
            return true;
        }

        token = string.Empty;
        return false;
    }

    private async Task<string> UploadImageAsync(Image<Rgb24> image, string accessToken)
    {
        using var memoryStream = new MemoryStream();
        await image.SaveAsPngAsync(memoryStream);
        memoryStream.Position = 0;

        using var multipartContent = new MultipartFormDataContent();
        using var imageContent = new StreamContent(memoryStream);
        imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        multipartContent.Add(imageContent, "image", "image.png");
        multipartContent.Add(new StringContent("message"), "image_type");

        using var request = new HttpRequestMessage(HttpMethod.Post, _uploadImageUrl)
        {
            Content = multipartContent
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request);
        var responseBody = await EnsureFeishuSuccessAsync(response, "Feishu upload image");

        using var document = JsonDocument.Parse(responseBody);
        if (!document.RootElement.TryGetProperty("data", out var dataElement) ||
            !dataElement.TryGetProperty("image_key", out var imageKeyElement) ||
            string.IsNullOrWhiteSpace(imageKeyElement.GetString()))
        {
            throw new NotifierException("Feishu upload image not found image key");
        }

        return imageKeyElement.GetString()!;
    }

    private static async Task<string> EnsureFeishuSuccessAsync(HttpResponseMessage response, string operation)
    {
        var responseBody = response.Content == null
            ? string.Empty
            : await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new NotifierException($"{operation} failed with status code {(int)response.StatusCode}: {responseBody}");
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            if (root.TryGetProperty("code", out var codeElement) &&
                codeElement.TryGetInt32(out var code) &&
                code != 0)
            {
                var message = root.TryGetProperty("msg", out var messageElement)
                    ? messageElement.GetString()
                    : null;
                throw new NotifierException($"{operation} failed with code {code}: {message}");
            }
        }
        catch (JsonException ex)
        {
            throw new NotifierException($"{operation} returned an invalid JSON response: {ex.Message}");
        }

        return responseBody;
    }

    public void Dispose()
    {
        _tokenSemaphore.Dispose();
    }
}
