using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notification.Model;
using System.IO;
using System.Net.Http.Headers;
using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace BetterGenshinImpact.Service.Notifier;

public class FeishuNotifier : INotifier
{
    public string Name { get; set; } = "Feishu";

    public string Endpoint { get; set; }

    public string AppId { get; set; }

    public string AppSecret { get; set; }

    private readonly HttpClient _httpClient;

    private static readonly string _accessTokenUrl = "https://open.feishu.cn/open-apis/auth/v3/tenant_access_token/internal";
    private static readonly string _uploadImageUrl = "https://open.feishu.cn/open-apis/im/v1/images";
    
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
            var response = await _httpClient.PostAsync(Endpoint, await TransformData(content));

            if (!response.IsSuccessStatusCode)
            {
                throw new NotifierException($"Feishu webhook call failed with code: {response.StatusCode}");
            }
        }
        catch (NotifierException)
        {
            throw;
        }
        catch (System.Exception ex)
        {
            throw new NotifierException($"Error sending Feishu webhook: {ex.Message}");
        }
    }

    private async Task<StringContent> TransformData(BaseNotificationData notificationData)
    {
        Object feishuMessage;
        if (notificationData.Screenshot != null && AppId.Length > 0 && AppSecret.Length > 0)
        {
            var accessToken = await GetAccessToken();
            var imageKey = await UploadImage(notificationData.Screenshot, accessToken);
            feishuMessage = new
            {
                msg_type = "post",
                content = new
                {
                    post = new
                    {
                        zh_cn = new
                        {
                            content = new object[] {
                                new object[] {
                                    new {
                                        tag = "text",
                                        text = notificationData.Message
                                    },
                                    new {
                                        tag = "img",
                                        image_key = imageKey,
                                    }
                                }
                            }
                        }
                    }
                }
            };
        }
        else
        {
            feishuMessage = new
            {
                msg_type = "text",
                content = new
                {
                    text = notificationData.Message
                }
            };
        }
        var serializedData = JsonSerializer.Serialize(feishuMessage);
        return new StringContent(serializedData, Encoding.UTF8, "application/json");
    }

    // obtain access tonken from feishu open api
    private async Task<string> GetAccessToken()
    {
        string tokenString = string.Empty;
        var accessTokenBody = new
        {
            app_id = AppId,
            app_secret = AppSecret
        };
        var accessTokenBodySerializedData = JsonSerializer.Serialize(accessTokenBody);
        var accessTokenBodyContent = new StringContent(accessTokenBodySerializedData, Encoding.UTF8, "application/json");
        using (var accessTokenResponse = await _httpClient.PostAsync(_accessTokenUrl, accessTokenBodyContent))
        {
            var tokenResponseContent = await accessTokenResponse.Content.ReadAsStringAsync();
            if (!accessTokenResponse.IsSuccessStatusCode)
            {
                throw new NotifierException($"Feishu access token call failed with code: {accessTokenResponse.StatusCode}");
            }
            using (JsonDocument doc = JsonDocument.Parse(tokenResponseContent))
            {
                JsonElement root = doc.RootElement;
                if (root.TryGetProperty("tenant_access_token", out JsonElement dataElement))
                {
                    var keyNullable = dataElement.GetString();
                    if (keyNullable != null)
                    {
                        tokenString = keyNullable;
                    }
                }
            }
        }
        if (string.IsNullOrEmpty(tokenString))
        {
            throw new NotifierException($"Feishu access token not found");
        }
        return tokenString;
    }

    private async Task<String> UploadImage(Image<Rgb24> image, string accessToken)
    {
        string imageKey = string.Empty;
        MultipartFormDataContent multipartContent = new MultipartFormDataContent();
        MemoryStream ms = new MemoryStream();
        await image.SaveAsPngAsync(ms);
        ms.Seek(0, SeekOrigin.Begin);
        ByteArrayContent byteContent = new ByteArrayContent(ms.ToArray());
        byteContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        multipartContent.Add(byteContent, "image", "image.png");
        multipartContent.Add(new StringContent("message"), "image_type");
        HttpRequestMessage uploadImageRequest = new HttpRequestMessage(HttpMethod.Post, _uploadImageUrl)
        {
            Content = multipartContent
        };
        uploadImageRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using (HttpResponseMessage uploadImageResponse = await _httpClient.SendAsync(uploadImageRequest))
        {
            var uploadResponseString = await uploadImageResponse.Content.ReadAsStringAsync();
            if (!uploadImageResponse.IsSuccessStatusCode)
            {
                throw new NotifierException($"Feishu upload image failed with code: {uploadImageResponse.StatusCode}");
            }
            using (JsonDocument doc = JsonDocument.Parse(uploadResponseString))
            {
                JsonElement root = doc.RootElement;
                if (root.TryGetProperty("data", out JsonElement dataElement) &&
                    dataElement.TryGetProperty("image_key", out JsonElement imageKeyElement))
                {
                    var keyNullable = imageKeyElement.GetString();
                    if (keyNullable != null)
                    {
                        imageKey = keyNullable;
                    }
                }
            }
        }
        if (string.IsNullOrEmpty(imageKey))
        {
            throw new NotifierException($"Feishu upload image not found image key");
        }
        return imageKey;
    }
}
