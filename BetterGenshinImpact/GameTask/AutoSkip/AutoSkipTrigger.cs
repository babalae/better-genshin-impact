using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.AutoSkip.Assets;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using BetterGenshinImpact.Core.Recognition.OCR;
using OpenCvSharp;
using Vanara.PInvoke;
using WindowsInput;
using BetterGenshinImpact.Core.Simulator;

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
            var dailyRewardIconRa = content.CaptureRectArea.Find(_autoSkipAssets.DailyRewardIconRo);
            if (!dailyRewardIconRa.IsEmpty())
            {
                var config = TaskContext.Instance().Config.AutoSkipConfig;
                var textRect = new Rect(dailyRewardIconRa.X + dailyRewardIconRa.Width, dailyRewardIconRa.Y, (int)(config.ChatOptionTextWidth * assetScale), dailyRewardIconRa.Height);
                //using var mat = new Mat(content.CaptureRectArea.SrcGreyMat, textRect);
                using var mat = new Mat(content.CaptureRectArea.SrcMat, textRect);
                // 只提取橙色
                using var bMat = OpenCvCommonHelper.Threshold(mat, new Scalar(247, 198, 50), new Scalar(255, 204, 504));
                var whiteCount = OpenCvCommonHelper.CountGrayMatColor(bMat, 255);
                if (whiteCount * 1.0 / (bMat.Width * bMat.Height) <= 0.1)
                {
                    dailyRewardIconRa.Dispose();
                    // 凯瑟琳聊天框不自动退出
                    return;
                }

                var text = OcrFactory.Paddle.Ocr(bMat);

                if (text.Contains("每日委托"))
                {
                    if (Math.Abs(content.FrameIndex - _prevOtherClickFrameIndex) >= 8)
                    {
                        _logger.LogInformation("自动选择：{Text}", text);
                    }

                    dailyRewardIconRa.ClickCenter();
                }

                _prevOtherClickFrameIndex = content.FrameIndex;
                dailyRewardIconRa.Dispose();
                return;
            }

            // 领取探索派遣奖励


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
}