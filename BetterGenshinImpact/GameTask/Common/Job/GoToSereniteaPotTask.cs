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
using Newtonsoft.Json;
using BetterGenshinImpact.GameTask.QuickSereniteaPot;
using BetterGenshinImpact.Core.Recognition.OCR;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

internal class GoToSereniteaPotTask
{
    private static Task Delay(int milliseconds, CancellationToken ct) => SereniteaPotTaskControl.Delay(milliseconds, ct);

    private static async Task WithHeldKey(GIActions action, Func<Task> operation, CancellationToken ct)
    {
        await Delay(0, ct);
        var simulator = TaskContext.Instance().PostMessageSimulator;
        using var hold = new SereniteaPotInputHold(
            () => simulator.SimulateAction(action, KeyType.KeyDown),
            () => simulator.SimulateAction(action, KeyType.KeyUp), ct);
        var registration = $"pot-input-{Guid.NewGuid():N}";
        var components = RunnerContext.Instance.SuspendableDictionary;
        components.Add(registration, hold);
        try
        {
            hold.Resume();
            ct.ThrowIfCancellationRequested();
            await operation();
        }
        finally { components.Remove(registration); }
    }

    private static Task MoveFor(GIActions action, int milliseconds, CancellationToken ct) =>
        WithHeldKey(action, () => Delay(milliseconds, ct), ct);
    public string Name => "领取尘歌壶奖励";

    private bool fail = false;
    private readonly ChooseTalkOptionTask _chooseTalkOptionTask = new();

    private readonly string ayuanHeyString;
    private readonly string ayuanHuolingString;
    private readonly string ayuanHuoling2String;
    private readonly string ayuanBelieveString;
    private readonly string ayuanShopString;
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
    }

    public async Task Start(CancellationToken ct)
    {
        try
        {
            await DoOnce(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (NormalEndException)
        {
            throw;
        }
        catch (Exception e)
        {
            SereniteaPotUi.SaveFailure("reward-exception");
            Logger.LogDebug(e, "领取尘歌壶奖励异常");
            Logger.LogError("领取尘歌壶奖励异常: {Msg}", e.Message);
        }
        finally
        {
            Simulation.ReleaseAllKey();
        }
    }

    private async Task<bool> OpenSereniteaPotMap(CancellationToken ct)
    {
        const int maxAttempts = 3;
        var failureStage = "map-open";
        var tpTask = new TpTask(ct);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            Logger.LogInformation("尘歌壶地图切区：第 {Attempt}/{MaxAttempts} 次尝试，先确认主界面", attempt, maxAttempts);
            // 重试必须从已确认的主界面开始，不能在展开的菜单上重复点击开关。
            await new ReturnMainUiTask().Start(ct);
            if (!await SereniteaPotUi.WaitForMainUi(ct, $"before-map-{attempt}", recordFailure: false))
            {
                failureStage = "before-map";
                continue;
            }

            TaskContext.Instance().PostMessageSimulator.SimulateAction(GIActions.OpenMap);
            var mapReady = await SereniteaPotWaiter.WaitAsync(() =>
            {
                using var capture = CaptureToRectArea();
                return Bv.IsInBigMapUi(capture);
            }, Delay, ct, TimeSpan.FromSeconds(30));
            if (!mapReady)
            {
                failureStage = "map-open";
                Logger.LogWarning("尘歌壶地图打开等待超时：第 {Attempt}/{MaxAttempts} 次尝试", attempt, maxAttempts);
                SereniteaPotUi.SaveCapture($"map-open-attempt-{attempt}");
                continue;
            }

            Action<string, ImageRegion>? saveFrame = SereniteaPotTestLogSink.Current == null ? null
                : (stage, capture) => SereniteaPotUi.SaveCapture($"attempt-{attempt}-{stage}", capture);
            if (await tpTask.TrySwitchSereniteaPotArea(saveFrame)) return true;

            failureStage = "map-area-switch";
            Logger.LogWarning("尘歌壶地图切区未完成：第 {Attempt}/{MaxAttempts} 次尝试", attempt, maxAttempts);
            SereniteaPotUi.SaveCapture($"map-area-switch-attempt-{attempt}");
        }

        SereniteaPotUi.SaveFailure(failureStage);
        return false;
    }

    private async Task<bool> IntoSereniteaPot(CancellationToken ct)
    {
        if (!await OpenSereniteaPotMap(ct)) return false;
        // 切区完成不代表洞天名称已加载；未确认名称不能继续传送和定位阿圆。
        if (!await ReadRealmName(ct)) return false;

        var homeSelected = false;
        for (int i = 0; i < 5; i++)
        {
            using var currentRa = CaptureToRectArea();
            using var sereniteaPotHomeIcon = currentRa.Find(ElementRecognition.Get("SereniteaPotHome", currentRa));
            if (!sereniteaPotHomeIcon.IsExist())
            {
                Logger.LogInformation("领取尘歌壶奖励:{text}", "住宅图标未找到，调整地图缩放至2。");
                await Task.Delay(1000, ct);
                await new Core.Script.Dependence.Genshin().SetBigMapZoomLevel(2.5-i*0.2);//尝试缩放地图
                await Task.Delay(1000, ct);
            }
            else
            {
                await Delay(100, ct);
                Simulation.ReleaseAllKey();
                await Delay(200, ct);
                sereniteaPotHomeIcon.Click();
                await Delay(500, ct);
                homeSelected = true;
                break;
            }
        }

        if (!homeSelected)
        {
            Logger.LogWarning("领取尘歌壶奖励:未找到住宅，停止传送");
            SereniteaPotUi.SaveFailure("map-home");
            return false;
        }

        var teleportTriggered = false;
        for (int attempt = 0; attempt < 10; attempt++) // 尝试点击传送按钮
        {
            using var ra = CaptureToRectArea();
            using var teleportBtn = ra.Find(RecognitionAssets.Get("QuickTeleport", "TeleportButton", ra));
            if (teleportBtn.IsExist())
            {
                await Delay(300, ct);
                teleportBtn.Click();
                await Delay(500, ct);
        
                bool isReClickRequired = true;
                for(int i = 0; i < 10; i++)     
                {
                    using var buttonCapture = CaptureToRectArea();
                    using var currentTeleportBtn = buttonCapture.Find(RecognitionAssets.Get("QuickTeleport", "TeleportButton", buttonCapture));
                    if (!currentTeleportBtn.IsExist())     //传送按钮消失
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
                teleportTriggered = true;
                break; // 找到并点击传送按钮、确认按钮消失后退出循环
            }
        
            //未找到传送按钮，点击传送住宅按钮
            using var teleportSereniteaPotHome = ra.Find(ElementRecognition.Get("TeleportSereniteaPotHome", ra));
            if (teleportSereniteaPotHome.IsExist())
            {
                teleportSereniteaPotHome.Click();
                await Delay(800, ct);  
                continue; // 找到并点击传送住宅按钮后再次点击传送按钮
            }
        
            Logger.LogInformation("领取尘歌壶奖励:{text}", "传送按钮、传送住宅按钮未找到，重试");
            await Delay(800, ct);    // 重试间隔
        }
        
        if (!teleportTriggered)
        {
            Logger.LogWarning("领取尘歌壶奖励:未确认传送按钮生效，停止进入流程");
            SereniteaPotUi.SaveFailure("map-teleport-button");
            return false;
        }

        return await SereniteaPotUi.WaitForEntry(ct, "map-entry");
    }

    /// <summary>
    /// 通过背包中的壶进入尘歌壶
    /// </summary>
    /// <param name="ct"></param>
    /// <returns>成功进入壶并初始化壶名称返回 true。</returns>
    private async Task<bool> IntoSereniteaPotByBag(CancellationToken ct)
    {
        // 尝试使用背包的壶进入。
        if (!await QuickSereniteaPotTask.Start(ct))
        {
            Logger.LogWarning("领取尘歌壶奖励:通过背包触发进入尘歌壶失败");
            return false;
        }

        if (!await SereniteaPotUi.WaitForEntry(ct, "bag-entry"))
        {
            return false;
        }

        // 已确认在壶内，再打开地图获取洞天名称。
        TaskContext.Instance().PostMessageSimulator.SimulateAction(GIActions.OpenMap);
        if (!await ReadRealmName(ct)) return false;
        await new ReturnMainUiTask().Start(ct);
        return await SereniteaPotUi.WaitForEntry(ct, "after-realm-map", afterTeleport: false);
    }

    /// <summary>两个入口共用洞天名称检查；失败或取消不保留未确认的名称。</summary>
    private async Task<bool> ReadRealmName(CancellationToken ct)
    {
        dongTianName = "";
        var realmName = await SereniteaPotWaiter.WaitForRealmNameAsync(() =>
        {
            using var ra = CaptureToRectArea(forceNew: true);
            if (!Bv.IsInBigMapUi(ra)) return null;
            var list = ra.FindMulti(new RecognitionObject
            {
                RecognitionType = RecognitionTypes.Ocr,
                RegionOfInterest = new Rect((int)(ra.Width * 0.86), ra.Height * 9 / 10, (int)(ra.Width * 0.073), (int)(ra.Height * 0.04))
            });
            return list.Count > 0 ? list[0].Text : null;
        }, Delay, ct);
        if (realmName != null)
        {
            dongTianName = realmName;
            Logger.LogInformation("领取尘歌壶奖励:{text}", "洞天名称：" + dongTianName);
            return true;
        }
        Logger.LogWarning("领取尘歌壶奖励:未识别到洞天名称，停止定位");
        SereniteaPotUi.SaveFailure("realm-name");
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
                    await MoveFor(GIActions.MoveForward, 200, ct);
                    break;
                case "清琼岛":
                    Logger.LogInformation("领取尘歌壶奖励:{text}", "在清琼岛，调整位置");
                    await MoveFor(GIActions.MoveLeft, 100, ct);
                    await Delay(300, ct);
                    Simulation.SendInput.Mouse.MiddleButtonClick();
                    await Delay(500, ct);
                    break;
                case "绘绮庭":
                    Logger.LogInformation("领取尘歌壶奖励:{text}", "在绘绮庭，调整位置");
                    await MoveFor(GIActions.MoveLeft, 1300, ct);
                    await Delay(500, ct);
                    await MoveFor(GIActions.MoveBackward, 600, ct);
                    await Delay(300, ct);
                    Simulation.SendInput.Mouse.MiddleButtonClick();
                    await Delay(800, ct);
                    break;
                case "旋流屿":
                    Logger.LogInformation("领取尘歌壶奖励:{text}", "在旋流屿，调整位置");
                    await MoveFor(GIActions.MoveBackward, 900, ct);
                    await Delay(300, ct);
                    Simulation.SendInput.Mouse.MiddleButtonClick();
                    await Delay(800, ct);
                    break;
            }
        }
        Logger.LogInformation("领取尘歌壶奖励:{text}", "寻找阿圆");
        // 中键回正视角
        Simulation.SendInput.Mouse.MiddleButtonClick();
        await Delay(900, ct);
        int continuousCount = 0;
        var searchWatch = Stopwatch.StartNew();
        while (!ct.IsCancellationRequested)
        {
            if (searchWatch.Elapsed > TimeSpan.FromSeconds(120))
            {
                fail = true;
                Logger.LogWarning("领取尘歌壶奖励:寻找或对准阿圆超时");
                SereniteaPotUi.SaveFailure("align-ayuan");
                return;
            }
            using var ra = CaptureToRectArea();
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
                SereniteaPotUi.SaveFailure("find-ayuan");
                return;
            }
        }

        ct.ThrowIfCancellationRequested();
        await WithHeldKey(GIActions.MoveForward, async () =>
        {
            Logger.LogInformation("领取尘歌壶奖励:{text}", "接近阿圆");
            var approachWatch = Stopwatch.StartNew();
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (approachWatch.Elapsed > TimeSpan.FromSeconds(45))
                {
                    fail = true;
                    Logger.LogWarning("领取尘歌壶奖励:接近阿圆超时");
                    SereniteaPotUi.SaveFailure("approach-ayuan");
                    return;
                }
                using var capture = CaptureToRectArea();
                if (Bv.FindF(capture, text: this.ayuanHeyString))
                {
                    Logger.LogInformation("领取尘歌壶奖励:{text}", "接近阿圆成功");
                    break;
                }
                TaskContext.Instance().PostMessageSimulator.SimulateAction(GIActions.Drop);//防止爬墙
                await Delay(50, ct);
            }
        }, ct);
    }

    private async Task BuyMaxNumber(CancellationToken ct)
    {
        using var ra = CaptureToRectArea();
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
        // var numberBtn = ra.Find(ElementAssets.Instance.SereniteapotShopNumberBtn);
        // if (numberBtn.IsExist())
        // {
        //     numberBtn.Move();
        //     await Delay(600, ct);//减慢速度，设备差异导致的延迟
        //     Simulation.SendInput.Mouse.LeftButtonDown();
        //     await Delay(600, ct);
        //     numberBtn.MoveTo(ra.Width/7,0);//moveby会超出边界，改用MoveTo
        //     await Delay(600, ct);
        //     Simulation.SendInput.Mouse.LeftButtonUp();
        // }

        // await Delay(600, ct);
        ra.Find(ElementRecognition.Get("BtnWhiteConfirm", ra)).Click();
        await Delay(600, ct);
        TaskContext.Instance().PostMessageSimulator.SimulateAction(GIActions.OpenPaimonMenu); // ESC 
    }

    private async Task<bool> GetReward(CancellationToken ct)
    {
        // 保证与阿圆对话
        var interactionFound = await SereniteaPotTaskControl.WaitForAction(() =>
        {
            using var capture = CaptureToRectArea();
            return Bv.FindFAndPress(capture, text: this.ayuanHeyString);
        }, ct);
        if (!interactionFound)
        {
            Logger.LogWarning("领取尘歌壶奖励:未确认与阿圆交互");
            SereniteaPotUi.SaveFailure("ayuan-interaction");
            return false;
        }
        //var ra = CaptureToRectArea();
        //Bv.FindFAndPress(ra,text:this.ayuanHeyString); // 开始对话
        await Delay(500, ct);
        // 领取奖励
        var rewardOption = await _chooseTalkOptionTask.SingleSelectText(this.ayuanBelieveString, ct);
        if (rewardOption != TalkOptionRes.FoundAndClick)
        {
            Logger.LogWarning("领取尘歌壶奖励:未找到信任等阶选项");
            SereniteaPotUi.SaveFailure("reward-dialog");
            return false;
        }
        if (rewardOption == TalkOptionRes.FoundAndClick)
        {
            Logger.LogInformation("领取尘歌壶奖励:{text}", "领取好感和宝钱");
            await Delay(1000, ct);

            if (SereniteaPotTestLogSink.Current != null) SereniteaPotUi.SaveCapture("reward-before");

            using var getAare = CaptureToRectArea();
            using var countArea = getAare.DeriveCrop(getAare.Width* 1801 / 1920,
                getAare.Height* 609 / 1080,getAare.Width * 75 / 1920,getAare.Width * 46 / 1920);
            var count = OcrFactory.Paddle.OcrWithoutDetector(countArea.SrcMat);
            
            var match = System.Text.RegularExpressions.Regex.Match(count, @"(\d+)\s*[/17]\s*(8)");
            var shouldClick = true;
            if (match.Success)
            {
                var numericPart = StringUtils.TryParseInt(match.Groups[1].Value);
                if (numericPart == 0)
                {
                    Logger.LogWarning("领取尘歌壶奖励:{text}", "没有角色可领取好感"); //存好感
                    shouldClick = false;
                }
            }
            
            if (shouldClick)
            {
                getAare.Find(ElementRecognition.Get("SereniteaPotLove", getAare), a => a.Click());
            }
            
            await Delay(500, ct);
            using var ra = CaptureToRectArea();
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

            using var ra1 = CaptureToRectArea();
            if (ra1.Find(ElementRecognition.Get("SereniteapotPageClose", ra1), a => a.Click()).IsExist())
            {
                await Delay(500, ct);
            }

            using var ra2 = CaptureToRectArea();
            ra2.Find(ElementRecognition.Get("SereniteaPotMoney", ra2), a => a.Click());
            await Delay(500, ct);
            if (SereniteaPotTestLogSink.Current != null) SereniteaPotUi.SaveCapture("reward-after");
            using var ra3 = CaptureToRectArea();
            ra3.Find(ElementRecognition.Get("SereniteapotPageClose", ra3), a => a.Click());
            await Delay(500, ct);
            using var ra4 = CaptureToRectArea();
            ra4.Find(ElementRecognition.Get("PageCloseWhite", ra4)).Click();
        }
        
        await Delay(900, ct);
        // 商店购买
        if (SelectedConfig.SecretTreasureObjects.Count == 0) 
        {
            Logger.LogInformation("领取尘歌壶奖励:{text}", "未配置购买商店物品");
            return true;
        }
        DateTimeOffset serverTime = ServerTimeHelper.GetServerTimeNow();
        DayOfWeek currentDayOfWeek = serverTime.Hour >= 4 ? serverTime.DayOfWeek : serverTime.AddDays(-1).DayOfWeek;
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
                                buy.Add(ElementRecognition.Get("AYuanCloth"));
                                break;
                            case "须臾树脂":
                                buy.Add(ElementRecognition.Get("AYuanresin"));
                                break;
                            case "大英雄的经验":
                                buy.Add(ElementRecognition.Get("SereniteapotExpBook"));
                                break;
                            case "流浪者的经验":
                                buy.Add(ElementRecognition.Get("SereniteapotExpBookSmall"));
                                break;
                            case "精锻用魔矿":
                                buy.Add(ElementRecognition.Get("AYuanMagicmineralprecision"));
                                break;
                            case "摩拉":
                                buy.Add(ElementRecognition.Get("AYuanMOla"));
                                break;
                            case "祝圣精华":
                                buy.Add(ElementRecognition.Get("AYuanExpBottleBig"));
                                break;
                            case "祝圣油膏":
                                buy.Add(ElementRecognition.Get("AYuanExpBottleSmall"));
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
                            using var itemCapture = CaptureToRectArea();
                            var itemRo = itemCapture.Find(item);
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
                    using var ra5 = CaptureToRectArea();
                    ra5.Find(ElementRecognition.Get("PageCloseWhite", ra5), a => a.Click());
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
        return true;
    }

    // 处理最后收尾操作
    private async Task<bool> Finished(CancellationToken ct)
    {
        Logger.LogInformation("领取尘歌壶奖励:{text}", "退出到主页");
        // 识别page 关闭按钮。
        using var ra6 = CaptureToRectArea();
        if (ra6.Find(ElementRecognition.Get("PageCloseWhite", ra6), a => a.Click()).IsExist())
        {
            await Delay(1000, ct);
        }

        var isMainUi = await _chooseTalkOptionTask.ClickChatExitUntilMainUi(ct);
        if (!isMainUi)
        {
            Logger.LogError("领取尘歌壶奖励:{text}", "阿圆对话框退出出错。");
            SereniteaPotUi.SaveFailure("leave-dialog");
            return false;
        }

        await Delay(500, ct);

        // TP回主世界
        var tp = new TpTask(ct);
        await tp.Tp(4508.97509765625, 3630.557373046875); // TP到枫丹
        return true;
    }

    public async Task DoOnce(CancellationToken ct)
    {
        fail = false;
        dongTianName = "";
        InitConfigList();
        await Execute(ct, entryOnly: false);
    }

    /// <summary>手动测试复用正式流程，只使用内存配置，不执行一条龙或洞天商店购买。</summary>
    internal async Task<bool> TestAsync(string entryType, bool includeRewards, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        fail = false;
        dongTianName = "";
        SelectedConfig = new OneDragonFlowConfig
        {
            SereniteaPotTpType = entryType,
            SecretTreasureObjects = []
        };
        try
        {
            using (var capture = CaptureToRectArea(forceNew: true))
            {
                using var finger = capture.Find(ElementRecognition.Get("FingerIcon", capture));
                if (finger.IsExist())
                {
                    Logger.LogWarning("尘歌壶测试:请先回到大世界，再测试进入尘歌壶");
                    SereniteaPotUi.SaveFailure("already-in-pot");
                    return false;
                }
            }
            return await Execute(ct, entryOnly: !includeRewards);
        }
        finally
        {
            Simulation.ReleaseAllKey();
        }
    }

    private async Task<bool> Execute(CancellationToken ct, bool entryOnly)
    {
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
            Logger.LogWarning("领取尘歌壶奖励:进入流程失败，本次未领取奖励，尝试恢复主界面");
            await new ReturnMainUiTask().Start(ct);
            await SereniteaPotUi.WaitForMainUi(ct, "entry-recovery");
            return false;
        }

        if (entryOnly)
        {
            Logger.LogInformation("尘歌壶测试:进壶已确认，停留在壶内");
            return true;
        }
        
        // 寻找阿圆并靠近
        await FindAYuan(ct);
        // 领取奖励
        if (fail)
        {
            await Finished(ct);
            return false;
        }

        await Delay(500, ct);
        var rewardsFinished = await GetReward(ct);

        // 收尾操作 - 退出到主页面 - 传送到提瓦特大陆
        var cleanupFinished = await Finished(ct);
        return rewardsFinished && cleanupFinished;
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
