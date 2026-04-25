
using System;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.RevivePrompt;

public class RevivePromptTrigger : ITaskTrigger
{
    public string Name => "复苏提示";
    public bool IsEnabled { get; set; }
    public int Priority => 15;
    public bool IsExclusive => false;
    
    public GameUiCategory SupportedGameUiCategory => GameUiCategory.Unknown;

    private DateTime _prevExecute = DateTime.MinValue;

    public void Init()
    {
        IsEnabled = true;
    }

    public void OnCapture(CaptureContent content)
    {
        if ((DateTime.Now - _prevExecute).TotalMilliseconds <= 300)
        {
            return;
        }

        _prevExecute = DateTime.Now;

        if (Bv.ClickIfInReviveModal(content.CaptureRectArea))
        {
            TaskControl.Logger.LogInformation("检测到复苏弹窗，已自动点击");
        }
    }
}