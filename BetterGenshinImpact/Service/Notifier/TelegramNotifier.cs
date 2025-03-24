using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;

namespace BetterGenshinImpact.Service.Notifier;

public class TelegramNotifier : INotifier, IDisposable
{
    // 默认Telegram API的标准URL前缀
    private const string DEFAULT_TELEGRAM_API_URL = "https://api.telegram.org/bot";
    private readonly bool _createdHttpClient;
    private readonly HttpClient _httpClient;

    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>
    ///     创建一个新的Telegram通知器实例
    /// </summary>
    /// <param name="httpClient">可选的HttpClient，如果不提供则创建新的</param>
    /// <param name="telegramBotToken">Telegram机器人Token</param>
    /// <param name="telegramChatId">Telegram聊天ID</param>
    /// <param name="telegramApiBaseUrl">自定义Telegram API基础URL（可以只填写域名，如"xxx.xxx.xxx"），为空则使用默认URL</param>
    public TelegramNotifier(HttpClient httpClient = null, string telegramBotToken = "", string telegramChatId = "",
        string telegramApiBaseUrl = "")
    {
        TelegramBotToken = telegramBotToken;
        TelegramChatId = telegramChatId;

        if (httpClient != null)
        {
            _httpClient = httpClient;
            _createdHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient();
            _createdHttpClient = true;
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        // 使用自定义API URL，如果为空则使用默认Telegram API URL
        if (string.IsNullOrEmpty(telegramApiBaseUrl))
        {
            TelegramApiBaseUrl = DEFAULT_TELEGRAM_API_URL;
        }
        else
        {
            // 格式化用户提供的API URL
            var formattedUrl = telegramApiBaseUrl.Trim();

            // 添加协议前缀（如果没有）
            if (!formattedUrl.StartsWith("http://") && !formattedUrl.StartsWith("https://"))
                formattedUrl = "https://" + formattedUrl;

            // 确保URL以斜杠结尾
            if (!formattedUrl.EndsWith("/")) formattedUrl += "/";

            // 添加bot路径（如果需要）
            if (!formattedUrl.EndsWith("/bot")) formattedUrl += "bot";

            TelegramApiBaseUrl = formattedUrl;
        }
    }

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

    public void Dispose()
    {
        if (_createdHttpClient)
        {
            _httpClient?.Dispose();
        }
    }

    /// <summary>
    ///     通知器名称
    /// </summary>
    public string Name { get; set; } = "Telegram";

    public async Task SendAsync(BaseNotificationData content)
    {
        if (string.IsNullOrEmpty(TelegramBotToken))
        {
            throw new NotifierException("Telegram bot token is not set");
        }

        if (string.IsNullOrEmpty(TelegramChatId))
        {
            throw new NotifierException("Telegram chat ID is not set");
        }

        try
        {
            var message = content.Message;
            var fullMessage = !string.IsNullOrEmpty(message) ? message : "";

            if (!string.IsNullOrEmpty(fullMessage))
            {
                await SendTextMessageAsync(fullMessage);
            }
            else
            {
                throw new NotifierException("No message content to send");
            }
        }
        catch (HttpRequestException ex)
        {
            throw new NotifierException("Network error sending Telegram notification: " + ex.Message);
        }
        catch (TaskCanceledException)
        {
            throw new NotifierException("Telegram API request timed out. Check your internet connection.");
        }
        catch (NotifierException)
        {
            throw;
        }
        catch (System.Exception ex)
        {
            throw new NotifierException("Error sending Telegram notification: " + ex.Message);
        }
    }

    private async Task SendTextMessageAsync(string message)
    {
        // 构建Telegram API URL - 使用自定义或默认API基础URL
        var endpoint = $"{TelegramApiBaseUrl}{TelegramBotToken}/sendMessage";

        try
        {
            var jsonContent = new
            {
                chat_id = TelegramChatId,
                text = message,
                disable_web_page_preview = true
            };

            var json = JsonSerializer.Serialize(jsonContent, _jsonSerializerOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = content
            };

            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("BetterGenshinImpact", "1.0"));

            var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new NotifierException(
                    $"Telegram message failed with code: {response.StatusCode}, Error: {responseContent}");
            }

            // Check for API errors in the response
            var (isSuccess, errorCode, errorDescription) = ValidateApiResponse(responseContent);

            if (!isSuccess)
            {
                if (errorCode == 400)
                {
                    throw new NotifierException(
                        "Please send a message to the bot first and check that the chat ID is correct.");
                }

                if (errorCode == 401)
                {
                    throw new NotifierException("Telegram bot token is incorrect.");
                }

                if (errorCode == 404)
                    throw new NotifierException(
                        $"Telegram API not found (404). Please verify your bot token is correct. URL: {endpoint}");

                throw new NotifierException($"Telegram API error: {errorDescription} (Code: {errorCode})");
            }
        }
        catch (System.Exception ex) when (!(ex is NotifierException))
        {
            throw new NotifierException("Error sending Telegram notification: " + ex.Message);
        }
    }

    private (bool isSuccess, int errorCode, string errorDescription) ValidateApiResponse(string responseJson)
    {
        try
        {
            using (var doc = JsonDocument.Parse(responseJson))
            {
                var root = doc.RootElement;

                // Telegram API returns "ok": true for success
                if (root.TryGetProperty("ok", out var okElement))
                {
                    var isOk = okElement.GetBoolean();

                    if (!isOk)
                    {
                        var errorDescription = "Unknown Telegram API error";
                        if (root.TryGetProperty("description", out var descriptionElement))
                            errorDescription = descriptionElement.GetString();

                        var errorCode = 0;
                        if (root.TryGetProperty("error_code", out var errorCodeElement))
                            errorCode = errorCodeElement.GetInt32();

                        return (false, errorCode, errorDescription);
                    }

                    return (true, 0, string.Empty);
                }

                return (false, 0, "Invalid API response: 'ok' field missing");
            }
        }
        catch (JsonException ex)
        {
            return (false, 0, "Failed to parse API response: " + ex.Message);
        }
    }
}