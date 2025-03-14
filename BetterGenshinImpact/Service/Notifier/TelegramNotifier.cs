using System;
using System.Diagnostics;
using System.Net;
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
    private const string DEFAULT_TG_API_BASE_URL = "https://api.telegram.org/bot";
    private readonly bool _createdHttpClient;

    private readonly HttpClient _httpClient;

    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public TelegramNotifier(HttpClient httpClient = null, string botToken = "", string chatId = "",
        string apiBaseUrl = "",
        string proxyHost = "", int? proxyPort = null, string proxyAuth = "")
    {
        LogDebug("Initializing Telegram notifier...");
        LogDebug($"Bot Token: {(string.IsNullOrEmpty(botToken) ? "[EMPTY]" : "[CONFIGURED]")}");
        LogDebug($"Chat ID: {(string.IsNullOrEmpty(chatId) ? "[EMPTY]" : chatId)}");
        LogDebug($"API Base URL: {(string.IsNullOrEmpty(apiBaseUrl) ? DEFAULT_TG_API_BASE_URL : apiBaseUrl)}");
        LogDebug($"Proxy Host: {proxyHost}");
        LogDebug($"Proxy Port: {proxyPort}");
        LogDebug($"Proxy Auth: {(string.IsNullOrEmpty(proxyAuth) ? "[NONE]" : "[CONFIGURED]")}");

        BotToken = botToken;
        ChatId = chatId;
        ProxyHost = proxyHost;
        ProxyPort = proxyPort;
        ProxyAuth = proxyAuth;

        try
        {
            if (httpClient != null)
            {
                _httpClient = httpClient;
                _createdHttpClient = false;
                LogDebug("Using provided HttpClient");
            }
            else
            {
                var handler = new HttpClientHandler
                {
                    UseProxy = true
                };

                // Configure proxy
                if (UseCustomProxy)
                {
                    var proxyUrl =
                        $"http://{(string.IsNullOrEmpty(ProxyAuth) ? "" : ProxyAuth + "@")}{ProxyHost}:{ProxyPort}";
                    handler.Proxy = new WebProxy(proxyUrl);
                    LogInfo($"Using custom proxy: {proxyUrl}");
                }
                else
                {
                    handler.Proxy = WebRequest.GetSystemWebProxy();
                    handler.UseDefaultCredentials = true;

                    // Output proxy information
                    var proxy = WebRequest.GetSystemWebProxy();
                    var testUrl = new Uri("https://api.telegram.org");
                    var proxyUri = proxy.GetProxy(testUrl);
                    LogInfo(
                        $"System proxy for Telegram: {(proxyUri != testUrl ? proxyUri.ToString() : "Direct connection")}");
                }

                _httpClient = new HttpClient(handler);
                _createdHttpClient = true;

                // Set timeout
                _httpClient.Timeout = TimeSpan.FromSeconds(30);
                LogDebug("HttpClient configured with 30 seconds timeout");
            }
        }
        catch (System.Exception ex)
        {
            LogError($"Error creating HttpClient with proxy settings: {ex.Message}");

            // If creation fails, create a default HttpClient
            _httpClient = new HttpClient();
            _createdHttpClient = true;
            LogWarning("Created default HttpClient without proxy settings");
        }

        // Format API base URL
        if (string.IsNullOrEmpty(apiBaseUrl))
            ApiBaseUrl = DEFAULT_TG_API_BASE_URL;
        else
            UpdateApiBaseUrl(apiBaseUrl);

        LogInfo("Telegram API Base URL: " + ApiBaseUrl);
        LogInfo("Full endpoint for sendMessage: " + FormatEndpoint("sendMessage"));
    }

    public string BotToken { get; set; }

    public string ChatId { get; set; }

    public string ApiBaseUrl { get; set; }

    // Proxy configuration properties
    public string ProxyHost { get; set; }
    public int? ProxyPort { get; set; }
    public string ProxyAuth { get; set; }
    public bool UseCustomProxy => !string.IsNullOrEmpty(ProxyHost) && ProxyPort.HasValue;

    public void Dispose()
    {
        LogDebug("Disposing TelegramNotifier resources");
        if (_createdHttpClient)
        {
            LogDebug("Disposing HttpClient");
            _httpClient?.Dispose();
        }
    }

    public string Name { get; set; } = "Telegram";

    public async Task SendAsync(BaseNotificationData content)
    {
        LogInfo("Sending Telegram notification...");

        if (string.IsNullOrEmpty(BotToken))
        {
            LogError("Telegram bot token is not set");
            throw new NotifierException("Telegram bot token is not set");
        }

        if (string.IsNullOrEmpty(ChatId))
        {
            LogError("Telegram chat ID is not set");
            throw new NotifierException("Telegram chat ID is not set");
        }

        try
        {
            var message = content.Message;

            // Use just the message content since BaseNotificationData doesn't have a Description property
            var fullMessage = !string.IsNullOrEmpty(message) ? message : "";

            // If you want to add description support in the future, you would need to extend the BaseNotificationData class
            // or check if the content is a derived type that includes a description property

            if (!string.IsNullOrEmpty(fullMessage))
            {
                LogDebug($"Message content: {fullMessage}");
                await SendTextMessageAsync(fullMessage);
            }
            else
            {
                LogWarning("No message content to send");
                throw new NotifierException("No message content to send");
            }
        }
        catch (HttpRequestException ex)
        {
            LogError($"Network error sending Telegram notification: {ex.Message}");
            throw new NotifierException("Network error sending Telegram notification: " + ex.Message);
        }
        catch (TaskCanceledException ex)
        {
            LogError($"Telegram API request timed out: {ex.Message}");
            throw new NotifierException("Telegram API request timed out. Check your internet connection.");
        }
        catch (NotifierException ex)
        {
            LogError($"Notifier exception: {ex.Message}");
            throw;
        }
        catch (System.Exception ex)
        {
            LogError($"Unexpected error sending Telegram notification: {ex.Message}");
            throw new NotifierException("Error sending Telegram notification: " + ex.Message);
        }
    }

    private string FormatEndpoint(string command)
    {
        // Format: https://api.telegram.org/bot<token>/<METHOD_NAME>
        var endpoint = ApiBaseUrl + BotToken + "/" + command;
        LogDebug($"Formatted endpoint for {command}: {endpoint}");
        return endpoint;
    }

    private async Task SendTextMessageAsync(string message)
    {
        var endpoint = FormatEndpoint("sendMessage");

        LogInfo($"Preparing request to: {endpoint}");

        try
        {
            // Using JSON content as shown in the JavaScript example
            var jsonContent = new
            {
                chat_id = ChatId,
                text = message,
                disable_web_page_preview = true
            };

            var json = JsonSerializer.Serialize(jsonContent, _jsonSerializerOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Log request content
            LogDebug("Request parameters:");
            LogDebug($"chat_id: {ChatId}");
            LogDebug($"text: {message}");
            LogDebug("disable_web_page_preview: true");

            // Set up request
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = content
            };

            // Set standard request headers
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("BetterGenshinImpact", "1.0"));

            LogDebug($"Content-Type: {content.Headers.ContentType}");
            LogDebug("Starting request to Telegram API...");

            var stopwatch = Stopwatch.StartNew();
            var response = await _httpClient.SendAsync(request);
            stopwatch.Stop();

            LogDebug($"Request completed in {stopwatch.ElapsedMilliseconds}ms");
            LogInfo($"Response status code: {response.StatusCode}");

            var responseContent = await response.Content.ReadAsStringAsync();
            LogDebug($"Response content: {responseContent}");

            if (!response.IsSuccessStatusCode)
            {
                LogError($"Telegram API error: HTTP {response.StatusCode}, {responseContent}");
                throw new NotifierException(
                    $"Telegram message failed with code: {response.StatusCode}, Error: {responseContent}");
            }

            // Check for API errors in the response
            var (isSuccess, errorCode, errorDescription) = ValidateApiResponse(responseContent);

            if (!isSuccess)
            {
                if (errorCode == 400)
                {
                    LogError("Please send a message to the bot first and check that the chat ID is correct.");
                    throw new NotifierException(
                        "Please send a message to the bot first and check that the chat ID is correct.");
                }

                if (errorCode == 401)
                {
                    LogError("Telegram bot token is incorrect.");
                    throw new NotifierException("Telegram bot token is incorrect.");
                }

                LogError($"Telegram API error: {errorDescription} (Code: {errorCode})");
                throw new NotifierException($"Telegram API error: {errorDescription} (Code: {errorCode})");
            }

            LogInfo("Message sent successfully");
        }
        catch (System.Exception ex)
        {
            LogError($"Exception during HTTP request: {ex.Message}");
            throw;
        }
    }

    private (bool isSuccess, int errorCode, string errorDescription) ValidateApiResponse(string responseJson)
    {
        try
        {
            LogDebug("Validating Telegram API response...");

            using (var doc = JsonDocument.Parse(responseJson))
            {
                var root = doc.RootElement;

                // Telegram API returns "ok": true for success
                if (root.TryGetProperty("ok", out var okElement))
                {
                    var isOk = okElement.GetBoolean();
                    LogDebug($"API response ok: {isOk}");

                    if (!isOk)
                    {
                        var errorDescription = "Unknown Telegram API error";
                        if (root.TryGetProperty("description", out var descriptionElement))
                            errorDescription = descriptionElement.GetString();

                        var errorCode = 0;
                        if (root.TryGetProperty("error_code", out var errorCodeElement))
                            errorCode = errorCodeElement.GetInt32();

                        LogError($"Telegram API error: {errorDescription} (Code: {errorCode})");
                        return (false, errorCode, errorDescription);
                    }

                    return (true, 0, string.Empty);
                }

                LogError("Invalid Telegram API response: 'ok' field missing");
                return (false, 0, "Invalid API response: 'ok' field missing");
            }
        }
        catch (JsonException ex)
        {
            LogError($"Failed to parse Telegram API response: {ex.Message}");
            return (false, 0, "Failed to parse API response: " + ex.Message);
        }
    }

    public void UpdateApiBaseUrl(string apiBaseUrl)
    {
        LogInfo($"Updating API Base URL: {apiBaseUrl}");

        if (string.IsNullOrEmpty(apiBaseUrl))
        {
            LogDebug($"Empty API Base URL provided, using default: {DEFAULT_TG_API_BASE_URL}");
            ApiBaseUrl = DEFAULT_TG_API_BASE_URL;
            return;
        }

        // Ensure URL contains protocol prefix
        if (!apiBaseUrl.StartsWith("http://") && !apiBaseUrl.StartsWith("https://"))
        {
            LogDebug($"No protocol specified, adding https:// prefix to {apiBaseUrl}");
            apiBaseUrl = "https://" + apiBaseUrl;
        }

        // Handle bot path
        if (apiBaseUrl.Contains("/bot/"))
        {
            // URL already contains special format bot path
            // Ensure it ends with '/' for easy concatenation
            LogDebug("URL contains /bot/ path, ensuring it ends with /");
            ApiBaseUrl = apiBaseUrl.EndsWith("/") ? apiBaseUrl : apiBaseUrl + "/";
        }
        else
        {
            // Normalize URL format, to end with "/bot"
            LogDebug("Normalizing URL to end with /bot/");
            apiBaseUrl = apiBaseUrl.TrimEnd('/') + "/bot";

            // Ensure it ends with '/' for easy concatenation
            ApiBaseUrl = apiBaseUrl.EndsWith("/") ? apiBaseUrl : apiBaseUrl + "/";
        }

        LogInfo("API Base URL updated to: " + ApiBaseUrl);
    }

    // Simple logging methods
    private void LogDebug(string message)
    {
        Console.WriteLine($"[DEBUG] [TelegramNotifier] {message}");
    }

    private void LogInfo(string message)
    {
        Console.WriteLine($"[INFO] [TelegramNotifier] {message}");
    }

    private void LogWarning(string message)
    {
        Console.WriteLine($"[WARNING] [TelegramNotifier] {message}");
    }

    private void LogError(string message)
    {
        Console.WriteLine($"[ERROR] [TelegramNotifier] {message}");
    }
}