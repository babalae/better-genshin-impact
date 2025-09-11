using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.QuickSereniteaPot;
using BetterGenshinImpact.GameTask.QuickTeleport.Assets;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

internal class GoToSereniteaPotTask
{
    public string Name => "领取尘歌壶奖励";

    private bool fail = false;
    private readonly ChooseTalkOptionTask _chooseTalkOptionTask = new();

    private readonly string ayuanHeyString;
    private readonly string ayuanHuolingString;
    private readonly string ayuanHuoling2String;
    private readonly string ayuanBelieveString;
    private readonly string ayuanShopString;
    private readonly string ayuanByeString;
    private string dongTianName;
    
    private  OneDragonFlowConfig? SelectedConfig;
    private ObservableCollection<OneDragonFlowConfig> ConfigList = [];
    private static readonly string OneDragonFlowConfigFolder = Global.Absolute(@"User\OneDragon");
    

    public GoToSereniteaPotTask()
    {
        IStringLocalizer<GoToSereniteaPotTask> stringLocalizer = App.GetService<IStringLocalizer<GoToSereniteaPotTask>>() ?? throw new NullReferenceException();
        CultureInfo cultureInfo = new CultureInfo(TaskContext.Instance().Config.OtherConfig.GameCultureInfoName);
        this.ayuanHeyString = stringLocalizer.WithCultureGet(cultureInfo, "阿圆");
        this.ayuanHuolingString = stringLocalizer.WithCultureGet(cultureInfo, "壶灵");
        this.ayuanHuoling2String = stringLocalizer.WithCultureGet(cultureInfo, "<壶灵>");
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
        finally
        {
            Simulation.ReleaseAllKey();
        }
    }

    private async Task<bool> IntoSereniteaPot(CancellationToken ct)
    {
        // 退出到主页面
        await new ReturnMainUiTask().Start(ct);

        await Delay(200, ct);

        TaskContext.Instance().PostMessageSimulator.SimulateAction(GIActions.OpenMap); // 打开地图
        await Delay(900, ct);

        // 进入 壶
        TpTask tpTask = new TpTask(ct);
        await tpTask.SwitchArea("尘歌壶");
        
        // 若未找到 ElementAssets.Instance.SereniteaPotRo 就是已经在尘歌壶了
        var  ra = CaptureToRectArea();
        for (int i = 0; i < 5; i++){
            ra = CaptureToRectArea();
            //确定洞天名称
            var list = ra.FindMulti(new RecognitionObject
            {
                RecognitionType = RecognitionTypes.Ocr,
                RegionOfInterest = new Rect((int)(ra.Width * 0.86), ra.Height*9/10, (int)(ra.Width * 0.073), (int)(ra.Height*0.04))
            });
            if (list.Count > 0)
            {
                dongTianName = list[0].Text;
                Logger.LogInformation("领取尘歌壶奖励:{text}", "洞天名称：" + dongTianName);
                await Task.Delay(100, ct);
                break;
            }
            else
            {
                dongTianName = "";
                Logger.LogInformation("领取尘歌壶奖励:{text}", "未识别到洞天名称");
            }
            await Task.Delay(100, ct);
        }

        for (int i = 0; i < 5; i++)
        {
            var sereniteaPotHomeIcon = ra.Find(ElementAssets.Instance.SereniteaPotHomeRo);
            if (!sereniteaPotHomeIcon.IsExist())
            {
                Logger.LogInformation("领取尘歌壶奖励:{text}", "住宅图标未找到，调整地图缩放至2。");
                await Task.Delay(1000, ct);
                await new Core.Script.Dependence.Genshin().SetBigMapZoomLevel(2.5-i*0.2);//尝试缩放地图
                await Task.Delay(1000, ct);
            }
            else
            {
                sereniteaPotHomeIcon.Click();
                await Delay(500, ct);
                break;
            }
        }

        for (int attempt = 0; attempt < 10; attempt++) // 尝试点击传送按钮
        {
            ra = CaptureToRectArea();
            var teleportBtn = ra.Find(QuickTeleportAssets.Instance.TeleportButtonRo);
            if (teleportBtn.IsExist())
            {
                await Delay(300, ct);
                teleportBtn.Click();
                await Delay(500, ct);
        
                bool isReClickRequired = true;
                for(int i = 0; i < 10; i++)     
                {
                    ra = CaptureToRectArea();
                    teleportBtn = ra.Find(QuickTeleportAssets.Instance.TeleportButtonRo);
                    if (!teleportBtn.IsExist())     //传送按钮消失
                    {
                        isReClickRequired = false;
                        break;
                    }
                    await Delay(500, ct);   // 传送按钮还在，等待游戏反应
                }

                if (isReClickRequired)
                {
                    continue;   //传送按钮未消失，再次尝试点击
                }
                break; // 找到并点击传送按钮、确认按钮消失后退出循环
            }
        
            //未找到传送按钮，点击传送住宅按钮
            var teleportSereniteaPotHome = ra.Find(ElementAssets.Instance.TeleportSereniteaPotHomeRo);
            if (teleportSereniteaPotHome.IsExist())
            {
                teleportSereniteaPotHome.Click();
                await Delay(800, ct);  
                continue; // 找到并点击传送住宅按钮后再次点击传送按钮
            }
        
            if (attempt == 9)
            {
                Logger.LogWarning("领取尘歌壶奖励:{text}", "传送至尘歌壶失败");
                return false;
            }
        
            Logger.LogInformation("领取尘歌壶奖励:{text}", "传送按钮、传送住宅按钮未找到，重试");
            await Delay(800, ct);    // 重试间隔
        }
        
        await NewRetry.WaitForAction(() => Bv.IsInMainUi(CaptureToRectArea()), ct);
        return true;
    }

    /// <summary>
    /// 通过背包中的壶进入尘歌壶
    /// </summary>
    /// <param name="ct"></param>
    /// <returns>成功进入壶并初始化壶名称返回 true。</returns>
    private async Task<bool> IntoSereniteaPotByBag(CancellationToken ct)
    {
        // 尝试使用背包的壶进入。
        QuickSereniteaPotTask.Done();
        await Delay(5000, ct); // 在点击壶之后的特殊加载页面会有 mainUI
        await Bv.WaitForMainUi(ct);
        // 判断是否在尘歌壶中
        if (CaptureToRectArea().Find(ElementAssets.Instance.FingerIconRo).IsExist())
        {
            await Delay(1000, ct);
            // 尝试获取尘歌壶名称
            TaskContext.Instance().PostMessageSimulator.SimulateAction(GIActions.OpenMap); // 打开地图
            await Delay(1000, ct);
            for (int i = 0; i < 5; i++)
            {
                var ra = CaptureToRectArea();
                //确定洞天名称
                var list = ra.FindMulti(new RecognitionObject
                {
                    RecognitionType = RecognitionTypes.Ocr,
                    RegionOfInterest = new Rect((int)(ra.Width * 0.86), ra.Height * 9 / 10, (int)(ra.Width * 0.073), (int)(ra.Height * 0.04))
                });
                if (list.Count > 0)
                {
                    dongTianName = list[0].Text;
                    Logger.LogInformation("领取尘歌壶奖励:{text}", "洞天名称：" + dongTianName);
                    await Task.Delay(100, ct);
                    for(int z  = 1; z < 5; z++) { 
                        TaskContext.Instance().PostMessageSimulator.SimulateAction(GIActions.OpenMap); await Delay(1000, ct);
                        if (Bv.IsInMainUi(CaptureToRectArea()))
                        {
                            break;
                        }
                    }
                    await Task.Delay(100, ct);
                    return true;
                }
                else
                {
                    dongTianName = "";
                    Logger.LogInformation("领取尘歌壶奖励:{text}", "未识别到洞天名称");
                }
                await Delay(200, ct);
            }
            return false;
        }
        Logger.LogInformation("领取尘歌壶奖励:未识别到手指");
        return false;
    }

    // 寻找阿圆并靠近
    private async Task FindAYuan(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(dongTianName)){
            await Delay(500, ct);
            switch (dongTianName)
            {
                case "妙香林":
                    Logger.LogInformation("领取尘歌壶奖励:{text}", "在妙香林，调整位置");
                    TaskContext.Instance().PostMessageSimulator.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
                    await Delay(200, ct);
                    TaskContext.Instance().PostMessageSimulator.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                    break;
                case "清琼岛":
                    Logger.LogInformation("领取尘歌壶奖励:{text}", "在清琼岛，调整位置");
                    TaskContext.Instance().PostMessageSimulator.SimulateAction(GIActions.MoveLeft, KeyType.KeyDown);
                    await Delay(100, ct);
                    TaskContext.Instance().PostMessageSimulator.SimulateAction(GIActions.MoveLeft, KeyType.KeyUp);
                    await Delay(300, ct);
                    Simulation.SendInput.Mouse.MiddleButtonClick();
                    await Delay(500, ct);
                    break;
                case "绘绮庭":
                    Logger.LogInformation("领取尘歌壶奖励:{text}", "在绘绮庭，调整位置");
                    TaskContext.Instance().PostMessageSimulator.SimulateAction(GIActions.MoveLeft, KeyType.KeyDown);
                    await Delay(1300, ct);
                    TaskContext.Instance().PostMessageSimulator.SimulateAction(GIActions.MoveLeft, KeyType.KeyUp);
                    await Delay(500, ct);
                    TaskContext.Instance().PostMessageSimulator.SimulateAction(GIActions.MoveBackward, KeyType.KeyDown);
                    await Delay(600, ct);
                    TaskContext.Instance().PostMessageSimulator.SimulateAction(GIActions.MoveBackward, KeyType.KeyUp);
                    await Delay(300, ct);
                    Simulation.SendInput.Mouse.MiddleButtonClick();
                    await Delay(800, ct);
                    break;
                case "旋流屿":
                    Logger.LogInformation("领取尘歌壶奖励:{text}", "在旋流屿，调整位置");
                    TaskContext.Instance().PostMessageSimulator.SimulateAction(GIActions.MoveBackward, KeyType.KeyDown);
                    await Delay(900, ct);
                    TaskContext.Instance().PostMessageSimulator.SimulateAction(GIActions.MoveBackward, KeyType.KeyUp);
                    await Delay(300, ct);
                    Simulation.SendInput.Mouse.MiddleButtonClick();
                    await Delay(800, ct);
                    break;
            }
        }
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
                RegionOfInterest = new Rect(ra.Width / 5, ra.Height / 15, (int)(ra.Width * 0.65), ra.Height / 2)
            });
            Region? ayuanIcon = list.FirstOrDefault(r =>
                r.Text.Contains(ayuanHeyString) || r.Text.Contains(ayuanHuolingString)||
                 r.Text.Contains(ayuanHuoling2String)); 
            if (ayuanIcon == null)
            {
                Simulation.SendInput.Mouse.MoveMouseBy(ra.Width / 10, 0);
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
                if (Math.Abs(middle - ayuanMiddle) > ayuanIcon.Width*1.4) //放宽范围，尽快找到阿圆
                {
                    if(ayuanMiddle - middle > 0)
                    {
                        Simulation.SendInput.Mouse.MoveMouseBy((ayuanMiddle - middle)/2, 0);//未对正前小转
                        await Delay(300, ct);
                    }
                    else if(ayuanMiddle - middle < 0)
                    {
                        Simulation.SendInput.Mouse.MoveMouseBy((ayuanMiddle - middle)*3/2, 0);//转过头回转加大距离
                        await Delay(300, ct);
                    }
                }
                else
                {
                    Logger.LogInformation("领取尘歌壶奖励:{text}", "寻找阿圆成功");
                    break;
                }
                await Delay(300, ct);
            }
            await Delay(500, ct); // 默认开启动态模糊，停顿时间太短的情况下，截图可能会模糊，导致识别失败
            if (continuousCount > 180)
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
                TaskContext.Instance().PostMessageSimulator.SimulateAction(GIActions.Drop);//防止爬墙
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
        string shopOff = "已售";
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
            await Delay(600, ct);//减慢速度，设备差异导致的延迟
            Simulation.SendInput.Mouse.LeftButtonDown();
            await Delay(600, ct);
            numberBtn.MoveTo(ra.Width/7,0);//moveby会超出边界，改用MoveTo
            await Delay(600, ct);
            Simulation.SendInput.Mouse.LeftButtonUp();
        }

        await Delay(600, ct);
        ra.Find(ElementAssets.Instance.BtnWhiteConfirm).Click();
        await Delay(600, ct);
        TaskContext.Instance().PostMessageSimulator.SimulateAction(GIActions.OpenPaimonMenu); // ESC 
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
        if (SelectedConfig.SecretTreasureObjects.Count == 0) 
        {
            Logger.LogInformation("领取尘歌壶奖励:{text}", "未配置购买商店物品");
            return; 
        }
        DateTime now = DateTime.Now;
        DayOfWeek currentDayOfWeek = now.Hour >= 4 ? now.DayOfWeek : now.AddDays(-1).DayOfWeek;
        DayOfWeek? configDayOfWeek = GetDayOfWeekFromConfig(SelectedConfig.SecretTreasureObjects.First());
        if (configDayOfWeek.HasValue || SelectedConfig.SecretTreasureObjects.First() == "每天重复" && SelectedConfig.SecretTreasureObjects.Count > 1)
        {
            // 对比当前日期的星期几与配置中的星期几
            if (configDayOfWeek.HasValue && currentDayOfWeek == configDayOfWeek.Value || SelectedConfig.SecretTreasureObjects.First() == "每天重复")
            {
                var shopOption = await _chooseTalkOptionTask.SingleSelectText(this.ayuanShopString, ct);
                if (shopOption == TalkOptionRes.FoundAndClick)
                {
                    Logger.LogInformation("领取尘歌壶奖励:{text}", "购买商店物品");
                    await Delay(500, ct);
                    // 购买的物品清单
                    var buy = new List<RecognitionObject>();
                    SelectedConfig.SecretTreasureObjects.RemoveAt(0);
                    Logger.LogInformation("购买洞天百宝物品：{text}",string.Join(" / ", SelectedConfig.SecretTreasureObjects)); // 输出所有需要购买的商品
                    foreach (var potBuyItem in SelectedConfig.SecretTreasureObjects)
                    {
                        switch (potBuyItem)
                        {
                            case "布匹":
                                buy.Add(ElementAssets.Instance.AYuanClothRo);
                                break;
                            case "须臾树脂":
                                buy.Add(ElementAssets.Instance.AYuanresinRo);
                                break;
                            case "大英雄的经验":
                                buy.Add(ElementAssets.Instance.SereniteapotExpBookRo);
                                break;
                            case "流浪者的经验":
                                buy.Add(ElementAssets.Instance.SereniteapotExpBookSmallRo);
                                break;
                            case "精锻用魔矿":
                                buy.Add(ElementAssets.Instance.AYuanMagicmineralprecisionRo);
                                break;
                            case "摩拉":
                                buy.Add(ElementAssets.Instance.AYuanMOlaRo);
                                break;
                            case "祝圣精华":
                                buy.Add(ElementAssets.Instance.AYuanExpBottleBigRo);
                                break;
                            case "祝圣油膏":
                                buy.Add(ElementAssets.Instance.AYuanExpBottleSmallRo);
                                break;
                            default:
                                Logger.LogInformation("未知的商品");
                                break;
                        }
                    }
                    
                    //对比购买成功和buy的数量，如果不等，重试一次
                    var buyCount = 0;
                    var retryBuy= 0;
                    // 直接购买最大数量
                    while (retryBuy < 2)
                    {
                        foreach (var item in buy)
                        {
                            var itemRo = CaptureToRectArea().Find(item);
                            if (itemRo.IsExist())
                            {
                                buyCount++;
                                Logger.LogInformation("领取尘歌壶奖励:购买 {text} ", item.Name);
                                itemRo.Click();
                                await Delay(600, ct);
                                await BuyMaxNumber(ct);
                                await Delay(1000, ct);//等待购买动画结束
                            }
                            else
                            {
                                await Delay(700, ct);
                                Logger.LogInformation("领取尘歌壶奖励: {text} 未找到", item.Name);
                            }
                            await Delay(700, ct);
                        }
                        if (buyCount < buy.Count)
                        {
                            retryBuy++;
                            await Delay(500, ct);
                        }else
                        {
                            break;
                        }
                    }
                    await Delay(900, ct);
                    Logger.LogInformation("领取尘歌壶奖励:{text}", "购买商店物品完成");
                    // 购买完成 关闭page
                    CaptureToRectArea().Find(ElementAssets.Instance.PageCloseWhiteRo, a => a.Click());
                }
            }
            else
            {
                Logger.LogInformation("领取尘歌壶奖励: 今天不是购买商店物品的{text}", SelectedConfig.SecretTreasureObjects.First());     
            }
        }
        else
        {
            Logger.LogInformation("领取尘歌壶奖励:{text}", "未配置购买商店物品");
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

    public async Task DoOnce(CancellationToken ct)
    {
        InitConfigList();
        // /**
        //  * 1. 首先退出到主页面
        //  * 2. 进入尘歌壶
        //  * 3. 旋转视角寻找 阿圆
        //  * 4. 贴近阿圆到能对话的地方，并对话
        //  * 5. 领取奖励
        //  */
        // 进入尘歌壶
        var success = false;
        if (SelectedConfig!.SereniteaPotTpType == "地图传送")
        {
            success = await IntoSereniteaPot(ct);
        }
        else
        {
            success = await IntoSereniteaPotByBag(ct);
        }
        if (!success)
        {
            await Finished(ct);
            return;
        }
        
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
    
    private void InitConfigList()
    {
        Directory.CreateDirectory(OneDragonFlowConfigFolder);
        // 读取文件夹内所有json配置，按创建时间正序
        var configFiles = Directory.GetFiles(OneDragonFlowConfigFolder, "*.json");
        var configs = new List<OneDragonFlowConfig>();

        OneDragonFlowConfig? selected = null;
        foreach (var configFile in configFiles)
        {
            var json = File.ReadAllText(configFile);
            var config = JsonConvert.DeserializeObject<OneDragonFlowConfig>(json);
            if (config != null)
            {
                configs.Add(config);
                if (config.Name == TaskContext.Instance().Config.SelectedOneDragonFlowConfigName)
                {
                    selected = config;
                }
            }
        }

        if (selected == null)
        {
            if (configs.Count > 0)
            {
                selected = configs[0];
            }
            else
            {
                selected = new OneDragonFlowConfig
                {
                    Name = "默认配置"
                };
                configs.Add(selected);
            }
        }

        ConfigList.Clear();
        foreach (var config in configs)
        {
            ConfigList.Add(config);
        }

        SelectedConfig = selected;
    }
    
    private DayOfWeek? GetDayOfWeekFromConfig(string configDay)
    {
        switch (configDay)
        {
            case "星期一":
                return DayOfWeek.Monday;
            case "星期二":
                return DayOfWeek.Tuesday;
            case "星期三":
                return DayOfWeek.Wednesday;
            case "星期四":
                return DayOfWeek.Thursday;
            case "星期五":
                return DayOfWeek.Friday;
            case "星期六":
                return DayOfWeek.Saturday;
            case "星期日":
                return DayOfWeek.Sunday;
            case "每天重复":
                return null; // 返回 null 表示每天都重复购买
            default:
                return null; // 返回 null 表示配置中的值不是有效的星期几
        }
    }
    
}
