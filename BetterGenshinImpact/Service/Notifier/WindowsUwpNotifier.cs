using System;
using System.Threading.Tasks;
using BetterGenshinImpact.Infrastructure.Temp;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notifier.Interface;
using Microsoft.Toolkit.Uwp.Notifications;
using SixLabors.ImageSharp;

namespace BetterGenshinImpact.Service.Notifier;

/// <param name="tempFileService">通知配图写入临时文件，随进程退出删除</param>
public class WindowsUwpNotifier(ITempFileService tempFileService) : INotifier
{
    public string Name => "Windows通知";

    public Task SendAsync(BaseNotificationData data)
    {
        var toastBuilder = new ToastContentBuilder();

        if (data.Screenshot != null)
        {
            string imagePath = tempFileService.CreateFilePath(TempCategories.Notification, ".png");
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
