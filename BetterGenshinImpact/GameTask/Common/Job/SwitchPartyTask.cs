using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.Common.Exceptions;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using System.Text.RegularExpressions;

namespace BetterGenshinImpact.GameTask.Common.Job;

public class SwitchPartyTask
{
    private readonly double _assetScale = TaskContext.Instance().SystemInfo.AssetScale;

    public string Name => "切换队伍";

    private readonly ReturnMainUiTask _returnMainUiTask = new();

    public async Task<bool> Start(string partyName, CancellationToken ct)
    {
        Logger.LogInformation("尝试切换至队伍: {Name}", partyName);
        using var ra1 = CaptureToRectArea();

        if (!Bv.IsInPartyViewUi(ra1))
        {
            // 如果不在主界面，则返回主界面
            if (!Bv.IsInMainUi(ra1))
            {
                await _returnMainUiTask.Start(ct);
                await Delay(200, ct);
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

                // 考虑加载时间 2s，共检查 2.5s，如果失败则抛出异常
                
                for (int i = 0; i < 10; i++) // 检查 3 次
                {
                    await Delay(600, ct);
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

        await Delay(500, ct);

        using var ra = CaptureToRectArea();
        var partyViewBtn = ra.Find(ElementAssets.Instance.PartyBtnChooseView);

        // OCR 当前队伍名称（无法单字，中间禁止空格）
        var currTeamName = ra.Find(new RecognitionObject
        {
            RecognitionType = RecognitionTypes.Ocr,
            RegionOfInterest = new Rect(partyViewBtn.Right, partyViewBtn.Top, (int)(350 * _assetScale),
                partyViewBtn.Height)
        }).Text;

        Logger.LogInformation("切换队伍，当前队伍名称: {Text}，使用正则表达式规则进行模糊匹配", currTeamName);
        if (Regex.IsMatch(currTeamName, partyName))
        {
            Logger.LogInformation("当前队伍[{Name}]即为目标队伍，无需切换", currTeamName);
            Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
            await Delay(500, ct);
            await _returnMainUiTask.Start(ct);
            return true;
        }

        // 点击队伍选择按钮
        partyViewBtn.Click();
        await Delay(500, ct);

        ImageRegion? switchRa = null;
        Region? partyDeleteBtn = null;
        using (var ocrRa = CaptureToRectArea())
        {
            var openPartyChooseSuccess = await NewRetry.WaitForAction(() =>
            {
                switchRa = ocrRa;
                partyDeleteBtn = switchRa.Find(ElementAssets.Instance.PartyBtnDelete);
                return partyDeleteBtn.IsExist();
            }, ct, 5);

            if (!openPartyChooseSuccess || switchRa == null || partyDeleteBtn == null)
            {
                throw new PartySetupFailedException("未能打开队伍配置界面");
            }
        }

        var nextX = partyDeleteBtn.Left;
        var nextY = partyDeleteBtn.Top - partyDeleteBtn.Height * 2;

        // 点击到最上方
        await Task.Delay(50, ct);
        GameCaptureRegion.GameRegion1080PPosClick(700, 120);
        await Task.Delay(50, ct);
        Simulation.SendInput.Mouse.LeftButtonDown();
        await Task.Delay(450, ct);
        Simulation.SendInput.Mouse.LeftButtonUp();

        // 逐页查找
        for (var i = 0; i < 11; i++)
        {
            using var page = CaptureToRectArea();
            var found = await FindPage(partyName, page, partyDeleteBtn, ct);
            if (found)
            {
                RunnerContext.Instance.ClearCombatScenes();
                return true;
            }

            // 点击下一页
            if (i == 0)
            {
                // #ebe4d8 首次点一下第一个，防止第五个被点击过
                page.ClickTo(600 * _assetScale, 200 * _assetScale);
            }

            page.ClickTo(nextX, nextY); // 点击最下方队伍下移
            await Delay(400, ct);
        }

        // 未找到
        Logger.LogError("未找到队伍: {Name}，返回主界面", partyName);
        Logger.LogInformation("如果找不到设定的队伍名，有可能是文字识别效果不佳，请尝试正则表达式");
        await _returnMainUiTask.Start(ct);
        return false;

    }

    private async Task<bool> FindPage(string partyName, ImageRegion page, Region partyDeleteBtn, CancellationToken ct)
    {
        var partySwitchNameRaList = page.FindMulti(new RecognitionObject
        {
            RecognitionType = RecognitionTypes.Ocr,
            RegionOfInterest = new Rect(0, (int)(80 * _assetScale), partyDeleteBtn.Right, partyDeleteBtn.Top - (int)(80 * _assetScale))
        });

        // 当前页存在则直接点击
        foreach (var textRegion in partySwitchNameRaList)
        {
            if (Regex.IsMatch(textRegion.Text, partyName))
            {
                page.ClickTo(textRegion.Right + textRegion.Width, textRegion.Bottom);
                await Delay(200, ct);
                Logger.LogInformation("切换队伍成功: {Text}", textRegion.Text);
                await ConfirmParty(page, ct);
                return true;
            }
        }

        return false;
    }

    private async Task ConfirmParty(ImageRegion page, CancellationToken ct)
    {
        var r1 = Bv.ClickWhiteConfirmButton(page.DeriveCrop(0, page.Height / 4, page.Width / 4, page.Height - page.Height / 4));
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
        var r2 = Bv.ClickWhiteConfirmButton(ra.DeriveCrop(page.Width - page.Width / 4, page.Height / 4, page.Width / 4, page.Height - page.Height / 4));
        await Delay(500, ct);
        await _returnMainUiTask.Start(ct);
    }
}