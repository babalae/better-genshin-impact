using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;
using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Notification.Model;

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
            throw new NotifierException("WorkWeixin webhook endpoint is not set");
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
                    throw new NotifierException($"WorkWeixin image webhook call failed with code: {imageResponse.StatusCode}");
                }
            }

            // Then send the text message
            if (!string.IsNullOrEmpty(content.Message))
            {
                var textPayload = await TransformTextData(content.Message);
                var textResponse = await _httpClient.PostAsync(Endpoint, textPayload);

                if (!textResponse.IsSuccessStatusCode)
                {
                    throw new NotifierException($"WorkWeixin text webhook call failed with code: {textResponse.StatusCode}");
                }
            }
        }
        catch (NotifierException)
        {
            throw;
        }
        catch (System.Exception ex)
        {
            throw new NotifierException($"Error sending WorkWeixin webhook: {ex.Message}");
        }
    }

    private async Task<StringContent> TransformImageData(System.Drawing.Image screenshot)
    {
        var base64Image = ConvertImageToBase64(screenshot);
        var md5Image = ComputeMd5Hash(screenshot); 
        
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

    private string ConvertImageToBase64(System.Drawing.Image image)
    {
        using (var ms = new MemoryStream())
        {
            string uniqueFileName = $"notification_image_{Guid.NewGuid()}.jpeg";
            string imagePath = Path.Combine(TempManager.GetTempDirectory(), uniqueFileName);
            image.Save(imagePath, System.Drawing.Imaging.ImageFormat.Jpeg);
            
            // Save the image to a MemoryStream in JPEG format
            image.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
            byte[] imageBytes = ms.ToArray();
            
            // Convert to Base64 (this will produce a single-line string)
            string base64String = Convert.ToBase64String(imageBytes);
            return base64String;
        }
    }

    private string ComputeMd5Hash(System.Drawing.Image image)
    {
        using (var ms = new MemoryStream())
        {
            image.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
            byte[] imageBytes = ms.ToArray();

            using (var md5 = MD5.Create())
            {
                // Compute the MD5 hash of the raw byte data (before encoding to Base64)
                byte[] hashBytes = md5.ComputeHash(imageBytes);
                // Convert hash bytes to a hex string (without hyphens)
                string md5Hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
                return md5Hash;
            }
        }
    }
}
