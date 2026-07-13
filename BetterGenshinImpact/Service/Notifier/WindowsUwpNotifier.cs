using System;
using System.IO;
using System.Threading.Tasks;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notifier.Interface;
using Microsoft.Toolkit.Uwp.Notifications;
using SixLabors.ImageSharp;

namespace BetterGenshinImpact.Service.Notifier;

public class WindowsUwpNotifier : INotifier
{
    public string Name => "Windows通知";

    public Task SendAsync(BaseNotificationData data)
    {
        var toastBuilder = new ToastContentBuilder();

        if (data.Screenshot != null)
        {
            string uniqueFileName = $"notification_image_{Guid.NewGuid()}.png";
            string imagePath = Path.Combine(TempManager.GetTempDirectory(), uniqueFileName);
            data.Screenshot.SaveAsPng(imagePath);
            toastBuilder.AddHeroImage(new Uri(imagePath));
        }

        if (!string.IsNullOrEmpty(data.Message))
        {
            toastBuilder.AddText(data.Message);
        }

        toastBuilder.Show(toast =>
        {
            toast.Group = data.Event.ToString();
            toast.ExpirationTime = DateTime.Now.AddHours(12);
        });
        return Task.CompletedTask;
    }
}
