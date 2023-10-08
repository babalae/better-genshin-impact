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

    /// <summary>
    /// 左上角剧情自动的按钮位置
    /// </summary>
    private Rect _prevSkipButtonRect = Rect.Empty;

    public void OnCapture(CaptureContent content)
    {
        if (content.IsReachInterval(TimeSpan.FromMilliseconds(200)))
        {
            return;
        }


        // 找左上角剧情自动的按钮
        var foundRectArea = content.CaptureRectArea.Find(_autoSkipAssets.StopAutoButtonRo);
        if (!foundRectArea.IsEmpty())
        {
            _prevSkipButtonRect = foundRectArea.ToRect();
            if (TaskContext.Instance().Config.AutoSkipConfig.QuicklySkipConversationsEnabled)
            {
                new InputSimulator().Keyboard.KeyPress(VirtualKeyCode.SPACE);

                // 找右下的对话选项按钮
                content.CaptureRectArea.Find(_autoSkipAssets.OptionButtonRo, (optionButtonRectArea) =>
                {
                    optionButtonRectArea.ClickCenter();

                    if (_prevClickFrameIndex <= content.FrameIndex - 1 && _prevClickFrameIndex >= content.FrameIndex - 5)
                    {
                        _logger.LogInformation("自动剧情：{Text}", "点击选项");
                    }

                    _prevClickFrameIndex = content.FrameIndex;
                });
            }
        }
        else
        {
            //if (_prevSkipButtonRect == Rect.Empty)
            //{
            //    // 没有进入自动剧情 跳过
            //    return;
            //}

            //// 判断是否在等待选择对话选项场景
            //// 取上次识别到的跳过按钮的区域
            //var skipButtonAreaMat = content.CaptureRectArea.SrcGreyMat[_prevSkipButtonRect];
            //Cv2.Threshold(skipButtonAreaMat, skipButtonAreaMat, 0, 255, ThresholdTypes.Otsu | ThresholdTypes.Binary);
            //// 比较图片相似度
            //Cv2.Absdiff(skipButtonAreaMat, _autoSkipAssets.BinaryStopAutoButtonMat, skipButtonAreaMat);
            //var sameCount = OpenCvCommonHelper.CountGrayMatColor(skipButtonAreaMat, 0);
            //var sameRate = sameCount * 1.0 / (skipButtonAreaMat.Width * skipButtonAreaMat.Height);
            //Debug.WriteLine($"相似度：{sameRate}");
            //if (sameRate > 0.9)
            //{

            //}

            // 黑屏剧情要点击鼠标（多次） 几乎全黑的时候不用点击
            var grayMat = content.CaptureRectArea.SrcGreyMat[0, 0, content.CaptureRectArea.SrcGreyMat.Width / 2, content.CaptureRectArea.SrcGreyMat.Height / 2];
            var blackCount = OpenCvCommonHelper.CountGrayMatColor(grayMat, 0);
            var rate = blackCount * 1.0 / (grayMat.Width * grayMat.Height);
            if (rate > 0.8 && rate < 0.99)
            {
                new InputSimulator().Mouse.LeftButtonClick();
                Debug.WriteLine($"点击黑屏剧情：{rate}");
            }
        }


        // 不存在则找右下的选项按钮
        //content.CaptureRectArea.Find(_autoSkipAssets.OptionButtonRo, (optionButtonRectArea) =>
        //{
        //    // 不存在菜单的情况下 剧情在播放中
        //    var menuRectArea = content.CaptureRectArea.Find(_autoSkipAssets.MenuRo);
        //    if (menuRectArea.IsEmpty())
        //    {
        //        optionButtonRectArea.ClickCenter();

        //        if (_prevClickFrameIndex <= content.FrameIndex - 1 && _prevClickFrameIndex >= content.FrameIndex - 5)
        //        {
        //            _logger.LogInformation("自动剧情：{Text}", "点击选项");
        //        }

        //        _prevClickFrameIndex = content.FrameIndex;
        //    }
        //});


        // TODO 自动交付材料
    }
}