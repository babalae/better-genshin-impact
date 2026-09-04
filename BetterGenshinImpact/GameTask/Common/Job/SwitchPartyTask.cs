using BetterGenshinImpact.Core.Recognition;
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
    // 换队硬编码延迟系数，所有固定等待时间统一乘以该系数
    private readonly double _delayFactor = TaskContext.Instance().Config.OtherConfig.SwitchPartyHardcodeDelayFactor;

    /// <summary>
    /// 将基础延迟毫秒数乘以换队延迟系数，得到实际等待毫秒数
    /// </summary>
    private int Ms(int baseMs) => Math.Max(1, (int)Math.Round(baseMs * _delayFactor));

    public string Name => "切换队伍";

    private readonly ReturnMainUiTask _returnMainUiTask = new();

    public async Task<bool> Start(string partyName, CancellationToken ct)
    {
        bool isInPartyViewUi = false;

        Logger.LogInformation("尝试切换至队伍: {Name}", partyName);
        using var ra1 = CaptureToRectArea();

        if (!Bv.IsInPartyViewUi(ra1))
        {
            isInPartyViewUi = true;
            // 如果不在主界面，则返回主界面
            if (!Bv.IsInMainUi(ra1))
            {
                await _returnMainUiTask.Start(ct);
                await Delay(Ms(200), ct);
                using var raAfterMain = CaptureToRectArea();
                if (!Bv.IsInMainUi(raAfterMain))
                {
                    throw new InvalidOperationException("未能返回主界面");
                }
            }

            // 尝试打开队伍配置页面
            const int maxAttempts = 2;
            bool isOpened = false;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                Simulation.SendInput.SimulateAction(GIActions.OpenPartySetupScreen);

                // 考虑加载时间 2s，共检查 4.2s，如果失败则抛出异常

                // 每次等待 600*系数 ms，循环次数为保证总等待时间不小于 4200ms 的最小整数
                int waitMs = Ms(600);
                int pollCount = Math.Max(1, (int)Math.Ceiling(4200d / waitMs));
                for (int i = 0; i < pollCount; i++)
                {
                    await Delay(waitMs, ct);
                    using var raCheck = CaptureToRectArea();
                    if (Bv.IsInPartyViewUi(raCheck))
                    {
                        isOpened = true;
                        break;
                    }
                }

                if (isOpened)
                {
                    break; // 页面已打开，跳出循环
                }
            }

            if (!isOpened)
            {
                throw new PartySetupFailedException("未能打开队伍配置界面");
            }
        }

        await Delay(Ms(500), ct);

        using var ra = CaptureToRectArea();
        var partyViewBtn = ra.Find(ElementRecognition.Get("PartyBtnChooseView", ra));

        // OCR 当前队伍名称（无法单字，中间禁止空格）
        var currTeamName = ra.Find(new RecognitionObject
        {
            RecognitionType = RecognitionTypes.Ocr,
            RegionOfInterest = new Rect(partyViewBtn.Right, partyViewBtn.Top, (int)(350 * _assetScale),
                partyViewBtn.Height)
        }).Text;
        
        var tempName = currTeamName
            .Replace("\"", "")        // 移除所有双引号（核心新增，解决日志里的""问题）
            .Replace("\r\n", "")      // 清理Windows换行符
            .Replace("\r", "");   // 先清理所有双引号，避免引号干扰后续处理
                              
        // 核心逻辑：找到第一个换行符(\n)的位置，截断并删除换行+后面所有字符
        int firstNewLineIndex = tempName.IndexOf('\n');
        if (firstNewLineIndex != -1) // 存在换行符，截取到换行符前
        {
            tempName = tempName.Substring(0, firstNewLineIndex);
        }
                          
        // 最后统一去首尾所有空白（空格、制表符、回车符\r等），得到纯净队伍名
        currTeamName = tempName.Trim();

        Logger.LogInformation("切换队伍，当前队伍名称: {Text}，使用正则表达式规则进行模糊匹配", currTeamName);
        if (Regex.IsMatch(currTeamName, partyName))
        {
            Logger.LogInformation("当前队伍[{Name}]即为目标队伍，无需切换", currTeamName);
            if (isInPartyViewUi)
            {
                Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
                await Delay(Ms(500), ct);
                await _returnMainUiTask.Start(ct);
            }

            return true;
        }

        var menu = await NewRetry.WaitForElementAppear(
            ElementRecognition.Get("PartyBtnDelete"),
            () => partyViewBtn.Click(),// 点击队伍选择按钮
            ct,
            4,
            Ms(500)
        );
        if (!menu)
        {
            throw new PartySetupFailedException("未能打开队伍选择页面");
        }

        ImageRegion? switchRa = null;
        Region? partyDeleteBtn = null;
        using (var ocrRa = CaptureToRectArea())
        {
            var openPartyChooseSuccess = await NewRetry.WaitForAction(() =>
            {
                switchRa = ocrRa;
                partyDeleteBtn = switchRa.Find(ElementRecognition.Get("PartyBtnDelete", switchRa));
                return partyDeleteBtn.IsExist();
            }, ct, 5, Ms(1000));

            if (!openPartyChooseSuccess || switchRa == null || partyDeleteBtn == null)
            {
                throw new PartySetupFailedException("未能打开队伍配置界面");
            }
        }

        Rect regionOfInterest = new Rect(0, (int)(80 * _assetScale), partyDeleteBtn.Right, partyDeleteBtn.Top - (int)(80 * _assetScale));
        RecognitionObject recognitionObject = new RecognitionObject
        {
            RecognitionType = RecognitionTypes.Ocr,
            RegionOfInterest = regionOfInterest,
            DrawOnWindow = true,
            Name = "队伍名称",
            DrawOnWindowPen = System.Drawing.Pens.White
        };

        // 打开菜单后先识别当前可见页，目标队伍在当前页则直接切换，无需回顶部
        try
        {
            using (var currentPage = CaptureToRectArea())
            {
                var currentPageNameList = currentPage.FindMulti(recognitionObject);
                if (currentPageNameList != null && currentPageNameList.Count > 0
                    && await TrySwitchToPartyOnPage(currentPage, currentPageNameList, partyName, ct, isInPartyViewUi))
                {
                    return true;
                }
            }
        }
        finally
        {
            // 无论成功、确认超时还是取消，都清理 OCR 绘制，避免残留影响后续任务
            VisionContext.Instance().DrawContent.ClearAll();
        }

        // 点击到最上方
        await Task.Delay(Ms(50), ct);
        GameCaptureRegion.GameRegion1080PPosClick(700, 125);
        await Task.Delay(Ms(50), ct);
        Simulation.SendInput.Mouse.LeftButtonDown();
        await Task.Delay(Ms(450), ct);
        Simulation.SendInput.Mouse.LeftButtonUp();
        await Task.Delay(Ms(100), ct);

        // 逐页查找
        int bottomHitCount = 0;   // 连续判定到底的累计次数
        try
        {
            for (var i = 0; i < 16; i++)    // 6.0版本最多20个队伍
            {
                using var page = CaptureToRectArea();

                var partySwitchNameRaList = page.FindMulti(recognitionObject);

                if (partySwitchNameRaList == null || partySwitchNameRaList.Count <= 0)
                {
                    Logger.LogInformation("管理队伍界面文字识别失败");
                    break;
                }

                // 当前页存在则直接点击
                if (await TrySwitchToPartyOnPage(page, partySwitchNameRaList, partyName, ct, isInPartyViewUi))
                {
                    return true;
                }

                Region lowest = partySwitchNameRaList.Where(r => r.X > 35 * _assetScale && r.X < 100 * _assetScale).OrderBy(r => r.Y).Last();
                lowest.DrawSelf("底部的队伍");

                if (lowest.Y < 777 * _assetScale)   // 如果最底下是空队伍则不会有队伍名，以此判断是否已遍历完成
                {
                    // 需要累计 3 次连续判定到底才停止，避免识别抖动造成过早退出
                    bottomHitCount++;
                    if (bottomHitCount >= 3)
                    {
                        Logger.LogInformation("已连续 3 次判定到底，确认抵达最后一个队伍");
                        break;
                    }
                    Logger.LogInformation("底部判定第 {Count}/3 次，继续向下滚动确认", bottomHitCount);
                }
                else
                {
                    bottomHitCount = 0;   // 未到底则清零，要求连续 3 次
                }

                // 点击下一页
                if (i == 0)
                {
                    // #ebe4d8 首次点一下第一个，防止第五个被点击过
                    page.ClickTo(600 * _assetScale, 200 * _assetScale);
                    await Task.Delay(Ms(300), ct); // 等待动画
                }

                // 点击最下方队伍下移，单次滑动距离为配置的栏数（钳制到 1~5 之间，支持小数向上取整）
                double scrollDistance = Math.Clamp(TaskContext.Instance().Config.OtherConfig.SwitchPartyScrollDistance, 1, 5);
                int clickCount = Math.Max(1, (int)Math.Ceiling(scrollDistance));
                for (int s = 0; s < clickCount; s++)
                {
                    page.ClickTo(regionOfInterest.X + regionOfInterest.Width / 2, lowest.Bottom);
                    await Delay(Ms(250), ct);
                }
                // 最后一次点击后再额外等待，等待滚动动画稳定
                await Delay(Ms(150), ct);
            }
        }
        finally
        {
            VisionContext.Instance().DrawContent.ClearAll();
        }

        // 未找到
        Logger.LogError("未找到队伍: {Name}，返回主界面", partyName);
        Logger.LogInformation("如果找不到设定的队伍名，有可能是文字识别效果不佳，请尝试正则表达式");
        await _returnMainUiTask.Start(ct);
        return false;
    }

    /// <summary>
    /// 在当前页 OCR 出的队伍名列表中查找目标队伍，找到则点击并确认切换
    /// </summary>
    private async Task<bool> TrySwitchToPartyOnPage(ImageRegion page, List<Region> partySwitchNameRaList, string partyName, CancellationToken ct, bool isInPartyViewUi)
    {
        foreach (var textRegion in partySwitchNameRaList)
        {
            if (Regex.IsMatch(textRegion.Text, partyName))
            {
                page.ClickTo(textRegion.Right + textRegion.Width, textRegion.Bottom);
                await Delay(Ms(200), ct);
                Logger.LogInformation("切换队伍成功: {Text}", textRegion.Text);
                await ConfirmParty(page, ct, isInPartyViewUi);

                RunnerContext.Instance.ClearCombatScenes();
                return true;
            }
        }

        return false;
    }

    private async Task ConfirmParty(ImageRegion page, CancellationToken ct, bool isInPartyViewUi = false)
    {
        var r1 = Bv.ClickWhiteConfirmButton(page, new Rect(0, page.Height / 4, page.Width / 4, page.Height - page.Height / 4));
        var partyChooseUiClosed = await NewRetry.WaitForAction(() =>
        {
            using var ra2 = CaptureToRectArea();
            return ra2.Find(ElementRecognition.Get("PartyBtnDelete", ra2)).IsEmpty();
        }, ct, 10, Ms(1000));
        if (!partyChooseUiClosed)
        {
            throw new PartySetupFailedException("选择队伍失败，等待队伍切换超时！");
        }
        await Delay(Ms(200), ct);
        using var ra = CaptureToRectArea();
        var r2 = Bv.ClickWhiteConfirmButton(ra, new Rect(page.Width - page.Width / 4, page.Height / 4, page.Width / 4, page.Height - page.Height / 4));
        await Delay(Ms(500), ct);
        if (isInPartyViewUi) await _returnMainUiTask.Start(ct);
    }
}
