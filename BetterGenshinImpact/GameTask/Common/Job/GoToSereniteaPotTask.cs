using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.QuickSereniteaPot;
using BetterGenshinImpact.GameTask.QuickTeleport.Assets;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.View.Pages.SoloTaskSettings;
using BetterGenshinImpact.ViewModel.SoloTaskSettings;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using BetterGenshinImpact.Core.Recognition.OCR;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 领取尘歌壶奖励：进入尘歌壶，找到阿圆领取好感与宝钱，并按参数购买洞天百宝商品，最后传送回主世界。
/// </summary>
[SoloTaskInfo(TaskId, "领取尘歌壶奖励", Group = "日常奖励", Order = 140, OneDragonOrder = 8)]
[SoloTaskSettingsView(typeof(SereniteaPotSettingsView), typeof(SereniteaPotSettingsViewModel))]
public class GoToSereniteaPotTask : ISoloTask<SereniteaPotParam, NoData>
{
    /// <summary>
    /// 任务标识。
    /// </summary>
    public const string TaskId = "serenitea_pot_rewards";

    /// <summary>
    /// 寻找阿圆是否失败
    /// </summary>
    private bool fail = false;

    /// <summary>
    /// 本次运行的参数，在 RunAsync 开头赋值
    /// </summary>
    private SereniteaPotParam _param = new();

    /// <summary>
    /// 本次运行使用的运行环境
    /// </summary>
    private readonly GameRuntime _runtime;

    /// <summary>
    /// 配置根
    /// </summary>
    private readonly AllConfig _config;

    /// <summary>
    /// 持续对话并选择选项子任务
    /// </summary>
    private readonly ChooseTalkOptionTask _chooseTalkOptionTask;

    private readonly string ayuanHeyString;
    private readonly string ayuanHuolingString;
    private readonly string ayuanHuoling2String;
    private readonly string ayuanBelieveString;
    private readonly string ayuanShopString;

    /// <summary>
    /// 识别到的洞天名称，用于寻找阿圆前调整站位；未识别时为空字符串
    /// </summary>
    private string dongTianName = string.Empty;


    /// <summary>
    /// 创建领取尘歌壶奖励任务
    /// </summary>
    /// <param name="runtime">本次运行使用的运行环境</param>
    /// <param name="config">配置根</param>
    public GoToSereniteaPotTask(GameRuntime runtime, AllConfig config)
    {
        _runtime = runtime;
        _config = config;
        _chooseTalkOptionTask = new ChooseTalkOptionTask(runtime);
        IStringLocalizer<GoToSereniteaPotTask> stringLocalizer = App.GetService<IStringLocalizer<GoToSereniteaPotTask>>() ?? throw new NullReferenceException();
        CultureInfo cultureInfo = new CultureInfo(config.OtherConfig.GameCultureInfoName);
        this.ayuanHeyString = stringLocalizer.WithCultureGet(cultureInfo, "阿圆");
        this.ayuanHuolingString = stringLocalizer.WithCultureGet(cultureInfo, "壶灵");
        this.ayuanHuoling2String = stringLocalizer.WithCultureGet(cultureInfo, "<壶灵>");
        this.ayuanBelieveString = stringLocalizer.WithCultureGet(cultureInfo, "信任");
        this.ayuanShopString = stringLocalizer.WithCultureGet(cultureInfo, "洞天百宝");
    }

    private async Task<bool> IntoSereniteaPot(CancellationToken ct)
    {
        // 退出到主页面
        await new ReturnMainUiTask(_runtime).Start(ct);

        await _runtime.Delay(200, ct);

        InputHub.Background.SimulateAction(GIActions.OpenMap); // 打开地图
        await _runtime.Delay(900, ct);

        // 进入 壶
        TpTask tpTask = new TpTask(_runtime, _config, ct);
        await tpTask.SwitchArea("尘歌壶");
        
        // 若未找到 ElementAssets.Instance.SereniteaPotRo 就是已经在尘歌壶了
        for (int i = 0; i < 5; i++){
            using var ra = _runtime.CaptureToRectArea();
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
            using var currentRa = _runtime.CaptureToRectArea();
            var sereniteaPotHomeIcon = currentRa.Find(ElementRecognition.Get("SereniteaPotHome", currentRa));
            if (!sereniteaPotHomeIcon.IsExist())
            {
                Logger.LogInformation("领取尘歌壶奖励:{text}", "住宅图标未找到，调整地图缩放至2。");
                await Task.Delay(1000, ct);
                await new Core.Script.Dependence.Genshin(_runtime, _config, ct).SetBigMapZoomLevel(2.5-i*0.2);//尝试缩放地图
                await Task.Delay(1000, ct);
            }
            else
            {
                await _runtime.Delay(100, ct);
                InputHub.ReleaseAll();
                await _runtime.Delay(200, ct);
                sereniteaPotHomeIcon.Click();
                await _runtime.Delay(500, ct);
                break;
            }
        }

        for (int attempt = 0; attempt < 10; attempt++) // 尝试点击传送按钮
        {
            using var ra = _runtime.CaptureToRectArea();
            var teleportBtn = ra.Find(RecognitionAssets.Get("QuickTeleport", "TeleportButton", ra));
            if (teleportBtn.IsExist())
            {
                await _runtime.Delay(300, ct);
                teleportBtn.Click();
                await _runtime.Delay(500, ct);
        
                bool isReClickRequired = true;
                for(int i = 0; i < 10; i++)     
                {
                    using var buttonCapture = _runtime.CaptureToRectArea();
                    teleportBtn = buttonCapture.Find(RecognitionAssets.Get("QuickTeleport", "TeleportButton", buttonCapture));
                    if (!teleportBtn.IsExist())     //传送按钮消失
                    {
                        isReClickRequired = false;
                        break;
                    }
                    await _runtime.Delay(500, ct);   // 传送按钮还在，等待游戏反应
                }

                if (isReClickRequired)
                {
                    continue;   //传送按钮未消失，再次尝试点击
                }
                break; // 找到并点击传送按钮、确认按钮消失后退出循环
            }
        
            //未找到传送按钮，点击传送住宅按钮
            var teleportSereniteaPotHome = ra.Find(ElementRecognition.Get("TeleportSereniteaPotHome", ra));
            if (teleportSereniteaPotHome.IsExist())
            {
                teleportSereniteaPotHome.Click();
                await _runtime.Delay(800, ct);  
                continue; // 找到并点击传送住宅按钮后再次点击传送按钮
            }
        
            if (attempt == 9)
            {
                Logger.LogWarning("领取尘歌壶奖励:{text}", "传送至尘歌壶失败");
                return false;
            }
        
            Logger.LogInformation("领取尘歌壶奖励:{text}", "传送按钮、传送住宅按钮未找到，重试");
            await _runtime.Delay(800, ct);    // 重试间隔
        }
        
        await NewRetry.WaitForAction(_runtime, () =>
        {
            using var capture = _runtime.CaptureToRectArea();
            return Bv.IsInMainUi(capture);
        }, ct);
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
        QuickSereniteaPotTask.Done(_runtime);
        await _runtime.Delay(5000, ct); // 在点击壶之后的特殊加载页面会有 mainUI
        await Bv.WaitForMainUi(_runtime, ct);
        // 判断是否在尘歌壶中
        using var ra0 = _runtime.CaptureToRectArea();
        if (ra0.Find(ElementRecognition.Get("FingerIcon", ra0)).IsExist())
        {
            await _runtime.Delay(1000, ct);
            // 尝试获取尘歌壶名称
            InputHub.Background.SimulateAction(GIActions.OpenMap); // 打开地图
            await _runtime.Delay(1000, ct);
            for (int i = 0; i < 5; i++)
            {
                using var ra = _runtime.CaptureToRectArea();
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
                        InputHub.Background.SimulateAction(GIActions.OpenMap); await _runtime.Delay(1000, ct);
                        using var mainUiCapture = _runtime.CaptureToRectArea();
                        if (Bv.IsInMainUi(mainUiCapture))
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
                await _runtime.Delay(200, ct);
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
            await _runtime.Delay(500, ct);
            switch (dongTianName)
            {
                case "妙香林":
                    Logger.LogInformation("领取尘歌壶奖励:{text}", "在妙香林，调整位置");
                    InputHub.Background.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
                    await _runtime.Delay(200, ct);
                    InputHub.Background.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                    break;
                case "清琼岛":
                    Logger.LogInformation("领取尘歌壶奖励:{text}", "在清琼岛，调整位置");
                    InputHub.Background.SimulateAction(GIActions.MoveLeft, KeyType.KeyDown);
                    await _runtime.Delay(100, ct);
                    InputHub.Background.SimulateAction(GIActions.MoveLeft, KeyType.KeyUp);
                    await _runtime.Delay(300, ct);
                    InputHub.Foreground.Mouse.MiddleButtonClick();
                    await _runtime.Delay(500, ct);
                    break;
                case "绘绮庭":
                    Logger.LogInformation("领取尘歌壶奖励:{text}", "在绘绮庭，调整位置");
                    InputHub.Background.SimulateAction(GIActions.MoveLeft, KeyType.KeyDown);
                    await _runtime.Delay(1300, ct);
                    InputHub.Background.SimulateAction(GIActions.MoveLeft, KeyType.KeyUp);
                    await _runtime.Delay(500, ct);
                    InputHub.Background.SimulateAction(GIActions.MoveBackward, KeyType.KeyDown);
                    await _runtime.Delay(600, ct);
                    InputHub.Background.SimulateAction(GIActions.MoveBackward, KeyType.KeyUp);
                    await _runtime.Delay(300, ct);
                    InputHub.Foreground.Mouse.MiddleButtonClick();
                    await _runtime.Delay(800, ct);
                    break;
                case "旋流屿":
                    Logger.LogInformation("领取尘歌壶奖励:{text}", "在旋流屿，调整位置");
                    InputHub.Background.SimulateAction(GIActions.MoveBackward, KeyType.KeyDown);
                    await _runtime.Delay(900, ct);
                    InputHub.Background.SimulateAction(GIActions.MoveBackward, KeyType.KeyUp);
                    await _runtime.Delay(300, ct);
                    InputHub.Foreground.Mouse.MiddleButtonClick();
                    await _runtime.Delay(800, ct);
                    break;
            }
        }
        Logger.LogInformation("领取尘歌壶奖励:{text}", "寻找阿圆");
        // 中键回正视角
        InputHub.Foreground.Mouse.MiddleButtonClick();
        await _runtime.Delay(900, ct);
        int continuousCount = 0;
        while (!ct.IsCancellationRequested)
        {
            using var ra = _runtime.CaptureToRectArea();
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
                InputHub.Foreground.Mouse.MoveMouseBy(ra.Width / 10, 0);
                continuousCount++;
            }
            else
            {
                // 判断阿圆的icon 是否在屏幕上四分之一 避免角色遮挡
                if ((ayuanIcon.Height / 2 + ayuanIcon.Y) > (ra.Height / 4))
                {
                    var moveY = (ayuanIcon.Height / 2 + ayuanIcon.Y) - (ra.Height / 4) + 100; // 加个偏移，快速收敛
                    InputHub.Foreground.Mouse.MoveMouseBy(0, (int)(moveY * _runtime.SystemInfo.DpiScale));
                    await _runtime.Delay(300, ct);
                    continue;
                }
                var middle = ra.Width / 2;
                var ayuanMiddle = ayuanIcon.X + ayuanIcon.Width / 2;
                if (Math.Abs(middle - ayuanMiddle) > ayuanIcon.Width*1.4) //放宽范围，尽快找到阿圆
                {
                    if(ayuanMiddle - middle > 0)
                    {
                        InputHub.Foreground.Mouse.MoveMouseBy((ayuanMiddle - middle)/2, 0);//未对正前小转
                        await _runtime.Delay(300, ct);
                    }
                    else if(ayuanMiddle - middle < 0)
                    {
                        InputHub.Foreground.Mouse.MoveMouseBy((ayuanMiddle - middle)*3/2, 0);//转过头回转加大距离
                        await _runtime.Delay(300, ct);
                    }
                }
                else
                {
                    Logger.LogInformation("领取尘歌壶奖励:{text}", "寻找阿圆成功");
                    break;
                }
                await _runtime.Delay(300, ct);
            }
            await _runtime.Delay(500, ct); // 默认开启动态模糊，停顿时间太短的情况下，截图可能会模糊，导致识别失败
            if (continuousCount > 180)
            {
                fail = true;
                Logger.LogWarning("领取尘歌壶奖励:{text}", "寻找阿圆失败");
                return;
            }
        }

        ct.ThrowIfCancellationRequested();
        try
        {
            InputHub.Background.SimulateAction(GIActions.MoveForward, KeyType.KeyDown); // 向前走
            Logger.LogInformation("领取尘歌壶奖励:{text}", "接近阿圆");
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                using var capture = _runtime.CaptureToRectArea();
                if (Bv.FindF(capture, text: this.ayuanHeyString))
                {
                    Logger.LogInformation("领取尘歌壶奖励:{text}", "接近阿圆成功");
                    break;
                }
                InputHub.Background.SimulateAction(GIActions.Drop);//防止爬墙
                await _runtime.Delay(50, ct);
            }
        }
        finally
        {
            // 正常结束、取消或异常时都释放前进键。
            InputHub.Background.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
        }
    }

    private async Task BuyMaxNumber(CancellationToken ct)
    {
        using var ra = _runtime.CaptureToRectArea();
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
        //     InputHub.Foreground.Mouse.LeftButtonDown();
        //     await Delay(600, ct);
        //     numberBtn.MoveTo(ra.Width/7,0);//moveby会超出边界，改用MoveTo
        //     await Delay(600, ct);
        //     InputHub.Foreground.Mouse.LeftButtonUp();
        // }

        // await Delay(600, ct);
        ra.Find(ElementRecognition.Get("BtnWhiteConfirm", ra)).Click();
        await _runtime.Delay(600, ct);
        InputHub.Background.SimulateAction(GIActions.OpenPaimonMenu); // ESC 
    }

    private async Task GetReward(CancellationToken ct)
    {
        // 保证与阿圆对话
        await NewRetry.WaitForAction(_runtime, () =>
        {
            using var capture = _runtime.CaptureToRectArea();
            return Bv.FindFAndPress(capture, text: this.ayuanHeyString);
        }, ct);
        //var ra = CaptureToRectArea();
        //Bv.FindFAndPress(ra,text:this.ayuanHeyString); // 开始对话
        await _runtime.Delay(500, ct);
        // 领取奖励
        var rewardOption = await _chooseTalkOptionTask.SingleSelectText(this.ayuanBelieveString, ct);
        if (rewardOption == TalkOptionRes.FoundAndClick)
        {
            Logger.LogInformation("领取尘歌壶奖励:{text}", "领取好感和宝钱");
            await _runtime.Delay(1000, ct);

            using var getAare = _runtime.CaptureToRectArea();
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
            
            await _runtime.Delay(500, ct);
            using var ra = _runtime.CaptureToRectArea();
            var list = ra.FindMulti(new RecognitionObject
            {
                RecognitionType = RecognitionTypes.Ocr,
                RegionOfInterest = new Rect((int)(ra.Width * 0.35), (int)(ra.Height * 0.45), (int)(ra.Width * 0.3), (int)(ra.Height * 0.05))
            });
            var tem = list.FirstOrDefault(a => a.Text.Contains("无法领取好感经验"));
            if (tem != null)
            {
                tem.Click();
                await _runtime.Delay(200, ct);
            }

            using var ra1 = _runtime.CaptureToRectArea();
            if (ra1.Find(ElementRecognition.Get("SereniteapotPageClose", ra1), a => a.Click()).IsExist())
            {
                await _runtime.Delay(500, ct);
            }

            using var ra2 = _runtime.CaptureToRectArea();
            ra2.Find(ElementRecognition.Get("SereniteaPotMoney", ra2), a => a.Click());
            await _runtime.Delay(500, ct);
            using var ra3 = _runtime.CaptureToRectArea();
            ra3.Find(ElementRecognition.Get("SereniteapotPageClose", ra3), a => a.Click());
            await _runtime.Delay(500, ct);
            using var ra4 = _runtime.CaptureToRectArea();
            ra4.Find(ElementRecognition.Get("PageCloseWhite", ra4)).Click();
        }
        
        await _runtime.Delay(900, ct);
        // 商店购买：第一项是购买日期，其后是商品名
        var secretTreasureObjects = _param.SecretTreasureObjects ?? [];
        if (secretTreasureObjects.Count == 0) 
        {
            Logger.LogInformation("领取尘歌壶奖励:{text}", "未配置购买商店物品");
            return; 
        }
        DateTimeOffset serverTime = ServerTimeHelper.GetServerTimeNow();
        DayOfWeek currentDayOfWeek = serverTime.Hour >= 4 ? serverTime.DayOfWeek : serverTime.AddDays(-1).DayOfWeek;
        DayOfWeek? configDayOfWeek = GetDayOfWeekFromConfig(secretTreasureObjects.First());
        if (configDayOfWeek.HasValue || secretTreasureObjects.First() == "每天重复" && secretTreasureObjects.Count > 1)
        {
            // 对比当前日期的星期几与配置中的星期几
            if (configDayOfWeek.HasValue && currentDayOfWeek == configDayOfWeek.Value || secretTreasureObjects.First() == "每天重复")
            {
                var shopOption = await _chooseTalkOptionTask.SingleSelectText(this.ayuanShopString, ct);
                if (shopOption == TalkOptionRes.FoundAndClick)
                {
                    Logger.LogInformation("领取尘歌壶奖励:{text}", "购买商店物品");
                    await _runtime.Delay(500, ct);
                    // 购买的物品清单
                    var buy = new List<RecognitionObject>();
                    // 跳过第一项（购买日期），不修改参数中的列表，避免参数被重复使用时丢失日期
                    var buyItemNames = secretTreasureObjects.Skip(1).ToList();
                    Logger.LogInformation("购买洞天百宝物品：{text}",string.Join(" / ", buyItemNames)); // 输出所有需要购买的商品
                    foreach (var potBuyItem in buyItemNames)
                    {
                        switch (potBuyItem)
                        {
                            case "布匹":
                                buy.Add(ElementRecognition.Get("AYuanCloth", _runtime.SystemInfo));
                                break;
                            case "须臾树脂":
                                buy.Add(ElementRecognition.Get("AYuanresin", _runtime.SystemInfo));
                                break;
                            case "大英雄的经验":
                                buy.Add(ElementRecognition.Get("SereniteapotExpBook", _runtime.SystemInfo));
                                break;
                            case "流浪者的经验":
                                buy.Add(ElementRecognition.Get("SereniteapotExpBookSmall", _runtime.SystemInfo));
                                break;
                            case "精锻用魔矿":
                                buy.Add(ElementRecognition.Get("AYuanMagicmineralprecision", _runtime.SystemInfo));
                                break;
                            case "摩拉":
                                buy.Add(ElementRecognition.Get("AYuanMOla", _runtime.SystemInfo));
                                break;
                            case "祝圣精华":
                                buy.Add(ElementRecognition.Get("AYuanExpBottleBig", _runtime.SystemInfo));
                                break;
                            case "祝圣油膏":
                                buy.Add(ElementRecognition.Get("AYuanExpBottleSmall", _runtime.SystemInfo));
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
                            using var itemCapture = _runtime.CaptureToRectArea();
                            var itemRo = itemCapture.Find(item);
                            if (itemRo.IsExist())
                            {
                                buyCount++;
                                Logger.LogInformation("领取尘歌壶奖励:购买 {text} ", item.Name);
                                itemRo.Click();
                                await _runtime.Delay(600, ct);
                                await BuyMaxNumber(ct);
                                await _runtime.Delay(1000, ct);//等待购买动画结束
                            }
                            else
                            {
                                await _runtime.Delay(700, ct);
                                Logger.LogInformation("领取尘歌壶奖励: {text} 未找到", item.Name);
                            }
                            await _runtime.Delay(700, ct);
                        }
                        if (buyCount < buy.Count)
                        {
                            retryBuy++;
                            await _runtime.Delay(500, ct);
                        }else
                        {
                            break;
                        }
                    }
                    await _runtime.Delay(900, ct);
                    Logger.LogInformation("领取尘歌壶奖励:{text}", "购买商店物品完成");
                    // 购买完成 关闭page
                    using var ra5 = _runtime.CaptureToRectArea();
                    ra5.Find(ElementRecognition.Get("PageCloseWhite", ra5), a => a.Click());
                }
            }
            else
            {
                Logger.LogInformation("领取尘歌壶奖励: 今天不是购买商店物品的{text}", secretTreasureObjects.First());     
            }
        }
        else
        {
            Logger.LogInformation("领取尘歌壶奖励:{text}", "未配置购买商店物品");
        }

        await _runtime.Delay(900, ct);
    }

    // 处理最后收尾操作
    private async Task Finished(CancellationToken ct)
    {
        Logger.LogInformation("领取尘歌壶奖励:{text}", "退出到主页");
        // 识别page 关闭按钮。
        using var ra6 = _runtime.CaptureToRectArea();
        if (ra6.Find(ElementRecognition.Get("PageCloseWhite", ra6), a => a.Click()).IsExist())
        {
            await _runtime.Delay(1000, ct);
        }

        var isMainUi = await _chooseTalkOptionTask.ClickChatExitUntilMainUi(ct);
        if (!isMainUi)
        {
            Logger.LogError("领取尘歌壶奖励:{text}", "阿圆对话框退出出错。");
            return;
        }

        await _runtime.Delay(500, ct);

        // TP回主世界
        var tp = new TpTask(_runtime, _config, ct);
        await tp.Tp(4508.97509765625, 3630.557373046875); // TP到枫丹
    }

    /// <summary>
    /// 执行一次尘歌壶奖励流程。识别或操作异常直接向外抛出，由调用方决定是否吞掉；无论结果如何结束时都释放所有按键。
    /// </summary>
    /// <param name="param">领取尘歌壶奖励参数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>未能进壶或未找到阿圆返回 Failed(page_not_reached)；到达领取与收尾阶段返回 Unverified（无法确认奖励是否真正到账）</returns>
    public async Task<SoloTaskResult<NoData>> RunAsync(SereniteaPotParam param, CancellationToken ct)
    {
        _param = param;
        fail = false;
        try
        {
            // /**
            //  * 1. 首先退出到主页面
            //  * 2. 进入尘歌壶
            //  * 3. 旋转视角寻找 阿圆
            //  * 4. 贴近阿圆到能对话的地方，并对话
            //  * 5. 领取奖励
            //  */
            // 进入尘歌壶：“地图传送”走大地图，其他方式使用背包中的尘歌壶道具
            var success = false;
            if (_param.TeleportType == "地图传送")
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
                return SoloTaskResult.Failed(SoloTaskReasons.PageNotReached, "尘歌壶奖励流程未到达领取与收尾阶段。");
            }
            
            // 寻找阿圆并靠近
            await FindAYuan(ct);
            // 领取奖励
            if (fail)
            {
                await Finished(ct);
                return SoloTaskResult.Failed(SoloTaskReasons.PageNotReached, "尘歌壶奖励流程未到达领取与收尾阶段。");
            }

            await _runtime.Delay(500, ct);
            await GetReward(ct);

            // 收尾操作 - 退出到主页面 - 传送到提瓦特大陆
            await Finished(ct);
            return SoloTaskResult.Unverified("尘歌壶奖励领取流程已结束。");
        }
        finally
        {
            // 与原 Start 一致：结束时释放所有按键，避免移动键残留按下
            InputHub.ReleaseAll();
        }
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
