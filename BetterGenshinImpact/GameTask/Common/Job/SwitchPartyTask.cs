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
        Logger.LogInformation("尝试切换至队伍: {Name}", partyName);
        using var ra1 = CaptureToRectArea();

        if (!Bv.IsInPartyViewUi(ra1))
        {
            if (!Bv.IsInMainUi(ra1))
            {
                await _returnMainUiTask.Start(ct);
                await Delay(200, ct);
            }

            Simulation.SendInput.SimulateAction(GIActions.OpenPartySetupScreen);
            await Delay(1000, ct); // 加载2s // 由于胡桃可以不等待直接进入，所以这里只等待1s
        }

        if (await Bv.WaitForPartyViewUi(ct))
        {
            await Delay(500, ct);

            using var ra = CaptureToRectArea();
            var partyViewBtn = ra.Find(ElementAssets.Instance.PartyBtnChooseView);
            if (!partyViewBtn.IsExist())
            {
                throw new Exception("补充判定失败，未能打开队伍界面");
            }

            // OCR 当前队伍名称（无法单字，中间禁止空格）
            var currTeamNameRaList = ra.FindMulti(new RecognitionObject
            {
                RecognitionType = RecognitionTypes.Ocr,
                RegionOfInterest = new Rect(partyViewBtn.Right, partyViewBtn.Top, (int)(350 * _assetScale), partyViewBtn.Height)
            });

            var currTeamName = string.Join("", currTeamNameRaList.Select(x => x.Text).Where(x => !string.IsNullOrWhiteSpace(x)));
            // Logger.LogInformation("切换队伍，当前队伍名称: {Text}", currTeamName);
            if (currTeamName == partyName)
            {
                Logger.LogInformation("切换队伍，当前队伍[{Name}]即为目标队伍，无需切换", partyName);
                Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
                await Delay(500, ct);
                await _returnMainUiTask.Start(ct);
                return true;
            }

            // 点击队伍选择按钮
            partyViewBtn.Click();
            await Delay(500, ct);

            using var switchRa = CaptureToRectArea();
            var partyDeleteBtn = switchRa.Find(ElementAssets.Instance.PartyBtnDelete);
            if (!partyDeleteBtn.IsExist())
            {
                throw new Exception("未能打开队伍选择界面");
            }

            var nextX = partyDeleteBtn.Left;
            var nextY = partyDeleteBtn.Top - partyDeleteBtn.Height * 2;

            // 滚轮到最上方
            switchRa.MoveTo(nextX, nextY);
            await Delay(10, ct);
            for (var i = 0; i < 100; i++)
            {
                Simulation.SendInput.Mouse.VerticalScroll(1);
                await Delay(10, ct);
            }

            await Delay(200, ct);

            // 逐页查找
            for (var i = 0; i < 11; i++)
            {
                var page = CaptureToRectArea();
                var found = await FindPage(partyName, page, partyDeleteBtn, ct);
                if (found)
                {
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
            await _returnMainUiTask.Start(ct);
            return false;
        }
        else
        {
            Logger.LogError("未能打开队伍界面");
            return false;
        }
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
            if (textRegion.Text == partyName)
            {
                page.ClickTo(textRegion.Right, textRegion.Bottom + textRegion.Height * 2);
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
        await Delay(1000, ct);
        using var ra = CaptureToRectArea();
        var r2 = Bv.ClickWhiteConfirmButton(ra.DeriveCrop(page.Width - page.Width / 4, page.Height / 4, page.Width / 4, page.Height - page.Height / 4));
        await Delay(500, ct);
        await _returnMainUiTask.Start(ct);
    }
}