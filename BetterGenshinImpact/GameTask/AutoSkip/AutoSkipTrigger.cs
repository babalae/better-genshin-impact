using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.AutoSkip.Assets;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using OpenCvSharp;
using Vanara.PInvoke;
using WindowsInput;

namespace BetterGenshinImpact.GameTask.AutoSkip;

/// <summary>
/// 自动剧情有选项点击
/// </summary>
public class AutoSkipTrigger : ITaskTrigger
{
    private readonly ILogger<AutoSkipTrigger> _logger = App.GetLogger<AutoSkipTrigger>();

    public string Name => "自动剧情";
    public bool IsEnabled { get; set; }
    public int Priority => 20;
    public bool IsExclusive => false;

    private readonly AutoSkipAssets _autoSkipAssets;

    public AutoSkipTrigger()
    {
        _autoSkipAssets = new AutoSkipAssets();
    }

    public void Init()
    {
        IsEnabled = TaskContext.Instance().Config.AutoSkipConfig.Enabled;
    }

    /// <summary>
    /// 用于日志只输出一次
    /// frame最好取模,应对极端场景
    /// </summary>
    private int _prevClickFrameIndex = -1;

    public void OnCapture(CaptureContent content)
    {
        if (content.IsReachInterval(TimeSpan.FromMilliseconds(200)))
        {
            return;
        }

        if (TaskContext.Instance().Config.AutoSkipConfig.QuicklySkipConversationsEnabled)
        {
            // 找左上角剧情自动的按钮
            content.CaptureRectArea.Find(_autoSkipAssets.StopAutoButtonRo, (_) =>
            {
                new InputSimulator().Keyboard.KeyPress(VirtualKeyCode.SPACE);
            });
        }

        // 不存在则找右下的选项按钮
        content.CaptureRectArea.Find(_autoSkipAssets.OptionButtonRo, (optionButtonRectArea) =>
        {
            // 不存在菜单的情况下 剧情在播放中
            var menuRectArea = content.CaptureRectArea.Find(_autoSkipAssets.MenuRo);
            if (menuRectArea.IsEmpty())
            {
                optionButtonRectArea.ClickCenter();

                if (_prevClickFrameIndex <= content.FrameIndex - 1 && _prevClickFrameIndex >= content.FrameIndex - 5)
                {
                    _logger.LogInformation("自动剧情：{Text}", "点击选项");
                }
                _prevClickFrameIndex = content.FrameIndex;
            }
        });

        // 黑屏剧情要点击鼠标（多次） 几乎全黑的时候不用点击
        var grayMat = content.CaptureRectArea.SrcGreyMat;
        var blackCount = OpenCvCommonHelper.CountGrayMatColor(grayMat, 0);
        var rate = blackCount * 1.0 / (grayMat.Width * grayMat.Height);
        if (rate > 0.7 && rate < 0.99)
        {
            new InputSimulator().Mouse.LeftButtonClick();
            Debug.WriteLine($"点击黑屏剧情：{rate}");
            return;
        }

        // TODO 自动交付材料

    }
}