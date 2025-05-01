using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using BetterGenshinImpact.GameTask.Common.Job;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.Core.Script.Dependence;
using BetterGenshinImpact.GameTask.QuickTeleport.Assets;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact;

namespace GameTask.Common.Job;

internal class GoToSereniteaPotTask
{
    private readonly ReturnMainUiTask _returnMainUiTask = new();
    private readonly QuickTeleportAssets _teleportAssets;

    public string Name => "领取尘歌壶奖励";

    private bool fail = false;

    public GoToSereniteaPotTask()
    {
        _teleportAssets = QuickTeleportAssets.Instance;
    }

    public async Task Start( CancellationToken ct)
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

    private async Task IntoSereniteaPot(CancellationToken ct) {
        // 退出到主页面
        await _returnMainUiTask.Start(ct);

        await Delay(200, ct);

        TaskContext.Instance().PostMessageSimulator.SimulateAction(GIActions.OpenMap); // 打开地图
        await Delay(900, ct);
        // 进入 壶
        var ra = CaptureToRectArea();
        var click_w = ra.Width * 0.958;
        var click_h = ra.Height * 0.944;
        ra.ClickTo(click_w, click_h);
        await Delay(500, ct);

        ra = CaptureToRectArea();
        var SereniteaPotIcon = ra.Find(ElementAssets.Instance.SereniteaPotRo);
        if (SereniteaPotIcon.IsExist())
        {
            SereniteaPotIcon.Click();
            await Delay(500, ct);
        }
        // 若未找到 ElementAssets.Instance.SereniteaPotRo 就是已经在尘歌壶了
        ra = CaptureToRectArea();
        for (int i = 0; i < 3; i++)
        {
            var SereniteaPotHomeIcon = ra.Find(ElementAssets.Instance.SereniteaPotHomeRo);
            if (!SereniteaPotHomeIcon.IsExist())
            {
                Logger.LogInformation("领取尘歌壶奖励:{text}", "住宅图标未找到，调整地图缩放至3.0。");
                await new Genshin().SetBigMapZoomLevel(3.0);
            }
            else
            {
                SereniteaPotHomeIcon.Click(); await Delay(500, ct);
                break;
            }
        }
        ra = CaptureToRectArea();
        var teleportBtn = ra.Find(_teleportAssets.TeleportButtonRo);
        if (!teleportBtn.IsExist())
        {
            var TeleportSereniteaPotHome = ra.Find(ElementAssets.Instance.TeleportSereniteaPotHomeRo);
            if (TeleportSereniteaPotHome.IsExist()) { TeleportSereniteaPotHome.Click(); }
        }
        ra = CaptureToRectArea();
        teleportBtn = ra.Find(_teleportAssets.TeleportButtonRo);
        if (teleportBtn.IsExist()) { teleportBtn.Click(); }
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
            var ayuanIcon = ra.Find(ElementAssets.Instance.AYuanIconRo);
            if (!ayuanIcon.IsExist()) { Simulation.SendInput.Mouse.MoveMouseBy(ra.Width/3, 0); continuousCount++; }
            else
            {
                var middle = ra.Width / 2;
                var ayuanMiddle = ayuanIcon.X + ayuanIcon.Width / 2;
                if(Math.Abs(middle - ayuanMiddle) > ayuanIcon.Width / 4)
                {
                    Simulation.SendInput.Mouse.MoveMouseBy((ayuanMiddle - middle)/2, 0);
                }
                else
                {
                    Logger.LogInformation("领取尘歌壶奖励:{text}", "寻找阿圆成功");
                    break;
                }
            }
            await Delay(300, ct);
            if (continuousCount > 24) {
                fail = true;
                Logger.LogWarning("领取尘歌壶奖励:{text}", "寻找阿圆失败");
                return;
            }
        }

        TaskContext.Instance().PostMessageSimulator.SimulateAction(GIActions.MoveForward,KeyType.KeyDown); // 向前走
        Logger.LogInformation("领取尘歌壶奖励:{text}", "接近阿圆");
        var findDialog = new Task(async () =>
        {
            while (!treeCts.IsCancellationRequested) {
                var aYuanDialog = CaptureToRectArea().Find(ElementAssets.Instance.AYuanHeyRo);
                if (aYuanDialog.IsExist())
                {
                    TaskContext.Instance().PostMessageSimulator.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                    treeCts.Cancel();
                    break;
                    //TaskContext.Instance().PostMessageSimulator.SimulateAction(GIActions.PickUpOrInteract);
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
        var numberBtn = ra.Find(ElementAssets.Instance.SereniteapotShopNumberBtn);
        if (numberBtn.IsExist())
        {
            numberBtn.Move();
            //Simulation.SendInput.Mouse.MoveMouseTo(numberBtn.X + numberBtn.Width / 2, numberBtn.Y + numberBtn.Height / 2);
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
        var ra = CaptureToRectArea();
        TaskContext.Instance().PostMessageSimulator.SimulateAction(GIActions.PickUpOrInteract); // 开始对话
        await NewRetry.WaitForAction(() => {
            ra.Click();
            return CaptureToRectArea().Find(ElementAssets.Instance.AYuanBelieveLevelRo).IsExist();
            }, ct); // 等待对话选项

        // 领取奖励
        if(await NewRetry.WaitForAction(()=> CaptureToRectArea().Find(ElementAssets.Instance.AYuanBelieveLevelRo).IsExist(), ct,50, 100))
        {
            Logger.LogInformation("领取尘歌壶奖励:{text}", "领取好感和宝钱");
            await Delay(900, ct);
            CaptureToRectArea().Find(ElementAssets.Instance.AYuanBelieveLevelRo, a => a.Click());
            await Delay(1000, ct);
            CaptureToRectArea().Find(ElementAssets.Instance.SereniteaPotLoveRo, a => a.Click());
            await Delay(500, ct);
            CaptureToRectArea().Find(ElementAssets.Instance.SereniteapotPageClose, a => a.Click());
            await Delay(500, ct);
            CaptureToRectArea().Find(ElementAssets.Instance.SereniteaPotMoneyRo, a => a.Click());
            await Delay(500, ct);
            CaptureToRectArea().Find(ElementAssets.Instance.SereniteapotPageClose, a => a.Click());
            await Delay(500, ct);
            CaptureToRectArea().Find(ElementAssets.Instance.PageCloseWhiteRo).Click();
        }

        // 商店购买
        if (await NewRetry.WaitForAction(() => CaptureToRectArea().Find(ElementAssets.Instance.AYuanShopRo).IsExist(), ct,50, 100))
        {
            Logger.LogInformation("领取尘歌壶奖励:{text}", "购买商店物品");
            await Delay(300, ct);
            CaptureToRectArea().Find(ElementAssets.Instance.AYuanShopRo, a => a.Click());
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
                    await Delay(200, ct);
                    await BuyMaxNumber(ct);
                    await Delay(500, ct);
                }
            }
        }
        await Delay(500, ct);
        // 购买完成 关闭page
        CaptureToRectArea().Find(ElementAssets.Instance.PageCloseWhiteRo, a => a.Click());
        await Delay(500, ct);
    }

    // 处理最后收尾操作
    private async Task Finished(CancellationToken ct)
    {
        Logger.LogInformation("领取尘歌壶奖励:{text}", "退出到主页");
        // 识别page 关闭按钮。
        if (CaptureToRectArea().Find(ElementAssets.Instance.PageCloseWhiteRo, a => a.Click()).IsExist())
        {
            await Delay(1000, ct);
        } 
        if (!await NewRetry.WaitForAction(() => CaptureToRectArea().Find(ElementAssets.Instance.AYuanByeRo, a => a.Click()).IsExist(), ct))
        {
            Logger.LogError("领取尘歌壶奖励:{text}","阿圆对话框退出出错。");
            return;
        }
        await Delay(300, ct);

        await NewRetry.WaitForAction(() => {
            var ra = CaptureToRectArea();
            if (!Bv.IsInMainUi(ra))
            {
                ra.Click();
                return false;
            }
            else
                return true;
        }, ct);

        // 是否要传送到提瓦特大陆？

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
        if (fail) { 
            return;
        }
        await Delay(500, ct);
        await GetReward(ct);

        // 收尾操作 - 退出到主页面
        await Finished(ct);
    }
}
