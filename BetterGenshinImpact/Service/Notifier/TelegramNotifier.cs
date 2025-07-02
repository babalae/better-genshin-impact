using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace BetterGenshinImpact.Service.Notifier;

public class TelegramNotifier : INotifier, IDisposable
{
    // 默认Telegram API的标准URL前缀
    private const string DefaultApiUrl = "https://api.telegram.org/bot";

    private static readonly JsonSerializerOptions JsonOptions = new()
        { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    /// <summary>
    ///     Telegram机器人Token
    /// </summary>
    public string TelegramBotToken { get; set; }

    /// <summary>
    ///     Telegram聊天ID
    /// </summary>
    public string TelegramChatId { get; set; }

    /// <summary>
    ///     Telegram API基础URL
    /// </summary>
    public string TelegramApiBaseUrl { get; set; }

    /// <summary>
    ///     通知器名称
    /// </summary>
    public string Name { get; set; } = "Telegram";

    /// <summary>
    ///     创建一个新的Telegram通知器实例
    /// </summary>
    /// <param name="httpClient">可选的HttpClient，如果不提供则创建新的</param>
    /// <param name="telegramBotToken">Telegram机器人Token</param>
    /// <param name="telegramChatId">Telegram聊天ID</param>
    /// <param name="telegramApiBaseUrl">自定义Telegram API基础URL（可以只填写域名，如"xxx.xxx.xxx"），为空则使用默认URL</param>
    public TelegramNotifier(HttpClient? httpClient = null, string telegramBotToken = "", string telegramChatId = "",
        string telegramApiBaseUrl = "")
    {
        TelegramBotToken = telegramBotToken;
        TelegramChatId = telegramChatId;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _ownsHttpClient = httpClient == null;
        TelegramApiBaseUrl = FormatApiBaseUrl(telegramApiBaseUrl);
    }

    public void Dispose()
    {
        if (_ownsHttpClient) _httpClient.Dispose();
    }

    public async Task SendAsync(BaseNotificationData content)
    {
        if (string.IsNullOrEmpty(TelegramBotToken)) throw new NotifierException("Telegram bot token is not set");
        if (string.IsNullOrEmpty(TelegramChatId)) throw new NotifierException("Telegram chat ID is not set");
        if (string.IsNullOrEmpty(content.Message)) throw new NotifierException("No message content to send");

        try
        {
            if (content.Screenshot != null)
            {
                await SendImageMessageAsync(content.Screenshot, content.Message.Length < 1024 ? content.Message : null);
                if (content.Message.Length < 1024) return;
            }

            await SendTextMessageAsync(content.Message);
        }
        catch (HttpRequestException ex)
        {
            throw new NotifierException("Network error sending Telegram notification: " + ex.Message);
        }
        catch (TaskCanceledException)
        {
            throw new NotifierException("Telegram API request timed out. Check your internet connection.");
        }
        catch (System.Exception ex) when (ex is not NotifierException)
        {
            throw new NotifierException("Error sending Telegram notification: " + ex.Message);
        }
    }

    private async Task SendImageMessageAsync(Image<Rgb24> image, string? caption)
    {
        var endpoint = $"{TelegramApiBaseUrl}{TelegramBotToken}/sendPhoto";
        using var memoryStream = new MemoryStream();
        await image.SaveAsPngAsync(memoryStream);
        memoryStream.Position = 0;

        var content = new MultipartFormDataContent
        {
            { new StreamContent(memoryStream), "photo", "image.png" },
            { new StringContent(TelegramChatId), "chat_id" }
        };
        if (!string.IsNullOrEmpty(caption)) content.Add(new StringContent(caption), "caption");

        await SendRequestAsync(endpoint, content, "image");
    }

    private async Task SendTextMessageAsync(string message)
    {
        var endpoint = $"{TelegramApiBaseUrl}{TelegramBotToken}/sendMessage";
        var json = JsonSerializer.Serialize(new
        {
            chat_id = TelegramChatId,
            text = message,
            disable_web_page_preview = true
        }, JsonOptions);

        var content = new StringContent(json, Encoding.UTF8, "application/json");
        await SendRequestAsync(endpoint, content, "text");
    }

    private async Task SendRequestAsync(string endpoint, HttpContent content, string type)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("BetterGenshinImpact", "1.0"));

        var response = await _httpClient.SendAsync(request);
        var responseContent = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new NotifierException($"Telegram {type} message failed: {response.StatusCode}, {responseContent}");

        var (isSuccess, errorCode, errorDescription) = ValidateApiResponse(responseContent);
        if (!isSuccess)
        {
            var msg = errorCode switch
            {
                400 => "Please send a message to the bot first and check that the chat ID is correct.",
                401 => "Telegram bot token is incorrect.",
                404 => $"Telegram API not found (404). Please verify your bot token is correct. URL: {endpoint}",
                _ => $"Telegram API error: {errorDescription} (Code: {errorCode})"
            };
            throw new NotifierException(msg);
        }
    }

    private static string FormatApiBaseUrl(string apiBaseUrl)
    {
        if (string.IsNullOrEmpty(apiBaseUrl)) return DefaultApiUrl;
        var url = apiBaseUrl.Trim();
        if (!url.StartsWith("http://") && !url.StartsWith("https://")) url = "https://" + url;
        if (!url.EndsWith("/")) url += "/";
        if (!url.EndsWith("/bot")) url += "bot";
        return url;
    }

    private static (bool isSuccess, int errorCode, string errorDescription) ValidateApiResponse(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var okElement) && okElement.GetBoolean())
                return (true, 0, string.Empty);

            var errorDescription = root.TryGetProperty("description", out var desc)
                ? desc.GetString()
                : "Unknown Telegram API error";
            var errorCode = root.TryGetProperty("error_code", out var code) ? code.GetInt32() : 0;
            return (false, errorCode, errorDescription ?? "");
        }
        catch (JsonException ex)
        {
            return (false, 0, "Failed to parse API response: " + ex.Message);
        }
    }
}