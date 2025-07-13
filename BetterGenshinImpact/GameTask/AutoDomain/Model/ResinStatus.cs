using System;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoDomain.Model;

public class ResinStatus
{
    /// <summary>
    /// 原粹树脂（1自回体）
    /// </summary>
    public int OriginalResinCount { get; set; } = 0;

    /// <summary>
    /// 脆弱树脂（60）
    /// </summary>
    public int FragileResinCount { get; set; } = 0;

    /// <summary>
    /// 浓缩树脂（40）
    /// </summary>
    public int CondensedResinCount { get; set; } = 0;

    /// <summary>
    /// 须臾树脂（60壶内购买）
    /// </summary>
    public int TransientResinCount { get; set; } = 0;

    public static ResinStatus RecogniseFromRegion(ImageRegion region)
    {
        var status = new ResinStatus();

        // 1. 原粹树脂 起点 w-(256+100) ~ w-256
        var captureArea = TaskContext.Instance().SystemInfo.ScaleMax1080PCaptureRect;
        var assetScale = TaskContext.Instance().SystemInfo.AssetScale;
        var originalResinTopIconRa = AutoFightAssets.Instance.OriginalResinTopIconRa;
        var originalResinRes = region.Find(originalResinTopIconRa);
        if (originalResinRes.IsEmpty())
        {
            throw new Exception("未找到原粹树脂图标");
        }

        var originalResinCountRect =  new Rect(originalResinRes.Right + 30, (int)(37 * assetScale),
      captureArea.Width - (originalResinRes.Right + 30) - (int)(190 * assetScale), (int)(21 * assetScale));
        string cnt1 = OcrFactory.Paddle.OcrWithoutDetector(region.DeriveCrop(originalResinCountRect).SrcMat);
        var match = System.Text.RegularExpressions.Regex.Match(cnt1, @"(\d+)\s*[/17]\s*(2|20|200)");
        if (match.Success)
        {
            var numericPart = match.Groups[1].Value;
            status.OriginalResinCount = StringUtils.TryExtractPositiveInt(numericPart, 0);
        }

        // 2. 浓缩树脂
        var condensedResinRes = region.Find(AutoFightAssets.Instance.CondensedResinTopIconRa);
        if (condensedResinRes.IsExist())
        {
            // 找出 icon 的位置 + 25 ~ icon 的位置+45 就是浓缩树脂的数字，数字宽20
            var condensedResinCountRect = new Rect(condensedResinRes.Right + (int)(25 * assetScale), condensedResinRes.Y, (int)(20 * assetScale), condensedResinRes.Height);
            string cnt40 = OcrFactory.Paddle.OcrWithoutDetector(region.DeriveCrop(condensedResinCountRect).SrcMat);
            status.CondensedResinCount = StringUtils.TryExtractPositiveInt(cnt40, 0);
        }

        return status;
    }

    public void Print(ILogger logger)
    {
        // logger.LogInformation("原粹树脂：{Cnt1}，浓缩树脂：{Cnt2}，须臾树脂：{Cnt3}，脆弱树脂：{Cnt4}", 
        //     OriginalResinCount, CondensedResinCount, FragileResinCount, TransientResinCount);
        logger.LogInformation("原粹树脂：{Cnt1}，浓缩树脂：{Cnt2}",
            OriginalResinCount, CondensedResinCount);
    }
}