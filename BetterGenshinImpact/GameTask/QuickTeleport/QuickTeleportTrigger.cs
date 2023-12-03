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
    public bool IsExclusive { get; set; }

    private readonly QuickTeleportAssets _assets;
    private DateTime _prevClickTeleportButtonTime = DateTime.MinValue;
    private readonly QuickTeleportConfig _config;

    public QuickTeleportTrigger()
    {
        _assets = new QuickTeleportAssets();
        _config = TaskContext.Instance().Config.QuickTeleportConfig;
    }

    public void Init()
    {
        IsEnabled = _config.Enabled;
        IsExclusive = false;
    }

    public void OnCapture(CaptureContent content)
    {
        IsExclusive = false;
        // 1.判断是否在地图界面
        content.CaptureRectArea.Find(_assets.MapScaleButtonRo, _ =>
        {
            IsExclusive = true;

            // 2. 判断是否有传送按钮
            var hasTeleportButton = CheckTeleportButton(content);

            if (!hasTeleportButton)
            {
                // 存在地图关闭按钮，说明未选中传送点，直接返回
                var mapCloseRa = content.CaptureRectArea.Find(_assets.MapCloseButtonRo);
                if (!mapCloseRa.IsEmpty())
                {
                    return;
                }
                // 存在地图选择按钮，说明未选中传送点，直接返回
                var mapChooseRa = content.CaptureRectArea.Find(_assets.MapChooseRo);
                if (!mapChooseRa.IsEmpty())
                {
                    return;
                }

                // 3. 循环判断选项列表是否有传送点
                var hasMapChooseIcon = CheckMapChooseIcon(content);
                if (hasMapChooseIcon)
                {
                    TaskControl.Sleep(_config.WaitTeleportPanelDelay);
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
            if ((DateTime.Now - _prevClickTeleportButtonTime).TotalSeconds > 1)
            {
                TaskControl.Logger.LogInformation("快速传送");
            }
            _prevClickTeleportButtonTime = DateTime.Now;
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
                TaskControl.Sleep(_config.TeleportListClickDelay);
                hasMapChooseIcon = true;
                ra.ClickCenter();
               
                break;
            }
        }

        return hasMapChooseIcon;
    }
}