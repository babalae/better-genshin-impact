using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;

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
    public int FragileResinCount { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    /// <summary>
    /// 浓缩树脂（60）
    /// </summary>
    public int CondensedResinCount { get; set; } = 0;

    /// <summary>
    /// 须臾树脂（60壶内购买）
    /// </summary>
    public int TransientResinCount { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public static ResinStatus RecogniseFromRegion(ImageRegion region, ISystemInfo systemInfo, IOcrService ocrService)
    {
        var status = new ResinStatus();

        // 1. 原粹树脂
        var assetScale = systemInfo.AssetScale;
        var originalResinTopIconRa = new RecognitionObject
        {
            Name = "OriginalResinTopIcon",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoFight", "original_resin_top_icon.png", systemInfo),
            DrawOnWindow = false
        }.InitTemplate();
        using ImageRegion crop1 = region.DeriveCrop(new Rect((int)(1300 * assetScale), (int)(25 * assetScale), (int)(160 * assetScale), (int)(50 * assetScale)));   // 数字位数的不同导致了水平方向上宽泛的区域
        //Cv2.ImShow("test", crop1.SrcMat);
        //Cv2.WaitKey();
        var originalResinRes = crop1.Find(originalResinTopIconRa);
        if (originalResinRes.IsEmpty())
        {
            throw new Exception("未找到原粹树脂图标");
        }

        var originalResinCountRect = new Rect(crop1.X + originalResinRes.Right + (int)(25 * assetScale), (int)(37 * assetScale), (int)(110 * assetScale)/* 考虑最长的“200/200” */, (int)(24 * assetScale));
        using ImageRegion originalResinCountRegion = region.DeriveCrop(originalResinCountRect);
        string cnt1 = ocrService.OcrWithoutDetector(originalResinCountRegion.SrcMat);
        var match = System.Text.RegularExpressions.Regex.Match(cnt1, @"(\d+)\s*[/17]\s*(2|20|200)");
        if (match.Success)
        {
            var numericPart = match.Groups[1].Value;
            status.OriginalResinCount = StringUtils.TryExtractPositiveInt(numericPart, 0);
        }

        // 2. 浓缩树脂
        int startX = crop1.X + originalResinRes.Left - (int)(180 * assetScale); // 从原粹树脂图标位置起算
        var condensedResinTopIconRa = new RecognitionObject
        {
            Name = "CondensedResinTopIcon",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoFight", "condensed_resin_top_icon.png", systemInfo),
            DrawOnWindow = false
        }.InitTemplate();
        using ImageRegion crop2 = region.DeriveCrop(new Rect(startX, (int)(25 * assetScale), (int)(90 * assetScale), (int)(50 * assetScale)));
        var condensedResinRes = crop2.Find(condensedResinTopIconRa);
        if (condensedResinRes.IsExist())
        {
            // 找出 icon 的位置 + 25 ~ icon 的位置+45 就是浓缩树脂的数字，数字宽20
            var condensedResinCountRect = new Rect(crop2.X + condensedResinRes.Right + (int)(20 * assetScale), (int)(37 * assetScale), (int)(70 * assetScale), (int)(24 * assetScale));
            using ImageRegion countRegion = region.DeriveCrop(condensedResinCountRect);
            using Mat threshold = countRegion.CacheGreyMat.Threshold(180, 255, ThresholdTypes.Binary);
            using Mat bitwiseNot = new Mat();
            Cv2.BitwiseNot(threshold, bitwiseNot);
            //Cv2.ImShow("bitwise", bitwise);
            //Cv2.WaitKey();
            string cnt40 = ocrService.OcrWithoutDetector(bitwiseNot);
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