using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.QuickTeleport.Assets;
using Microsoft.Extensions.Logging;
using System;

namespace BetterGenshinImpact.GameTask.QuickTeleport;

internal class QuickTeleportTrigger : ITaskTrigger
{
    public string Name => "快速传送";
    public bool IsEnabled { get; set; }
    public int Priority => 21;
    public bool IsExclusive => false;

    private readonly QuickTeleportAssets _assets;

    public QuickTeleportTrigger()
    {
        _assets = new QuickTeleportAssets();
    }

    public void Init()
    {
        IsEnabled = false;
    }

    public void OnCapture(CaptureContent content)
    {
        // 1.判断是否在地图界面
        content.CaptureRectArea.Find(_assets.MapScaleButtonRo, _ =>
        {
            // 2. 判断是否有传送按钮
            var hasTeleportButton = CheckTeleportButton(content);

            if (!hasTeleportButton)
            {
                // 3. 循环判断选项列表是否有传送点
                var hasMapChooseIcon = CheckMapChooseIcon(content);
                if (hasMapChooseIcon)
                {
                    TaskControl.Sleep(200);
                    content = TaskControl.CaptureToContent();
                    CheckTeleportButton(content);
                }
            }
        });
    }

    private bool CheckTeleportButton(CaptureContent content)
    {
        var hasTeleportButton = false;
        content.CaptureRectArea.Find(_assets.TeleportButtonRo, ra =>
        {
            ra.ClickCenter();
            hasTeleportButton = true;
            TaskControl.Logger.LogInformation("快速传送");
        });
        return hasTeleportButton;
    }

    private bool CheckMapChooseIcon(CaptureContent content)
    {
        var hasMapChooseIcon = false;
        foreach (var ro in _assets.MapChooseIconRoList)
        {
            var ra = content.CaptureRectArea.Find(ro);
            if (!ra.IsEmpty())
            {
                TaskControl.Sleep(200);
                hasMapChooseIcon = true;
                ra.ClickCenter();
                break;
            }
        }

        return hasMapChooseIcon;
    }
}