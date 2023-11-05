using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.AutoSkip.Assets;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading;
using BetterGenshinImpact.Core.Recognition.OCR;
using OpenCvSharp;
using Vanara.PInvoke;
using WindowsInput;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Model;

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

    private int _prevOtherClickFrameIndex = -1;

    public void OnCapture(CaptureContent content)
    {
        if (content.IsReachInterval(TimeSpan.FromMilliseconds(200)))
        {
            return;
        }

        var config = TaskContext.Instance().Config.AutoSkipConfig;
        var assetScale = TaskContext.Instance().SystemInfo.AssetScale;
        // 找左上角剧情自动的按钮
        using var foundRectArea = content.CaptureRectArea.Find(_autoSkipAssets.StopAutoButtonRo);
        if (!foundRectArea.IsEmpty())
        {
            if (TaskContext.Instance().Config.AutoSkipConfig.QuicklySkipConversationsEnabled)
            {
                Simulation.SendInput.Keyboard.KeyPress(VirtualKeyCode.SPACE);
            }

            // 领取每日委托奖励
            if (config.AutoGetDailyRewardsEnabled)
            {
                var dailyRewardIconRa = content.CaptureRectArea.Find(_autoSkipAssets.DailyRewardIconRo);
                if (!dailyRewardIconRa.IsEmpty())
                {
                    var text = GetOrangeOptionText(content.CaptureRectArea.SrcMat, dailyRewardIconRa, (int)(config.ChatOptionTextWidth * assetScale));

                    if (text.Contains("每日委托"))
                    {
                        if (Math.Abs(content.FrameIndex - _prevOtherClickFrameIndex) >= 8)
                        {
                            _logger.LogInformation("自动选择：{Text}", text);
                        }

                        dailyRewardIconRa.ClickCenter();
                        dailyRewardIconRa.Dispose();
                        return;
                    }

                    _prevOtherClickFrameIndex = content.FrameIndex;
                    dailyRewardIconRa.Dispose();
                }
            }

            // 领取探索派遣奖励
            if (config.AutoReExploreEnabled)
            {
                var exploreIconRa = content.CaptureRectArea.Find(_autoSkipAssets.ExploreIconRo);
                if (!exploreIconRa.IsEmpty())
                {
                    var text = GetOrangeOptionText(content.CaptureRectArea.SrcMat, exploreIconRa, (int)(config.ExpeditionOptionTextWidth * assetScale));
                    if (text.Contains("探索派遣"))
                    {
                        if (Math.Abs(content.FrameIndex - _prevOtherClickFrameIndex) >= 8)
                        {
                            _logger.LogInformation("自动选择：{Text}", text);
                        }

                        exploreIconRa.ClickCenter();

                        // 等待探索派遣界面打开
                        Thread.Sleep(1000);
                        new ExpeditionTask().Run(content);
                        exploreIconRa.Dispose();
                        return;
                    }

                    _prevOtherClickFrameIndex = content.FrameIndex;
                    exploreIconRa.Dispose();
                    return;
                }
            }

            // 找右下的对话选项按钮
            content.CaptureRectArea.Find(_autoSkipAssets.OptionIconRo, (optionButtonRectArea) =>
            {
                optionButtonRectArea.ClickCenter();

                if (Math.Abs(content.FrameIndex - _prevClickFrameIndex) >= 8)
                {
                    _logger.LogInformation("自动剧情：{Text}", "点击选项");
                }

                _prevClickFrameIndex = content.FrameIndex;
                optionButtonRectArea.Dispose();
            });
        }
        else
        {
            // 黑屏剧情要点击鼠标（多次） 几乎全黑的时候不用点击
            using var grayMat = content.CaptureRectArea.SrcGreyMat[new Rect(0, 0, content.CaptureRectArea.SrcGreyMat.Width / 2, content.CaptureRectArea.SrcGreyMat.Height / 2)];
            var blackCount = OpenCvCommonHelper.CountGrayMatColor(grayMat, 0);
            var rate = blackCount * 1.0 / (grayMat.Width * grayMat.Height);
            if (rate > 0.8 && rate < 0.99)
            {
                Simulation.SendInput.Mouse.LeftButtonClick();
                Debug.WriteLine($"点击黑屏剧情：{rate}");
            }

            // TODO 自动交付材料
        }
    }

    /// <summary>
    /// 获取橙色选项的文字
    /// </summary>
    /// <param name="captureMat"></param>
    /// <param name="foundIconRectArea"></param>
    /// <returns></returns>
    private string GetOrangeOptionText(Mat captureMat, RectArea foundIconRectArea, int chatOptionTextWidth)
    {
        var textRect = new Rect(foundIconRectArea.X + foundIconRectArea.Width, foundIconRectArea.Y, chatOptionTextWidth, foundIconRectArea.Height);
        using var mat = new Mat(captureMat, textRect);
        // 只提取橙色
        using var bMat = OpenCvCommonHelper.Threshold(mat, new Scalar(247, 198, 50), new Scalar(255, 204, 54));
        Cv2.ImWrite("bMat2.png", bMat);
        var whiteCount = OpenCvCommonHelper.CountGrayMatColor(bMat, 255);
        var rate = whiteCount * 1.0 / (bMat.Width * bMat.Height);
        if (rate < 0.1)
        {
            Debug.WriteLine($"识别到橙色文字区域占比:{rate}");
            return string.Empty;
        }

        var text = OcrFactory.Paddle.Ocr(bMat);
        return text;
    }
}