using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.QuickTeleport.Assets;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

internal class GoToSereniteaPotTask
{
    public string Name => "领取尘歌壶奖励";

    private bool fail = false;
    private readonly ChooseTalkOptionTask _chooseTalkOptionTask = new();

    private readonly string ayuanHeyString;
    private readonly string ayuanBelieveString;
    private readonly string ayuanShopString;
    private readonly string ayuanByeString;

    public GoToSereniteaPotTask()
    {
        IStringLocalizer<GoToSereniteaPotTask> stringLocalizer = App.GetService<IStringLocalizer<GoToSereniteaPotTask>>() ?? throw new NullReferenceException();
        CultureInfo cultureInfo = new CultureInfo(TaskContext.Instance().Config.OtherConfig.GameCultureInfoName);
        this.ayuanHeyString = stringLocalizer.WithCultureGet(cultureInfo, "阿圆");
        this.ayuanBelieveString = stringLocalizer.WithCultureGet(cultureInfo, "信任");
        this.ayuanShopString = stringLocalizer.WithCultureGet(cultureInfo, "洞天百宝");
        this.ayuanByeString = stringLocalizer.WithCultureGet(cultureInfo, "再见");
    }

    public async Task Start(CancellationToken ct)
    {
        try
        {
            await DoOnce(ct);
        }
        catch (Exception e)
        {
            Logger.LogDebug(e, "领取尘歌壶奖励异常");
            Logger.LogError("领取尘歌壶奖励异常: {Msg}", e.Message);
        }
    }

    private async Task IntoSereniteaPot(CancellationToken ct)
    {
        // 退出到主页面
        await new ReturnMainUiTask().Start(ct);

        await Delay(200, ct);

        TaskContext.Instance().PostMessageSimulator.SimulateAction(GIActions.OpenMap); // 打开地图
        await Delay(900, ct);
        // 进入 壶
        await ChangeCountryForce("尘歌壶", ct);
        // 若未找到 ElementAssets.Instance.SereniteaPotRo 就是已经在尘歌壶了
        var ra = CaptureToRectArea();
        for (int i = 0; i < 3; i++)
        {
            var sereniteaPotHomeIcon = ra.Find(ElementAssets.Instance.SereniteaPotHomeRo);
            if (!sereniteaPotHomeIcon.IsExist())
            {
                Logger.LogInformation("领取尘歌壶奖励:{text}", "住宅图标未找到，调整地图缩放至3.0。");
                await new Core.Script.Dependence.Genshin().SetBigMapZoomLevel(3.0);
            }
            else
            {
                sereniteaPotHomeIcon.Click();
                await Delay(500, ct);
                break;
            }
        }

        ra = CaptureToRectArea();
        var teleportBtn = ra.Find(QuickTeleportAssets.Instance.TeleportButtonRo);
        if (!teleportBtn.IsExist())
        {
            var teleportSereniteaPotHome = ra.Find(ElementAssets.Instance.TeleportSereniteaPotHomeRo);
            if (teleportSereniteaPotHome.IsExist())
            {
                teleportSereniteaPotHome.Click();
            }
        }

        ra = CaptureToRectArea();
        teleportBtn = ra.Find(QuickTeleportAssets.Instance.TeleportButtonRo);
        if (teleportBtn.IsExist())
        {
            teleportBtn.Click();
        }

        await NewRetry.WaitForAction(() => Bv.IsInMainUi(CaptureToRectArea()), ct);
    }


    // 寻找阿圆并靠近
    private async Task FindAYuan(CancellationToken ct)
    {
        Logger.LogInformation("领取尘歌壶奖励:{text}", "寻找阿圆");
        CancellationTokenSource treeCts = new();
        ct.Register(treeCts.Cancel);
        // 中键回正视角
        Simulation.SendInput.Mouse.MiddleButtonClick();
        await Delay(900, ct);
        int continuousCount = 0;
        while (!ct.IsCancellationRequested)
        {
            var ra = CaptureToRectArea();
            var list = ra.FindMulti(new RecognitionObject
            {
                RecognitionType = RecognitionTypes.Ocr,
                RegionOfInterest = new Rect(ra.Width / 5, ra.Height / 10, (int)(ra.Width * 0.65), ra.Height / 2)
            });
            Region? ayuanIcon = list.FirstOrDefault(r => r.Text.Length == ayuanHeyString.Length && r.Text.Contains(ayuanHeyString));
            if (ayuanIcon == null)
            {
                Simulation.SendInput.Mouse.MoveMouseBy(ra.Width / 3, 0);
                continuousCount++;
            }
            else
            {
                // 判断阿圆的icon 是否在屏幕上四分之一 避免角色遮挡
                if ((ayuanIcon.Height / 2 + ayuanIcon.Y) > (ra.Height / 4))
                {
                    var moveY = (ayuanIcon.Height / 2 + ayuanIcon.Y) - (ra.Height / 4) + 100; // 加个偏移，快速收敛
                    Simulation.SendInput.Mouse.MoveMouseBy(0, (int)(moveY * TaskContext.Instance().DpiScale));
                    await Delay(300, ct);
                    continue;
                }

                var middle = ra.Width / 2;
                var ayuanMiddle = ayuanIcon.X + ayuanIcon.Width / 2;
                if (Math.Abs(middle - ayuanMiddle) > ayuanIcon.Width / 4)
                {
                    Simulation.SendInput.Mouse.MoveMouseBy((ayuanMiddle - middle) / 2, 0);
                }
                else
                {
                    Logger.LogInformation("领取尘歌壶奖励:{text}", "寻找阿圆成功");
                    break;
                }
            }

            await Delay(500, ct);
            if (continuousCount > 24)
            {
                fail = true;
                Logger.LogWarning("领取尘歌壶奖励:{text}", "寻找阿圆失败");
                return;
            }
        }

        TaskContext.Instance().PostMessageSimulator.SimulateAction(GIActions.MoveForward, KeyType.KeyDown); // 向前走
        Logger.LogInformation("领取尘歌壶奖励:{text}", "接近阿圆");
        var findDialog = new Task(async () =>
        {
            while (!treeCts.IsCancellationRequested)
            {
                if (Bv.FindF(CaptureToRectArea(), text: this.ayuanHeyString))
                {
                    TaskContext.Instance().PostMessageSimulator.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                    Logger.LogInformation("领取尘歌壶奖励:{text}", "接近阿圆成功");
                    treeCts.Cancel();
                    break;
                }

                await Delay(50, treeCts.Token);
            }
        }, treeCts.Token);
        findDialog.Start();
        await Task.WhenAll(findDialog);
    }

    private async Task BuyMaxNumber(CancellationToken ct)
    {
        var ra = CaptureToRectArea();
        var list = ra.FindMulti(new RecognitionObject
        {
            RecognitionType = RecognitionTypes.Ocr,
            RegionOfInterest = new Rect((int)(ra.Width * 0.7), (int)(ra.Height * 0.35), (int)(ra.Width * 0.2), (int)(ra.Height * 0.15))
        });
        IStringLocalizer<MapLazyAssets> stringLocalizer = App.GetService<IStringLocalizer<MapLazyAssets>>() ?? throw new NullReferenceException(nameof(stringLocalizer));
        CultureInfo cultureInfo = new CultureInfo(TaskContext.Instance().Config.OtherConfig.GameCultureInfoName);
        string shopOff = stringLocalizer.WithCultureGet(cultureInfo, "已售");
        var shopOffRo = list.FirstOrDefault(r => r.Text.Contains(shopOff));
        if (shopOffRo != null)
        {
            Logger.LogInformation("领取尘歌壶奖励:{text}", "商店物品售空");
            return;
        }

        Logger.LogInformation("领取尘歌壶奖励:{text}", "购买商店物品最大数量");
        var numberBtn = ra.Find(ElementAssets.Instance.SereniteapotShopNumberBtn);
        if (numberBtn.IsExist())
        {
            numberBtn.Move();
            await Delay(300, ct);
            Simulation.SendInput.Mouse.LeftButtonDown();
            await Delay(300, ct);
            Simulation.SendInput.Mouse.MoveMouseBy(numberBtn.Width * 15, 0);
            await Delay(300, ct);
            Simulation.SendInput.Mouse.LeftButtonUp();
        }

        await Delay(300, ct);
        ra.Find(ElementAssets.Instance.BtnWhiteConfirm).Click();
        await Delay(500, ct);
        ra.Find(ElementAssets.Instance.BtnWhiteConfirm).Click();
    }

    private async Task GetReward(CancellationToken ct)
    {
        // 保证与阿圆对话
        await NewRetry.WaitForAction(() => Bv.FindFAndPress(CaptureToRectArea(), text: this.ayuanHeyString), ct);
        //var ra = CaptureToRectArea();
        //Bv.FindFAndPress(ra,text:this.ayuanHeyString); // 开始对话
        await Delay(500, ct);
        // 领取奖励
        var rewardOption = await _chooseTalkOptionTask.SingleSelectText(this.ayuanBelieveString, ct);
        if (rewardOption == TalkOptionRes.FoundAndClick)
        {
            Logger.LogInformation("领取尘歌壶奖励:{text}", "领取好感和宝钱");
            await Delay(1000, ct);
            CaptureToRectArea().Find(ElementAssets.Instance.SereniteaPotLoveRo, a => a.Click());
            await Delay(500, ct);
            var ra = CaptureToRectArea();
            var list = ra.FindMulti(new RecognitionObject
            {
                RecognitionType = RecognitionTypes.Ocr,
                RegionOfInterest = new Rect((int)(ra.Width * 0.35), (int)(ra.Height * 0.45), (int)(ra.Width * 0.3), (int)(ra.Height * 0.05))
            });
            var tem = list.FirstOrDefault(a => a.Text.Contains("无法领取好感经验"));
            if (tem != null)
            {
                tem.Click();
                await Delay(200, ct);
            }

            if (CaptureToRectArea().Find(ElementAssets.Instance.SereniteapotPageClose, a => a.Click()).IsExist())
            {
                await Delay(500, ct);
            }

            CaptureToRectArea().Find(ElementAssets.Instance.SereniteaPotMoneyRo, a => a.Click());
            await Delay(500, ct);
            CaptureToRectArea().Find(ElementAssets.Instance.SereniteapotPageClose, a => a.Click());
            await Delay(500, ct);
            CaptureToRectArea().Find(ElementAssets.Instance.PageCloseWhiteRo).Click();
        }

        await Delay(900, ct);
        // 商店购买
        var shopOption = await _chooseTalkOptionTask.SingleSelectText(this.ayuanShopString, ct);
        if (shopOption == TalkOptionRes.FoundAndClick)
        {
            Logger.LogInformation("领取尘歌壶奖励:{text}", "购买商店物品");
            await Delay(500, ct);
            // 购买的物品清单
            var buy = new List<RecognitionObject>()
            {
                ElementAssets.Instance.AYuanExpBottleBigRo,
                ElementAssets.Instance.AYuanExpBottleSmallRo,
                ElementAssets.Instance.SereniteapotExpBookRo,
                ElementAssets.Instance.SereniteapotExpBookSmallRo,
            };
            // 直接购买最大数量
            foreach (var item in buy)
            {
                var itemRo = CaptureToRectArea().Find(item);
                if (itemRo.IsExist())
                {
                    itemRo.Click();
                    await Delay(500, ct);
                    await BuyMaxNumber(ct);
                    await Delay(500, ct);
                }
            }

            await Delay(900, ct);
            Logger.LogInformation("领取尘歌壶奖励:{text}", "购买商店物品完成");
            // 购买完成 关闭page
            CaptureToRectArea().Find(ElementAssets.Instance.PageCloseWhiteRo, a => a.Click());
        }

        await Delay(900, ct);
    }

    // 处理最后收尾操作
    private async Task Finished(CancellationToken ct)
    {
        var isMainUi = false;
        Logger.LogInformation("领取尘歌壶奖励:{text}", "退出到主页");
        // 识别page 关闭按钮。
        if (CaptureToRectArea().Find(ElementAssets.Instance.PageCloseWhiteRo, a => a.Click()).IsExist())
        {
            await Delay(1000, ct);
        }

        var quitOption = await _chooseTalkOptionTask.SingleSelectText(this.ayuanByeString, ct);
        if (quitOption != TalkOptionRes.FoundAndClick)
        {
            if (!Bv.IsInMainUi(CaptureToRectArea()))
            {
                Logger.LogError("领取尘歌壶奖励:{text}", "阿圆对话框退出出错。");
                return;
            }
            else
            {
                isMainUi = true;
            }
        }

        if (!isMainUi)
        {
            await Delay(300, ct);
            await NewRetry.WaitForAction(() =>
            {
                var ra = CaptureToRectArea();
                if (!Bv.IsInMainUi(ra))
                {
                    ra.Click();
                    return false;
                }
                else
                    return true;
            }, ct);
        }

        // TP回主世界
        var tp = new TpTask(ct);
        await tp.Tp(4508.97509765625, 3630.557373046875); // TP到枫丹
    }

    private async Task ChangeCountryForce(string country, CancellationToken ct)
    {
        GameCaptureRegion.GameRegionClick((rect, scale) => (rect.Width - 160 * scale, rect.Height - 60 * scale));
        await Delay(500, ct);
        using var ra = CaptureToRectArea();
        var list = ra.FindMulti(new RecognitionObject
        {
            RecognitionType = RecognitionTypes.Ocr,
            RegionOfInterest = new Rect(ra.Width / 2, 0, ra.Width / 2, ra.Height)
        });
        IStringLocalizer<MapLazyAssets> stringLocalizer = App.GetService<IStringLocalizer<MapLazyAssets>>() ?? throw new NullReferenceException(nameof(stringLocalizer));
        CultureInfo cultureInfo = new CultureInfo(TaskContext.Instance().Config.OtherConfig.GameCultureInfoName);
        string minCountryLocalized = stringLocalizer.WithCultureGet(cultureInfo, country);
        string commissionLocalized = stringLocalizer.WithCultureGet(cultureInfo, "委托");
        Region? matchRect = list.FirstOrDefault(r => r.Text.Length == minCountryLocalized.Length && !r.Text.Contains(commissionLocalized) && r.Text.Contains(minCountryLocalized));
        if (matchRect == null)
        {
            Logger.LogWarning("切换区域失败：{Country}", country);
        }
        else
        {
            matchRect.Click();
            Logger.LogInformation("切换到区域：{Country}", country);
        }

        await Delay(500, ct);
    }

    public async Task DoOnce(CancellationToken ct)
    {
        /**
         * 1. 首先退出到主页面
         * 2. 进入尘歌壶
         * 3. 旋转视角寻找 阿圆
         * 4. 贴近阿圆到能对话的地方，并对话
         * 5. 领取奖励
         */
        // 进入尘歌壶
        await IntoSereniteaPot(ct);
        // 寻找阿圆并靠近
        await FindAYuan(ct);
        // 领取奖励
        if (fail)
        {
            await Finished(ct);
            return;
        }

        await Delay(500, ct);
        await GetReward(ct);

        // 收尾操作 - 退出到主页面 - 传送到提瓦特大陆
        await Finished(ct);
    }
}