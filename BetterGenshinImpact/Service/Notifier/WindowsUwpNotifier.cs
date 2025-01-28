using System;
using System.IO;
using System.Threading.Tasks;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notifier.Interface;
using Microsoft.Toolkit.Uwp.Notifications;

namespace BetterGenshinImpact.Service.Notifier;

public class WindowsUwpNotifier : INotifier
{
    public string Name => "Windows通知";

    public Task SendAsync(INotificationData data)
    {
        var toastBuilder = new ToastContentBuilder()
            .AddHeader("BetterGI", "BetterGI", "action=click");

        if (data.Screenshot != null)
        {
            string uniqueFileName = $"notification_image_{Guid.NewGuid()}.png";
            string imagePath = Path.Combine(TempManager.TempDirectory, uniqueFileName);
            data.Screenshot.Save(imagePath, System.Drawing.Imaging.ImageFormat.Png);
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