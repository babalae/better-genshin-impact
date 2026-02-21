using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Exceptions;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.View.Drawable;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

public class SwitchPartyTask
{
    private readonly double _assetScale = TaskContext.Instance().SystemInfo.AssetScale;

    public string Name => "切换队伍";

    private readonly ReturnMainUiTask _returnMainUiTask = new();

    public async Task<bool> Start(string partyName, CancellationToken ct)
    {
        var useOcrMatch = TaskContext.Instance().Config.OtherConfig.OcrConfig.UseOcrMatchForPartySwitch;

        Logger.LogInformation("尝试切换至队伍: {Name}", partyName);
        using var ra1 = CaptureToRectArea();

        // 确保进入队伍配置界面
        bool isInPartyViewUi = false;
        if (!Bv.IsInPartyViewUi(ra1))
        {
            isInPartyViewUi = true;
            await EnsurePartyViewOpen(ra1, ct);
        }

        await Delay(500, ct);

        using var ra = CaptureToRectArea();
        var partyViewBtn = ra.Find(ElementAssets.Instance.PartyBtnChooseView);

        if (!partyViewBtn.IsExist())
        {
            Logger.LogWarning("未找到队伍选择按钮，无法判断当前队伍");
            throw new PartySetupFailedException("未找到队伍选择按钮");
        }

        // 检查当前队伍是否已是目标
        if (IsCurrentTeamMatch(ra, partyViewBtn, partyName, useOcrMatch))
        {
            if (isInPartyViewUi)
            {
                Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
                await Delay(500, ct);
                await _returnMainUiTask.Start(ct);
            }

            return true;
        }

        // 打开队伍选择页面
        var partyDeleteBtn = await OpenPartyChoosePage(partyViewBtn, ct);
        await ScrollToTop(ct);

        // 逐页查找目标队伍
        Rect regionOfInterest = new(0, (int)(80 * _assetScale), partyDeleteBtn.Right, partyDeleteBtn.Top - (int)(80 * _assetScale));
        var recognitionObject = new RecognitionObject
        {
            RecognitionType = RecognitionTypes.Ocr,
            RegionOfInterest = regionOfInterest,
            DrawOnWindow = true,
            Name = "队伍名称",
            DrawOnWindowPen = System.Drawing.Pens.White
        };

        try
        {
            for (var i = 0; i < 16; i++) // 6.0版本最多20个队伍
            {
                using var page = CaptureToRectArea();
                var nameList = page.FindMulti(recognitionObject);

                if (nameList == null || nameList.Count <= 0)
                {
                    Logger.LogInformation("管理队伍界面文字识别失败");
                    break;
                }

                // 在当前页查找匹配
                var (match, score) = FindMatchInPage(page, nameList, partyName, useOcrMatch);
                if (match != null)
                {
                    page.ClickTo(match.Right + match.Width, match.Bottom);
                    await Delay(200, ct);
                    if (useOcrMatch)
                        Logger.LogInformation("切换队伍成功: {Text}（匹配分数: {Score:F4}）", match.Text, score);
                    else
                        Logger.LogInformation("切换队伍成功: {Text}", match.Text);
                    await ConfirmParty(page, ct, isInPartyViewUi);
                    RunnerContext.Instance.ClearCombatScenes();
                    return true;
                }

                // 判断是否已遍历所有队伍
                var lowest = nameList
                    .Where(r => r.X > 35 * _assetScale && r.X < 100 * _assetScale)
                    .OrderBy(r => r.Y)
                    .LastOrDefault();
                if (lowest == null)
                {
                    Logger.LogInformation("未找到符合坐标范围的队伍名称，跳过翻页判断");
                    continue;
                }
                lowest.DrawSelf("底部的队伍");

                if (lowest.Y < 777 * _assetScale) // 如果最底下是空队伍则不会有队伍名，以此判断是否已遍历完成
                {
                    Logger.LogInformation("已抵达最后一个队伍");
                    break;
                }

                // 翻页
                if (i == 0)
                {
                    // 首次点一下第一个，防止第五个被点击过
                    page.ClickTo(600 * _assetScale, 200 * _assetScale);
                    await Task.Delay(300, ct);
                }

                page.ClickTo(regionOfInterest.X + regionOfInterest.Width / 2, lowest.Bottom);
                await Delay(400, ct);
            }
        }
        finally
        {
            VisionContext.Instance().DrawContent.ClearAll();
        }

        // 未找到
        Logger.LogError("未找到队伍: {Name}，返回主界面", partyName);
        Logger.LogInformation(useOcrMatch
            ? "如果找不到设定的队伍名，有可能是文字识别效果不佳，请尝试调整 OcrMatch 模糊匹配阈值"
            : "如果找不到设定的队伍名，有可能是文字识别效果不佳，请尝试正则表达式");
        await _returnMainUiTask.Start(ct);
        return false;
    }

    /// <summary>
    /// 确保队伍配置界面已打开。如果不在主界面则先返回主界面，然后打开队伍配置。
    /// </summary>
    private async Task EnsurePartyViewOpen(ImageRegion currentScreen, CancellationToken ct)
    {
        if (!Bv.IsInMainUi(currentScreen))
        {
            await _returnMainUiTask.Start(ct);
            await Delay(200, ct);
            using var raMain = CaptureToRectArea();
            if (!Bv.IsInMainUi(raMain))
                throw new InvalidOperationException("未能返回主界面");
        }

        const int maxAttempts = 2;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            Simulation.SendInput.SimulateAction(GIActions.OpenPartySetupScreen);
            for (int i = 0; i < 7; i++) // 考虑加载时间 2s，共检查 4.2s
            {
                await Delay(600, ct);
                using var raCheck = CaptureToRectArea();
                if (Bv.IsInPartyViewUi(raCheck)) return;
            }
        }

        throw new PartySetupFailedException("未能打开队伍配置界面");
    }

    /// <summary>
    /// 检查当前队伍名称是否匹配目标
    /// </summary>
    private bool IsCurrentTeamMatch(ImageRegion ra, Region partyViewBtn, string partyName, bool useOcrMatch)
    {
        var roi = new Rect(partyViewBtn.Right, partyViewBtn.Top, (int)(350 * _assetScale), partyViewBtn.Height);

        if (useOcrMatch)
        {
            var matchService = OcrFactory.PaddleMatch;
            var threshold = TaskContext.Instance().Config.OtherConfig.OcrConfig.OcrMatchDefaultThreshold;
            using var region = ra.DeriveCrop(roi);
            var score = matchService.OcrMatch(region.SrcMat, partyName);
            Logger.LogInformation("切换队伍，当前队伍 OcrMatch 分数: {Score:F4}，判断阈值: {Threshold}", score, threshold);
            if (score >= threshold)
            {
                Logger.LogInformation("当前队伍即为目标队伍（匹配分数: {Score:F4}），无需切换", score);
                return true;
            }

            return false;
        }

        var text = CleanOcrText(ra.Find(new RecognitionObject
        {
            RecognitionType = RecognitionTypes.Ocr,
            RegionOfInterest = roi
        }).Text);
        Logger.LogInformation("切换队伍，当前队伍名称: {Text}，使用正则表达式规则进行模糊匹配", text);
        if (Regex.IsMatch(text, partyName))
        {
            Logger.LogInformation("当前队伍[{Name}]即为目标队伍，无需切换", text);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 在当前页的文字区域列表中查找匹配目标的队伍
    /// </summary>
    private (Region? match, double score) FindMatchInPage(
        ImageRegion page, List<Region> textRegions, string partyName, bool useOcrMatch)
    {
        if (useOcrMatch)
        {
            var matchService = OcrFactory.PaddleMatch;
            var threshold = TaskContext.Instance().Config.OtherConfig.OcrConfig.OcrMatchDefaultThreshold;
            Region? bestMatch = null;
            double bestScore = 0;
            var imgW = page.SrcMat.Width;
            var imgH = page.SrcMat.Height;
            foreach (var region in textRegions)
            {
                var cx = Math.Max(0, region.X);
                var cy = Math.Max(0, region.Y);
                var cw = Math.Min(region.Width, imgW - cx);
                var ch = Math.Min(region.Height, imgH - cy);
                if (cw <= 0 || ch <= 0)
                    continue;

                using var cropped = page.DeriveCrop(cx, cy, cw, ch);
                var score = matchService.OcrMatchDirect(cropped.SrcMat, partyName);
                if (score >= threshold && score > bestScore)
                {
                    bestScore = score;
                    bestMatch = region;
                }
            }

            return (bestMatch, bestScore);
        }

        foreach (var region in textRegions)
        {
            if (Regex.IsMatch(region.Text, partyName))
                return (region, 0);
        }

        return (null, 0);
    }

    /// <summary>
    /// 打开队伍选择页面（点击选择按钮并等待加载）
    /// </summary>
    private static async Task<Region> OpenPartyChoosePage(Region partyViewBtn, CancellationToken ct)
    {
        var menu = await NewRetry.WaitForElementAppear(
            ElementAssets.Instance.PartyBtnDelete,
            () => partyViewBtn.Click(),
            ct, 4, 500);
        if (!menu)
            throw new PartySetupFailedException("未能打开队伍选择页面");

        Region? partyDeleteBtn = null;
        var success = await NewRetry.WaitForAction(() =>
        {
            using var ocrRa = CaptureToRectArea();
            partyDeleteBtn = ocrRa.Find(ElementAssets.Instance.PartyBtnDelete);
            return partyDeleteBtn.IsExist();
        }, ct, 5);

        if (!success || partyDeleteBtn == null)
            throw new PartySetupFailedException("未能打开队伍配置界面");

        return partyDeleteBtn;
    }

    /// <summary>
    /// 滚动列表到最上方
    /// </summary>
    private static async Task ScrollToTop(CancellationToken ct)
    {
        await Task.Delay(50, ct);
        GameCaptureRegion.GameRegion1080PPosClick(700, 125);
        await Task.Delay(50, ct);
        Simulation.SendInput.Mouse.LeftButtonDown();
        await Task.Delay(450, ct);
        Simulation.SendInput.Mouse.LeftButtonUp();
        await Task.Delay(100, ct);
    }

    /// <summary>
    /// 清理 OCR 识别结果中的干扰字符
    /// </summary>
    private static string CleanOcrText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        var cleaned = text.Replace("\"", "").Replace("\r\n", "").Replace("\r", "");
        var newLineIndex = cleaned.IndexOf('\n');
        if (newLineIndex != -1)
            cleaned = cleaned[..newLineIndex];
        return cleaned.Trim();
    }

    private async Task ConfirmParty(ImageRegion page, CancellationToken ct, bool isInPartyViewUi = false)
    {
        Bv.ClickWhiteConfirmButton(page.DeriveCrop(0, page.Height / 4, page.Width / 4, page.Height - page.Height / 4));
        var partyChooseUiClosed = await NewRetry.WaitForAction(() =>
        {
            using var ra2 = CaptureToRectArea();
            return ra2.Find(ElementAssets.Instance.PartyBtnDelete).IsEmpty();
        }, ct, 10);
        if (!partyChooseUiClosed)
        {
            throw new PartySetupFailedException("选择队伍失败，等待队伍切换超时！");
        }

        await Delay(200, ct);
        using var ra = CaptureToRectArea();
        Bv.ClickWhiteConfirmButton(ra.DeriveCrop(page.Width - page.Width / 4, page.Height / 4, page.Width / 4, page.Height - page.Height / 4));
        await Delay(500, ct);
        if (isInPartyViewUi) await _returnMainUiTask.Start(ct);
    }
}
