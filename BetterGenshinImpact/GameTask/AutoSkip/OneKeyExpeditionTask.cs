using BetterGenshinImpact.GameTask.AutoSkip.Assets;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.View.Drawable;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoSkip;

public class OneKeyExpeditionTask
{
    public void Run(AutoSkipAssets assets)
    {
        // 1.全部领取
        var content = TaskControl.CaptureToContent();
        content.CaptureRectArea.Find(assets.CollectRo, ra =>
        {
            ra.ClickCenter();
            TaskControl.Logger.LogInformation("探索派遣：{Text}", "全部领取");
            TaskControl.Sleep(1000);
            // 2.重新派遣
            content = TaskControl.CaptureToContent();
            content.CaptureRectArea.Find(assets.ReRo, ra2 =>
            {
                ra2.ClickCenter();
                TaskControl.Logger.LogInformation("探索派遣：{Text}", "再次派遣");
            });
        });
    }
}