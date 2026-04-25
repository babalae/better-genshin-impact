
using System;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.RevivePrompt;

public class RevivePromptTrigger : ITaskTrigger
{
    private readonly RevivePromptConfig _config;

    public string Name => "复苏提示";
    public bool IsEnabled { get; set; }
    public int Priority => 15;
    public bool IsExclusive => false;
    
    public GameUiCategory SupportedGameUiCategory => GameUiCategory.Unknown;

    private DateTime _prevExecute = DateTime.MinValue;

    public RevivePromptTrigger()
    {
        _config = TaskContext.Instance().Config.RevivePromptConfig;
    }

    public void Init()
    {
        IsEnabled = _config.Enabled;
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
