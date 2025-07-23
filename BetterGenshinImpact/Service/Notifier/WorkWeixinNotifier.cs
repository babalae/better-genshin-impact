using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;
using BetterGenshinImpact.Service.Interface;
using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notification.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace BetterGenshinImpact.Service.Notifier;

public class WorkWeixinNotifier : INotifier
{
    public string Name { get; set; } = "WorkWeixin";

    public string Endpoint { get; set; }

    private readonly HttpClient _httpClient;
    
    public WorkWeixinNotifier(HttpClient httpClient, string endpoint = "")
    {
        _httpClient = httpClient;
        Endpoint = endpoint;
    }

    public async Task SendAsync(BaseNotificationData content)
    {
        if (string.IsNullOrEmpty(Endpoint))
        {
            var localizationService = App.GetService<ILocalizationService>();
            var errorMessage = localizationService != null ? localizationService.GetString("notification.error.workWeixinEndpointEmpty") : "WorkWeixin webhook endpoint is not set";
            throw new NotifierException(errorMessage);
        }
        
        try
        {
            // If there's a screenshot, send it first as an image
            if (content.Screenshot != null)
            {
                var imagePayload = await TransformImageData(content.Screenshot);
                var imageResponse = await _httpClient.PostAsync(Endpoint, imagePayload);

                if (!imageResponse.IsSuccessStatusCode)
                {
                    var localizationService = App.GetService<ILocalizationService>();
                    var errorMessage = localizationService != null ? localizationService.GetString("notification.error.workWeixinImageFailed", imageResponse.StatusCode) : $"WorkWeixin image webhook call failed with code: {imageResponse.StatusCode}";
                    throw new NotifierException(errorMessage);
                }
            }
            // 添加延迟让图片先发送出去
            Thread.Sleep(1000);
            // Then send the text message
            if (!string.IsNullOrEmpty(content.Message))
            {
                var outputMessage = content.Timestamp + "\n\n" + content.Message;
                var textPayload = await TransformTextData(outputMessage);
                var textResponse = await _httpClient.PostAsync(Endpoint, textPayload);

                if (!textResponse.IsSuccessStatusCode)
                {
                    var localizationService = App.GetService<ILocalizationService>();
                    var errorMessage = localizationService != null ? localizationService.GetString("notification.error.workWeixinTextFailed", textResponse.StatusCode) : $"WorkWeixin text webhook call failed with code: {textResponse.StatusCode}";
                    throw new NotifierException(errorMessage);
                }
            }
        }
        catch (NotifierException)
        {
            throw;
        }
        catch (System.Exception ex)
        {
            var localizationService = App.GetService<ILocalizationService>();
            var errorMessage = localizationService != null ? localizationService.GetString("notification.error.workWeixinError", ex.Message) : $"Error sending WorkWeixin webhook: {ex.Message}";
            throw new NotifierException(errorMessage);
        }
    }

    private async Task<StringContent> TransformImageData(Image<Rgb24> screenshot)
    {
        var base64Image = ConvertImageToBase64(screenshot, out var md5Image);
        
        var imageMessage = new
        {
            msgtype = "image",
            image = new
            {
                base64 = base64Image,
                md5 = md5Image
            }
        };
        await Task.Yield();
        var serializedImageData = JsonSerializer.Serialize(imageMessage);
        return new StringContent(serializedImageData, Encoding.UTF8, "application/json");
    }

    private async Task<StringContent> TransformTextData(string message)
    {
        var textMessage = new
        {
            msgtype = "text",
            text = new
            {
                content = message
            }
        };
        await Task.Yield();
        var serializedTextData = JsonSerializer.Serialize(textMessage);
        return new StringContent(serializedTextData, Encoding.UTF8, "application/json");
    }

    private string ConvertImageToBase64(Image<Rgb24> image, out string md5Hash)
    {
        using (var ms = new MemoryStream())
        {
            // Save the image to a MemoryStream in JPEG format
            image.SaveAsJpeg(ms);
            byte[] imageBytes = ms.ToArray();

            // Convert to Base64 (this will produce a single-line string)
            string base64String = Convert.ToBase64String(imageBytes);

            // Compute the MD5 hash of the raw byte data
            byte[] hashBytes = MD5.HashData(imageBytes);
            // Convert hash bytes to a hex string
            md5Hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();

            return base64String;
        }
    }
}
