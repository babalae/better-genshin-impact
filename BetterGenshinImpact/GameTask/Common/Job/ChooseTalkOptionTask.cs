using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoSkip.Assets;
using BetterGenshinImpact.GameTask.AutoSkip;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using System.Text.RegularExpressions;
using BetterGenshinImpact.Core.Config;
using Microsoft.Extensions.Logging;
using BetterGenshinImpact.Core.Recognition.OpenCv;

namespace BetterGenshinImpact.GameTask.Common.Job;

public partial class ChooseTalkOptionTask
{
    private readonly ILogger<ChooseTalkOptionTask> _logger = App.GetLogger<ChooseTalkOptionTask>();

    public string Name => "持续对话并选择目标选项";

    // private readonly AutoSkipConfig _config = TaskContext.Instance().Config.AutoSkipConfig;

    /// <summary>
    /// 单个界面单个选项选择
    /// </summary>
    /// <param name="option"></param>
    /// <param name="ct"></param>
    /// <param name="skipTimes">200ms一次，点击几次空格</param>
    /// <param name="isOrange"></param>
    /// <returns></returns>
    public async Task<TalkOptionRes> SingleSelectText(string option, CancellationToken ct, int skipTimes = 10, bool isOrange = false)
    {
        if (!await Bv.WaitAndSkipForTalkUi(ct, 10))
        {
            Logger.LogError("选项选择：{Text}", "当前界面不在对话选项界面");
            return TalkOptionRes.NotFound;
        }

        await Task.Delay(500, ct);

        bool firstOcrOption = true;
        for (var i = 0; i < skipTimes; i++) // 重试N次
        {
            var region = CaptureToRectArea();
            var optionRegions = RecognizeOption(region, ct);
            if (optionRegions == null)
            {
                TaskContext.Instance().PostMessageSimulator.KeyPressBackground(User32.VK.VK_SPACE);
                await Delay(500, ct);
                continue; // retry
            }
            else
            {
                // 首次识别到文字，延迟1s重新识别一次，保证文字已经完全展示
                if (firstOcrOption)
                {
                    await Delay(1000, ct);
                    firstOcrOption = false;
                }
            }

            foreach (var optionRa in optionRegions)
            {
                if (optionRa.Text.Contains(option))
                {
                    if (isOrange)
                    {
                        // region.DeriveCrop(optionRa.ToRect()).SrcMat.SaveImage(Global.Absolute($"log\\t{optionRa.Text}.png"));
                        if (!IsOrangeOption(region.DeriveCrop(optionRa.ToRect()).SrcMat))
                        {
                            return TalkOptionRes.FoundButNotOrange;
                        }
                    }

                    ClickOcrRegion(optionRa);
                    await Task.Delay(300, ct);
                    return TalkOptionRes.FoundAndClick;
                }
            }
        }

        return TalkOptionRes.NotFound;
    }

    public async Task SelectLastOptionOnce(CancellationToken ct)
    {
        var region = CaptureToRectArea();
        if (Bv.IsInTalkUi(region))
        {
            var chatOptionResultList = region.FindMulti(AutoSkipAssets.Instance.OptionIconRo);
            chatOptionResultList = [.. chatOptionResultList.OrderByDescending(r => r.Y)];
            if (chatOptionResultList.Count > 0)
            {
                ClickOcrRegion(chatOptionResultList[0]);
                await Task.Delay(200, ct);
            }
        }
    }

    public async Task SelectLastOptionUntilEnd(CancellationToken ct, Func<ImageRegion, bool>? endAction = null, int retry = 2400)
    {
        for (var i = 0; i < retry; i++)
        {
            var region = CaptureToRectArea();
            if (Bv.IsInTalkUi(region))
            {
                var chatOptionResultList = region.FindMulti(AutoSkipAssets.Instance.OptionIconRo);
                chatOptionResultList = [.. chatOptionResultList.OrderByDescending(r => r.Y)];
                if (chatOptionResultList.Count > 0)
                {
                    ClickOcrRegion(chatOptionResultList[0]);
                }
                else
                {
                    TaskContext.Instance().PostMessageSimulator.KeyPressBackground(User32.VK.VK_SPACE);
                }
            }
            else if (Bv.IsInMainUi(region))
            {
                break;
            }
            else if (endAction != null && endAction(region))
            {
                break;
            }
            await Task.Delay(200, ct);
        }
    }

    [GeneratedRegex(@"^[a-zA-Z0-9]+$")]
    private static partial Regex EnOrNumRegex();

    /// <summary>
    /// 识别当前对话界面的所有选项
    /// </summary>
    /// <param name="region"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public List<Region>? RecognizeOption(ImageRegion region, CancellationToken ct)
    {
        var assetScale = TaskContext.Instance().SystemInfo.AssetScale;

        // 气泡识别
        var chatOptionResultList = region.FindMulti(AutoSkipAssets.Instance.OptionIconRo);
        if (chatOptionResultList.Count > 0)
        {
            // 第一个元素就是最下面的
            chatOptionResultList = [.. chatOptionResultList.OrderByDescending(r => r.Y)];

            // 通过最下面的气泡框来文字识别
            var lowest = chatOptionResultList[0];
            var ocrRect = new Rect((int)(lowest.X + lowest.Width + 8 * assetScale), region.Height / 8,
                (int)(535 * assetScale), (int)(lowest.Y + lowest.Height + 30 * assetScale - region.Height / 12d));
            var ocrResList = region.FindMulti(RecognitionObject.Ocr(ocrRect));

            // 删除为空的结果 和 纯英文的结果
            var rs = new List<Region>();
            // 按照y坐标排序
            ocrResList = [.. ocrResList.OrderBy(r => r.Y)];
            for (var i = 0; i < ocrResList.Count; i++)
            {
                var item = ocrResList[i];
                if (string.IsNullOrEmpty(item.Text) || (item.Text.Length < 5 && EnOrNumRegex().IsMatch(item.Text)))
                {
                    continue;
                }

                if (i != ocrResList.Count - 1)
                {
                    if (ocrResList[i + 1].Y - ocrResList[i].Y > 150)
                    {
                        Debug.WriteLine($"存在Y轴偏差过大的结果，忽略:{item.Text}");
                        continue;
                    }
                }

                rs.Add(item);
            }

            return ocrResList;
        }

        return null; // 没有找到气泡
    }

    private void ClickOcrRegion(Region region)
    {
        region.Click();
        AutoSkipLog(region.Text);
    }

    private void AutoSkipLog(string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            _logger.LogInformation("对话选项：{Text}", text);
        }
    }

    private bool IsOrangeOption(Mat textMat)
    {
        // 只提取橙色
        Scalar lowerOrange = new Scalar(10, 150, 150);
        Scalar upperOrange = new Scalar(25, 255, 255);
        var mask = OpenCvCommonHelper.InRangeHsv(textMat, lowerOrange, upperOrange);
        int highConfidencePixels = Cv2.CountNonZero(mask);
        double rate = highConfidencePixels * 1.0 / (mask.Width * mask.Height);
        Debug.WriteLine($"识别到橙色文字区域占比:{rate}");
        _logger.LogInformation($"识别到橙色文字区域占比:{rate}");
        return rate > 0.1;
    }
}

public enum TalkOptionRes
{
    // 未找到
    NotFound,

    // 找到但不是橙色
    FoundButNotOrange,

    // 找到并点击
    FoundAndClick,
}
