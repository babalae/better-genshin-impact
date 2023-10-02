using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.AutoSkip.Assets;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
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
        IsEnabled = true;
    }

    public void OnCapture(CaptureContent content)
    {
        if (content.IsReachInterval(TimeSpan.FromMilliseconds(200)))
        {
            return;
        }

        // 找左上角剧情自动的按钮
        content.CaptureRectArea.Find(_autoSkipAssets.StopAutoButtonRo, (_) =>
        {
            new InputSimulator().Keyboard.KeyPress(VirtualKeyCode.SPACE);
        });

        // 不存在则找右下的选项按钮
        content.CaptureRectArea.Find(_autoSkipAssets.OptionButtonRo, (optionButtonRectArea) =>
        {
            // 不存在菜单的情况下 剧情在播放中
            var menuRectArea = content.CaptureRectArea.Find(_autoSkipAssets.MenuRo);
            if (menuRectArea.IsEmpty())
            {
                optionButtonRectArea.ClickCenter();
                _logger.LogInformation("点击选项按钮");
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

        //var grayMat = content.SrcGreyMat;
        //// 找左上角剧情自动的按钮
        //var grayLeftTopMat = CutHelper.CutLeftTop(grayMat, grayMat.Width / 5, grayMat.Height / 5);
        //var p1 = OldMatchTemplateHelper.FindSingleTarget(grayLeftTopMat, AutoSkipAssets.StopAutoButtonMat, 0.9);
        //if (p1 is { X: > 0, Y: > 0 })
        //{
        //    //_logger.LogInformation($"找到剧情自动按钮：{p1}");
        //    VisionContext.Instance().DrawContent.PutRect("StopAutoButton",
        //        p1.CenterPointToRect(AutoSkipAssets.StopAutoButtonMat).ToRectDrawable());
        //    new InputSimulator().Keyboard.KeyPress(VirtualKeyCode.SPACE);
        //}
        //else
        //{
        //    VisionContext.Instance().DrawContent.RemoveRect("StopAutoButton");
        //}

        //// 不存在则找右下的选项按钮
        //var grayRightBottomMat = content.SrcGreyRightBottomMat;
        //var p2 = OldMatchTemplateHelper.FindSingleTarget(grayRightBottomMat, AutoSkipAssets.OptionMat);
        //if (p2 is { X: > 0, Y: > 0 })
        //{
        //    // 不存在菜单的情况下 剧情在播放中
        //    var grayLeftTopMat2 = CutHelper.CutLeftTop(grayMat, grayMat.Width / 4, grayMat.Height / 4);
        //    var pMenu = OldMatchTemplateHelper.FindSingleTarget(grayLeftTopMat2, AutoSkipAssets.MenuMat);
        //    if (pMenu is { X: 0, Y: 0 })
        //    {
        //        p2 = p2.ToDesktopPositionOffset65535(grayMat.Width - grayMat.Width / 2,
        //            grayMat.Height - grayMat.Height / 3 * 2);
        //        new InputSimulator().Mouse.MoveMouseTo(p2.X, p2.Y).LeftButtonClick();
        //        _logger.LogInformation($"点击选项按钮：{p2}");
        //        return;
        //    }
        //}



    }
}