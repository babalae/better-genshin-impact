using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.GameTask.Common.Map;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.View.Drawable;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using static Vanara.PInvoke.Kernel32;
using static Vanara.PInvoke.User32;
using Microsoft.Extensions.Localization;
using System.Globalization;
using System.Text.RegularExpressions;
using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using System.Collections.ObjectModel;
using BetterGenshinImpact.Core.Script.Dependence;
using BetterGenshinImpact.GameTask.AutoDomain.Model;
using BetterGenshinImpact.GameTask.Common;
using Compunet.YoloSharp;
using Microsoft.Extensions.DependencyInjection;
using BetterGenshinImpact.GameTask.AutoFight;

namespace BetterGenshinImpact.GameTask.AutoDomain;

public class AutoDomainTask : ISoloTask
{
    public string Name => Lang.S["Task_059_1f7122"];

    private readonly AutoDomainParam _taskParam;

    private readonly BgiYoloPredictor _predictor;

    private readonly AutoDomainConfig _config;

    private readonly CombatScriptBag _combatScriptBag;

    private CancellationToken _ct;

    private ObservableCollection<OneDragonFlowConfig> ConfigList = [];

    private readonly string challengeCompletedLocalizedString;
    private readonly string autoLeavingLocalizedString;
    private readonly string skipLocalizedString;
    private readonly string leyLineDisorderLocalizedString;
    private readonly string clickanywheretocloseLocalizedString;
    private readonly string matchingChallengeString;
    private readonly string rapidformationString;
    private readonly string limitedFullyString;
    private readonly string limitedFullyAllString;

    private List<ResinUseRecord> _resinPriorityListWhenSpecifyUse;

    public AutoDomainTask(AutoDomainParam taskParam)
    {
        AutoFightAssets.DestroyInstance();
        _taskParam = taskParam;
        _predictor = App.ServiceProvider.GetRequiredService<BgiOnnxFactory>().CreateYoloPredictor(BgiOnnxModel.BgiTree);

        _config = TaskContext.Instance().Config.AutoDomainConfig;

        _combatScriptBag = CombatScriptParser.ReadAndParse(_taskParam.CombatStrategyPath);

        _resinPriorityListWhenSpecifyUse = ResinUseRecord.BuildFromDomainParam(taskParam);

        IStringLocalizer<AutoDomainTask> stringLocalizer =
            App.GetService<IStringLocalizer<AutoDomainTask>>() ?? throw new NullReferenceException();
        CultureInfo cultureInfo = new CultureInfo(TaskContext.Instance().Config.OtherConfig.GameCultureInfoName);
        this.challengeCompletedLocalizedString = stringLocalizer.WithCultureGet(cultureInfo, Lang.S["GameTask_10478_31227f"]);
        this.autoLeavingLocalizedString = stringLocalizer.WithCultureGet(cultureInfo, Lang.S["GameTask_10477_0fc0ef"]);
        this.skipLocalizedString = stringLocalizer.WithCultureGet(cultureInfo, Lang.S["GameTask_10380_92636e"]);
        this.leyLineDisorderLocalizedString = stringLocalizer.WithCultureGet(cultureInfo, Lang.S["GameTask_10476_4aa502"]);
        this.clickanywheretocloseLocalizedString = stringLocalizer.WithCultureGet(cultureInfo, Lang.S["GameTask_10475_b56caa"]);
        this.matchingChallengeString = stringLocalizer.WithCultureGet(cultureInfo, Lang.S["GameTask_10474_fd4ae0"]);
        this.rapidformationString = stringLocalizer.WithCultureGet(cultureInfo, Lang.S["GameTask_10473_b13ea6"]);
        this.limitedFullyString = stringLocalizer.WithCultureGet(cultureInfo, Lang.S["GameTask_10472_96cff4"]);
        this.limitedFullyAllString = stringLocalizer.WithCultureGet(cultureInfo, Lang.S["GameTask_10471_0056ad"]);
    }

    private static RecognitionObject GetConfirmRa(params string[] targetText)
    {
        var screenArea = CaptureToRectArea();
        var x = (int)(screenArea.Width * 0.5);
        var y = (int)(screenArea.Height * 0.5);
        var width = (int)(screenArea.Width * 0.5);
        var height = (int)(screenArea.Height * 0.5);
        return RecognitionObject.OcrMatch(x, y, width, height, targetText);
    }

    public async Task Start(CancellationToken ct)
    {
        _ct = ct;

        Init();
        Notify.Event(NotificationEvent.DomainStart).Success(Lang.S["GameTask_10470_f23957"]);

        // 复活重试
        for (var i = 0; i < _config.ReviveRetryCount; i++)
        {
            try
            {
                await DoDomain();
                // 其他场景不重试
                break;
            }
            catch (RetryException e)
            {
                // 只有选择了秘境的时候才会重试
                if (!string.IsNullOrEmpty(_taskParam.DomainName))
                {
                    var msg = e.Message;
                    if (msg.Contains(Lang.S["GameTask_10469_a8a79c"]))
                    {
                        msg = Lang.S["GameTask_10468_bb1983"];
                    }

                    Logger.LogWarning(Lang.S["GameTask_10421_8f46b3"], msg);
                    await Delay(2000, ct);
                    Notify.Event(NotificationEvent.DomainRetry).Error(msg);
                    continue;
                }

                throw;
            }
        }


        await Delay(2000, ct);
        await Bv.WaitForMainUi(_ct, 30);
        await Delay(2000, ct);

        await ArtifactSalvage();
        Notify.Event(NotificationEvent.DomainEnd).Success(Lang.S["GameTask_10467_45a31a"]);
    }

    private async Task DoDomain()
    {
        // 传送到秘境
        await TpDomain();
        // 切换队伍
        // await SwitchParty(_taskParam.PartyName);

        // 前置进入秘境
        await EnterDomain();

        var combatScenes = new CombatScenes();
        for (var i = 0; i < _taskParam.DomainRoundNum; i++)
        {
            // 0. 关闭秘境提示
            Logger.LogDebug(Lang.S["GameTask_10466_23d4b0"]);
            await CloseDomainTip();

            //0.5. 初始化队伍，只执行一次
            if (i == 0)
            {
                combatScenes = new CombatScenes().InitializeTeam(CaptureToRectArea());
            }

            RetryTeamInit(combatScenes); // 队伍没初始化成功则重试

            // 0. 切换到第一个角色
            var combatCommands = FindCombatScriptAndSwitchAvatar(combatScenes);

            // 1. 走到钥匙处启动
            Logger.LogInformation(Lang.S["GameTask_10421_8f46b3"], "1. 走到钥匙处启动");
            await WalkToPressF();

            // 2. 执行战斗（战斗线程、视角线程、检测战斗完成线程）
            Logger.LogInformation(Lang.S["GameTask_10421_8f46b3"], "2. 执行战斗策略");
            await StartFight(combatScenes, combatCommands);
            combatScenes.AfterTask();
            EndFightWait();

            // 3. 寻找石化古树 并左右移动直到石化古树位于屏幕中心
            Logger.LogInformation(Lang.S["GameTask_10421_8f46b3"], "3. 寻找石化古树");
            await FindPetrifiedTree();

            // 4. 走到石化古树处
            Logger.LogInformation(Lang.S["GameTask_10421_8f46b3"], "4. 走到石化古树处");
            await WalkToPressF();

            // 5. 快速领取奖励并判断是否有下一轮
            Logger.LogInformation(Lang.S["GameTask_10421_8f46b3"], "5. 领取奖励");
            if (!await GettingTreasure())
            {
                Logger.LogInformation(Lang.S["GameTask_10460_bf7571"]);
                break;
            }

            Notify.Event(NotificationEvent.DomainReward).Success(Lang.S["GameTask_10459_485b94"]);
        }
    }

    private void Init()
    {
        LogScreenResolution();
        if (_config.AutoEat)
        {
            TaskTriggerDispatcher.Instance().AddTrigger("AutoEat", null);
        }

        if (_config.SpecifyResinUse)
        {
            Logger.LogInformation(Lang.S["GameTask_10458_4e8665"], "自动秘境，");
        }
        else
        {
            Logger.LogInformation(Lang.S["GameTask_10456_c81dfc"], "自动秘境，");
        }
    }

    private void LogScreenResolution()
    {
        var gameScreenSize = SystemControl.GetGameScreenRect(TaskContext.Instance().GameHandle);
        if (gameScreenSize.Width * 9 != gameScreenSize.Height * 16)
        {
            Logger.LogError(Lang.S["GameTask_10455_634fe1"],
                gameScreenSize.Width, gameScreenSize.Height);
            throw new Exception(Lang.S["GameTask_10454_708f7d"]);
        }

        if (gameScreenSize.Width < 1920 || gameScreenSize.Height < 1080)
        {
            Logger.LogWarning(Lang.S["GameTask_10453_104863"],
                gameScreenSize.Width, gameScreenSize.Height);
        }
    }

    private void RetryTeamInit(CombatScenes combatScenes)
    {
        if (!combatScenes.CheckTeamInitialized())
        {
            combatScenes.InitializeTeam(CaptureToRectArea());
            if (!combatScenes.CheckTeamInitialized())
            {
                throw new Exception(Lang.S["GameTask_10452_517a4d"]);
            }
        }
    }

    private async Task TpDomain()
    {
        // 传送到秘境
        if (!string.IsNullOrEmpty(_taskParam.DomainName))
        {
            if (MapLazyAssets.Instance.DomainPositionMap.TryGetValue(_taskParam.DomainName, out var domainPosition))
            {
                Logger.LogInformation(Lang.S["GameTask_10451_98a525"], _taskParam.DomainName);
                await new TpTask(_ct).Tp(domainPosition.X, domainPosition.Y);
                await Delay(1000, _ct);
                await Bv.WaitForMainUi(_ct);

                var menuFound = false;
                if (Lang.S["GameTask_10450_f6dfdf"].Equals(_taskParam.DomainName))
                {
                    menuFound = await NewRetry.WaitForElementAppear(
                        AutoPickAssets.Instance.PickRo,
                        () => Simulation.SendInput.SimulateAction(GIActions.MoveBackward, KeyType.KeyDown),
                        _ct,
                        20,
                        500
                    );
                    Simulation.SendInput.SimulateAction(GIActions.MoveBackward, KeyType.KeyUp);
                }
                else if (Lang.S["GameTask_10449_8d6c1e"].Equals(_taskParam.DomainName))
                {
                    Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
                    Thread.Sleep(500);
                    Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);

                    menuFound = await NewRetry.WaitForElementAppear(
                        AutoPickAssets.Instance.PickRo,
                        () => Simulation.SendInput.SimulateAction(GIActions.MoveLeft, KeyType.KeyDown),
                        _ct,
                        20,
                        500
                    );
                    Simulation.SendInput.SimulateAction(GIActions.MoveLeft, KeyType.KeyUp);
                }
                else if (Lang.S["GameTask_10448_647201"].Equals(_taskParam.DomainName))
                {
                    menuFound = await NewRetry.WaitForElementAppear(
                        AutoPickAssets.Instance.PickRo,
                        () => { },
                        _ct,
                        20,
                        500
                    );
                }
                else
                {
                    menuFound = await NewRetry.WaitForElementAppear(
                        AutoPickAssets.Instance.PickRo,
                        () => Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown),
                        _ct,
                        20,
                        500
                    );
                    Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                }

                if (!menuFound)
                {
                    throw new Exception(Lang.S["GameTask_10447_044c32"]);
                }

                var menu = await NewRetry.WaitForElementAppear(
                    GetConfirmRa(Lang.S["GameTask_10434_2dfd63"]),
                    () => Simulation.SendInput.Keyboard.KeyPress(AutoPickAssets.Instance.PickVk),
                    _ct,
                    20,
                    500
                );
                if (!menu)
                {
                    throw new Exception(Lang.S["GameTask_10446_92356a"]);
                }
            }
            else
            {
                Logger.LogError(Lang.S["GameTask_10445_739e93"], _taskParam.DomainName);
                throw new Exception($"{Lang.S["GameTask_10444_48f508"]});
            }
        }
    }

    /// <summary>
    /// 切换队伍
    /// </summary>
    /// <param name="partyName"></param>
    /// <returns></returns>
    private async Task<bool> SwitchParty(string? partyName)
    {
        if (!string.IsNullOrEmpty(partyName))
        {
            var b = await new SwitchPartyTask().Start(partyName, _ct);
            await Delay(500, _ct);
            return b;
        }

        return true;
    }

    private async Task EnterDomain()
    {
        var fightAssets = AutoFightAssets.Instance;

        var menuFound = await NewRetry.WaitForElementAppear(
            GetConfirmRa(Lang.S["GameTask_10434_2dfd63"]),
            () => Simulation.SendInput.Keyboard.KeyPress(AutoPickAssets.Instance.PickVk),
            _ct,
            10,
            1000
        );
        if (!menuFound)
        {
            Logger.LogWarning(Lang.S["GameTask_10443_dec35c"]);
        }

        using var limitedFullyStringRa = CaptureToRectArea();
        var limitedFullyStringRaocrList =
            limitedFullyStringRa.FindMulti(RecognitionObject.Ocr(0, 0, limitedFullyStringRa.Width * 0.5,
                limitedFullyStringRa.Height));
        var limitedFullyStringRaocrListdone = limitedFullyStringRaocrList.LastOrDefault(t =>
            Regex.IsMatch(t.Text, this.limitedFullyString) || Regex.IsMatch(t.Text, this.limitedFullyAllString));
        // 检测是否为限时全开秘境
        if (limitedFullyStringRaocrListdone != null)
        {
            Logger.LogInformation(Lang.S["GameTask_10421_8f46b3"], "检测到秘境限时全开");
        }

        var serverTime = ServerTimeHelper.GetServerTimeNow();
        if (serverTime is { DayOfWeek: DayOfWeek.Sunday, Hour: >= 4 } || serverTime is { DayOfWeek: DayOfWeek.Monday, Hour: < 4 } || limitedFullyStringRaocrListdone != null)
        {
            using var artifactArea = CaptureToRectArea().Find(fightAssets.ArtifactAreaRa); //检测是否为圣遗物副本
            if (artifactArea.IsEmpty())
            {
                if (int.TryParse(_taskParam.SundaySelectedValue, out int sundaySelectedValue))
                {
                    if (sundaySelectedValue > 0)
                    {
                        Logger.LogInformation(limitedFullyStringRaocrListdone != null ? Lang.S["GameTask_10440_81c2e6"] : "自动秘境：周日设置了秘境奖励序号 {sundaySelectedValue}", sundaySelectedValue);
                        using var abnormalscreenRa = CaptureToRectArea();
                        GlobalMethod.MoveMouseTo(abnormalscreenRa.Width / 4, abnormalscreenRa.Height / 2); //移到左侧
                        for (var i = 0; i < 100; i++)
                        {
                            Simulation.SendInput.Mouse.VerticalScroll(-1);
                            await Delay(10, _ct);
                        }

                        await Delay(400, _ct);

                        using var abnormalRa = CaptureToRectArea();
                        var ocrList =
                            abnormalRa.FindMulti(RecognitionObject.Ocr(0, 0, abnormalRa.Width * 0.5,
                                abnormalRa.Height));
                        var done = ocrList.LastOrDefault(t =>
                            Regex.IsMatch(t.Text, this.leyLineDisorderLocalizedString));
                        if (done != null)
                        {
                            await Delay(300, _ct);

                            switch (sundaySelectedValue)
                            {
                                case 1:
                                    GlobalMethod.Click(done.X, done.Y - abnormalRa.Height / 5);
                                    break;
                                case 2:
                                    GlobalMethod.Click(done.X, done.Y - abnormalRa.Height / 10);
                                    break;
                                case 3:
                                    GlobalMethod.Click(done.X, done.Y);
                                    break;
                                default:
                                    Logger.LogWarning(Lang.S["GameTask_10439_65c145"],
                                        sundaySelectedValue);
                                    break;
                            }
                        }
                    }
                    else
                    {
                        Logger.LogInformation(limitedFullyStringRaocrListdone != null ? Lang.S["GameTask_10437_b37038"] : "自动秘境：周日秘境未设置特定秘境奖励");
                    }
                }
                else
                {
                    Logger.LogWarning(_taskParam.SundaySelectedValue == "" ? Lang.S["GameTask_10435_2de903"] : "设置秘境奖励序号错误，请检查配置页面");
                }
            }

            await Delay(300, _ct);
        }

        // 点击单人挑战确认并等待队伍界面--使用图像模版匹配的方法，也可以使用文字OCR的方法识别“单人挑战”直到消失
        await NewRetry.WaitForElementAppear(
            ElementAssets.Instance.PartyBtnChooseView,
            async void () =>
            {
                using var ra = CaptureToRectArea();
                var ra2 = ra.Find(fightAssets.ConfirmRa);
                if (!ra2.IsEmpty())
                {
                    ra2.Click();
                    ra2.Dispose();
                    Logger.LogInformation(Lang.S["GameTask_10425_1b3bc2"], "单人挑战");
                }

                using var confirmRectArea2 = ra.Find(RecognitionObject.Ocr(ra.Width * 0.263, ra.Height * 0.32,
                    ra.Width - ra.Width * 0.263 * 2, ra.Height - ra.Height * 0.32 - ra.Height * 0.353));
                if (confirmRectArea2.IsExist() && confirmRectArea2.Text.Contains(Lang.S["GameTask_10433_53552e"]))
                {
                    Logger.LogWarning(Lang.S["GameTask_10432_78453e"], confirmRectArea2.Text);
                    throw new Exception(Lang.S["GameTask_10431_eacdb4"]);
                }
            },
            _ct,
            10,
            1000
        );

        // 等待队伍选择界面出现
        var teamUiFound = await NewRetry.WaitForElementAppear(
            ElementAssets.Instance.PartyBtnChooseView,
            () => { Logger.LogInformation(Lang.S["GameTask_10429_f2310e"], "队伍选择界面"); },
            _ct,
            10,
            1000
        );
        if (!teamUiFound)
        {
            Logger.LogWarning(Lang.S["GameTask_10428_5220a0"]);
        }
        else
        {
            await SwitchParty(_taskParam.PartyName);
        }

        // 点击开始挑战确认并等待“开始挑战”文字消失
        var startFightFound = await NewRetry.WaitForElementDisappear(
            GetConfirmRa(Lang.S["GameTask_10427_33aae2"]),
            screen =>
            {
                screen.Find(fightAssets.ConfirmRa, ra =>
                {
                    ra.Click();
                    ra.Dispose();
                    Logger.LogInformation(Lang.S["GameTask_10425_1b3bc2"], "开始挑战");
                });
            },
            _ct,
            10,
            1000
        );
        if (!startFightFound)
        {
            Logger.LogWarning(Lang.S["GameTask_10426_68828d"]);
        }

        // 载入
        await Delay(1000, _ct);
    }

    private async Task CloseDomainTip()
    {
        //先等待秘境提示出现,如果直接出现Enter也属于完成条件
        var domainTipFound = await NewRetry.WaitForAction(() =>
        {
            using var ra = CaptureToRectArea();

            var ocrList = ra.FindMulti(RecognitionObject.Ocr(0, ra.Height * 0.2, ra.Width, ra.Height * 0.6));
            var ocrListLeft = ra.Find(AutoFightAssets.Instance.AbnormalIconRa);
            return (ocrList.Any(t => t.Text.Contains(leyLineDisorderLocalizedString) ||
                                     t.Text.Contains(clickanywheretocloseLocalizedString))) || ocrListLeft.IsExist();
        }, _ct, 40, 500);
        if (!domainTipFound)
        {
            Logger.LogWarning(Lang.S["GameTask_10424_2b83d6"]);
        }

        //持续点击，直到左下角出现目标文字
        var leftBottomFound = await NewRetry.WaitForAction(() =>
        {
            using var ra = CaptureToRectArea();
            var ocrList = ra.FindMulti(RecognitionObject.Ocr(0, ra.Height * 0.2, ra.Width, ra.Height * 0.6));
            // 查找目标文字
            var done = ocrList.FirstOrDefault(t =>
                Regex.IsMatch(t.Text, this.leyLineDisorderLocalizedString) ||
                Regex.IsMatch(t.Text, this.clickanywheretocloseLocalizedString));
            if (done != null)
            {
                done.Click();
                done.Dispose();
                Logger.LogInformation(Lang.S["GameTask_10425_1b3bc2"], done.Text);
            }

            // 检查左下角区域是否还存在目标文字，消失则继续，存在则结束
            using var leftBottom = CaptureToRectArea();
            var leftBottomOcr = leftBottom.Find(AutoFightAssets.Instance.AbnormalIconRa);
            return leftBottomOcr.IsExist();
        }, _ct, 20, 500);
        if (!leftBottomFound)
        {
            //尝试随意点击一下右下角
            GameCaptureRegion.GameRegion1080PPosClick(1515, 892);
            Logger.LogWarning(Lang.S["GameTask_10424_2b83d6"]);
        }

        await Delay(500, _ct);
    }

    private List<CombatCommand> FindCombatScriptAndSwitchAvatar(CombatScenes combatScenes)
    {
        var combatCommands = _combatScriptBag.FindCombatScript(combatScenes.GetAvatars());
        var avatar = combatScenes.SelectAvatar(combatCommands[0].Name);
        avatar?.SwitchWithoutCts();
        Sleep(200);
        return combatCommands;
    }

    /// <summary>
    /// 走到钥匙处启动
    /// </summary>
    private async Task WalkToPressF()
    {
        if (_ct.IsCancellationRequested)
        {
            return;
        }

        await Task.Run((Action)(() =>
        {
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
            Sleep(30, _ct);
            // 组合键好像不能直接用 postmessage
            if (!_config.WalkToF)
            {
                Simulation.SendInput.SimulateAction(GIActions.SprintKeyboard, KeyType.KeyDown);
            }

            try
            {
                var startTime = DateTime.Now;
                while (!_ct.IsCancellationRequested)
                {
                    using var fRectArea = Common.TaskControl.CaptureToRectArea().Find(AutoPickAssets.Instance.PickRo);
                    if (fRectArea.IsEmpty())
                    {
                        Sleep(100, _ct);
                    }
                    else
                    {
                        Logger.LogInformation(Lang.S["GameTask_10423_d4417d"]);
                        Simulation.SendInput.Keyboard.KeyPress(AutoPickAssets.Instance.PickVk);
                        break;
                    }

                    // 超时直接放弃整个秘境
                    if (DateTime.Now - startTime > TimeSpan.FromSeconds(60))
                    {
                        Logger.LogWarning(Lang.S["GameTask_10421_8f46b3"], "前往目标位置处超时，如果选择了秘境名称，将在传送后重试秘境！");
                        Avatar.TpForRecover(_ct, new RetryException(Lang.S["GameTask_10420_427869"]));
                    }
                }
            }
            finally
            {
                Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                Sleep(50);
                if (!_config.WalkToF)
                {
                    Simulation.SendInput.SimulateAction(GIActions.SprintKeyboard, KeyType.KeyUp);
                }
            }
        }), _ct);
    }

    private Task StartFight(CombatScenes combatScenes, List<CombatCommand> combatCommands)
    {
        CancellationTokenSource cts = new();
        _ct.Register(cts.Cancel);
        combatScenes.BeforeTask(cts.Token);
        // 战斗操作
        var combatTask = new Task(() =>
        {
            try
            {
                AutoFightTask.FightStatusFlag = true;
                while (!cts.Token.IsCancellationRequested)
                {
                    // 通用化战斗策略
                    foreach (var command in combatCommands)
                    {
                        command.Execute(combatScenes);
                    }
                }
            }
            catch (NormalEndException e)
            {
                Logger.LogInformation(Lang.S["GameTask_10419_85c3aa"], e.Message);
            }
            catch (Exception e)
            {
                Logger.LogWarning(e.Message);
                throw;
            }
            finally
            {
                Logger.LogInformation(Lang.S["GameTask_10418_b5582c"]);
                Simulation.ReleaseAllKey();
                AutoFightTask.FightStatusFlag = false;
            }
        }, cts.Token);

        // 对局结束检测
        var domainEndTask = DomainEndDetectionTask(cts);
        // 自动吃药
        // var autoEatRecoveryHpTask = AutoEatRecoveryHpTask(cts.Token);
        combatTask.Start();
        domainEndTask.Start();
        // autoEatRecoveryHpTask.Start();
        return Task.WhenAll(combatTask, domainEndTask);
    }

    private void EndFightWait()
    {
        if (_ct.IsCancellationRequested)
        {
            return;
        }

        var s = TaskContext.Instance().Config.AutoDomainConfig.FightEndDelay;
        if (s > 0)
        {
            Logger.LogInformation(Lang.S["GameTask_10417_09b7a0"], s);
            Sleep((int)(s * 1000), _ct);
        }
    }

    /// <summary>
    /// 对局结束检测
    /// </summary>
    private Task DomainEndDetectionTask(CancellationTokenSource cts)
    {
        return new Task(async () =>
        {
            try
            {
                while (!_ct.IsCancellationRequested)
                {
                    if (IsDomainEnd())
                    {
                        await cts.CancelAsync();
                        break;
                    }

                    await Delay(1000, cts.Token);
                }
            }
            catch
            {
            }
        }, cts.Token);
    }

    private bool IsDomainEnd()
    {
        using var ra = CaptureToRectArea();

        var endTipsRect = ra.DeriveCrop(AutoFightAssets.Instance.EndTipsUpperRect);
        var text = OcrFactory.Paddle.Ocr(endTipsRect.SrcMat);
        if (Regex.IsMatch(text, this.challengeCompletedLocalizedString))
        {
            Logger.LogInformation(Lang.S["GameTask_10416_204900"]);
            return true;
        }

        endTipsRect = ra.DeriveCrop(AutoFightAssets.Instance.EndTipsRect);
        text = OcrFactory.Paddle.Ocr(endTipsRect.SrcMat);
        if (Regex.IsMatch(text, this.autoLeavingLocalizedString))
        {
            Logger.LogInformation(Lang.S["GameTask_10415_2fbfa9"]);
            return true;
        }

        return false;
    }

    private Task AutoEatRecoveryHpTask(CancellationToken ct)
    {
        return new Task(async () =>
        {
            if (!_config.AutoEat)
            {
                return;
            }

            if (!IsTakeFood())
            {
                Logger.LogInformation(Lang.S["GameTask_10413_9e7aec"], "便携营养袋");
                return;
            }

            try
            {
                while (!_ct.IsCancellationRequested)
                {
                    if (Bv.CurrentAvatarIsLowHp(CaptureToRectArea()))
                    {
                        // 模拟按键 "Z"
                        Simulation.SendInput.SimulateAction(GIActions.QuickUseGadget);
                        Logger.LogInformation(Lang.S["GameTask_10412_da36ff"]);
                        // TODO 吃饱了会一直吃
                    }

                    await Delay(500, ct);
                }
            }
            catch (Exception e)
            {
                Logger.LogDebug(e, Lang.S["GameTask_10411_1d3563"]);
            }
        }, ct);
    }

    private bool IsTakeFood()
    {
        // 获取图像
        using var ra = CaptureToRectArea();
        // 识别道具图标下是否是数字
        var s = TaskContext.Instance().SystemInfo.AssetScale;
        var countArea = ra.DeriveCrop(1800 * s, 845 * s, 40 * s, 20 * s);
        var count = OcrFactory.Paddle.OcrWithoutDetector(countArea.CacheGreyMat);
        return int.TryParse(count, out _);
    }

    /// <summary>
    /// 旋转视角后寻找石化古树
    /// </summary>
    private Task FindPetrifiedTree()
    {
        CancellationTokenSource treeCts = new();
        _ct.Register(treeCts.Cancel);
        // 中键回正视角
        Simulation.SendInput.Mouse.MiddleButtonClick();
        Sleep(900, _ct);

        // 左右移动直到石化古树位于屏幕中心任务
        var moveAvatarTask = MoveAvatarHorizontallyTask(treeCts);

        // 锁定东方向视角线程
        var lockCameraToEastTask = LockCameraToEastTask(treeCts, moveAvatarTask);
        lockCameraToEastTask.Start();
        return Task.WhenAll(moveAvatarTask, lockCameraToEastTask);
    }

    private Task MoveAvatarHorizontallyTask(CancellationTokenSource treeCts)
    {
        return new Task(() =>
        {
            var keyConfig = TaskContext.Instance().Config.KeyBindingsConfig;
            var moveLeftKey = keyConfig.MoveLeft.ToVK();
            var moveRightKey = keyConfig.MoveRight.ToVK();
            var moveForwardKey = keyConfig.MoveForward.ToVK();
            var captureArea = TaskContext.Instance().SystemInfo.ScaleMax1080PCaptureRect;
            var middleX = captureArea.Width / 2;
            var leftKeyDown = false;
            var rightKeyDown = false;
            var noDetectCount = 0;
            var prevKey = moveLeftKey;
            var backwardsAndForwardsCount = 0;
            while (!_ct.IsCancellationRequested)
            {
                var treeRect = DetectTree(CaptureToRectArea());
                if (treeRect != default)
                {
                    var treeMiddleX = treeRect.X + treeRect.Width / 2;
                    if (treeRect.X + treeRect.Width < middleX && !_config.ShortMovement)
                    {
                        backwardsAndForwardsCount = 0;
                        // 树在左边 往左走
                        Debug.WriteLine($"{Lang.S["GameTask_10410_06ee59"]});
                        if (rightKeyDown)
                        {
                            // 先松开D键
                            Simulation.SendInput.Keyboard.KeyUp(moveRightKey);
                            rightKeyDown = false;
                        }

                        if (!leftKeyDown)
                        {
                            Simulation.SendInput.Keyboard.KeyDown(moveLeftKey);
                            leftKeyDown = true;
                        }
                    }
                    else if (treeRect.X > middleX && !_config.ShortMovement)
                    {
                        backwardsAndForwardsCount = 0;
                        // 树在右边 往右走
                        Debug.WriteLine($"{Lang.S["GameTask_10409_47f1b7"]});
                        if (leftKeyDown)
                        {
                            // 先松开A键
                            Simulation.SendInput.Keyboard.KeyUp(moveLeftKey);
                            leftKeyDown = false;
                        }

                        if (!rightKeyDown)
                        {
                            Simulation.SendInput.Keyboard.KeyDown(moveRightKey);
                            rightKeyDown = true;
                        }
                    }
                    else
                    {
                        // 树在中间 松开所有键
                        if (rightKeyDown)
                        {
                            Simulation.SendInput.Keyboard.KeyUp(moveRightKey);
                            prevKey = moveRightKey;
                            rightKeyDown = false;
                        }

                        if (leftKeyDown)
                        {
                            Simulation.SendInput.Keyboard.KeyUp(moveLeftKey);
                            prevKey = moveLeftKey;
                            leftKeyDown = false;
                        }

                        // 松开按键后使用小碎步移动
                        if (treeMiddleX < middleX)
                        {
                            if (prevKey == moveRightKey)
                            {
                                backwardsAndForwardsCount++;
                            }

                            Simulation.SendInput.Keyboard.KeyDown(moveLeftKey);
                            Sleep(60);
                            Simulation.SendInput.Keyboard.KeyUp(moveLeftKey);
                            prevKey = moveLeftKey;
                        }
                        else if (treeMiddleX > middleX)
                        {
                            if (prevKey == moveLeftKey)
                            {
                                backwardsAndForwardsCount++;
                            }

                            Simulation.SendInput.Keyboard.KeyDown(moveRightKey);
                            Sleep(60);
                            Simulation.SendInput.Keyboard.KeyUp(moveRightKey);
                            prevKey = moveRightKey;
                        }
                        else
                        {
                            Simulation.SendInput.Keyboard.KeyDown(moveForwardKey);
                            Sleep(60);
                            Simulation.SendInput.Keyboard.KeyUp(moveForwardKey);
                            Sleep(500, _ct);
                            treeCts.Cancel();
                            break;
                        }
                    }
                }
                else
                {
                    backwardsAndForwardsCount = 0;
                    // 左右巡逻
                    noDetectCount++;
                    if (noDetectCount > 40)
                    {
                        if (leftKeyDown)
                        {
                            Simulation.SendInput.Keyboard.KeyUp(moveLeftKey);
                            leftKeyDown = false;
                        }

                        if (!rightKeyDown)
                        {
                            Simulation.SendInput.Keyboard.KeyDown(moveRightKey);
                            rightKeyDown = true;
                        }
                    }
                    else
                    {
                        if (rightKeyDown)
                        {
                            Simulation.SendInput.Keyboard.KeyUp(moveRightKey);
                            rightKeyDown = false;
                        }

                        if (!leftKeyDown)
                        {
                            Simulation.SendInput.Keyboard.KeyDown(moveLeftKey);
                            leftKeyDown = true;
                        }
                    }
                }

                if (backwardsAndForwardsCount >= _config.LeftRightMoveTimes)
                {
                    // 左右移动5次说明已经在树中心了
                    Simulation.SendInput.Keyboard.KeyDown(moveForwardKey);
                    Sleep(60);
                    Simulation.SendInput.Keyboard.KeyUp(moveForwardKey);
                    Sleep(500, _ct);
                    treeCts.Cancel();
                    break;
                }

                Sleep(60, _ct);
            }

            VisionContext.Instance().DrawContent.ClearAll();
        });
    }

    private Rect DetectTree(ImageRegion region)
    {
        var result = _predictor.Predictor.Detect(region.CacheImage);
        var list = new List<RectDrawable>();
        foreach (var box in result)
        {
            var rect = new Rect(box.Bounds.X, box.Bounds.Y, box.Bounds.Width, box.Bounds.Height);
            list.Add(region.ToRectDrawable(rect, "tree"));
        }

        VisionContext.Instance().DrawContent.PutOrRemoveRectList("TreeBox", list);

        if (list.Count > 0)
        {
            var box = result[0];
            return new Rect(box.Bounds.X, box.Bounds.Y, box.Bounds.Width, box.Bounds.Height);
        }

        return default;
    }

    private Task LockCameraToEastTask(CancellationTokenSource cts, Task moveAvatarTask)
    {
        return new Task(() =>
        {
            var continuousCount = 0; // 连续东方向次数
            var started = false;
            while (!cts.Token.IsCancellationRequested)
            {
                using var captureRegion = CaptureToRectArea();
                var angle = CameraOrientation.Compute(captureRegion.SrcMat);
                CameraOrientation.DrawDirection(captureRegion, angle);
                if (angle is >= 356 or <= 4)
                {
                    // 算作对准了
                    continuousCount++;
                    // 360 度 东方向视角
                    if (continuousCount > 5)
                    {
                        if (!started && moveAvatarTask.Status != TaskStatus.Running)
                        {
                            started = true;
                            moveAvatarTask.Start();
                        }
                    }
                }
                else
                {
                    continuousCount = 0;
                }

                if (angle <= 180)
                {
                    // 左移视角
                    var moveAngle = (int)Math.Round(angle);
                    if (moveAngle > 2)
                    {
                        moveAngle *= 2;
                    }

                    Simulation.SendInput.Mouse.MoveMouseBy(-moveAngle, 0);
                }
                else if (angle is > 180 and < 360)
                {
                    // 右移视角
                    var moveAngle = 360 - (int)Math.Round(angle);
                    if (moveAngle > 2)
                    {
                        moveAngle *= 2;
                    }

                    Simulation.SendInput.Mouse.MoveMouseBy(moveAngle, 0);
                }

                Sleep(100, _ct);
            }

            Logger.LogInformation(Lang.S["GameTask_10408_7c1aba"]);
            VisionContext.Instance().DrawContent.ClearAll();
        });
    }

    /// <summary>
    /// 领取奖励
    /// </summary>
    private async Task<bool> GettingTreasure()
    {
        bool isLastTurn = false;
        // 等待窗口弹出
        await Delay(300, _ct);

        // 1. OCR 直到确认弹出框弹出
        bool chooseResinPrompt = await NewRetry.WaitForAction(() =>
        {
            using var ra = CaptureToRectArea();
            var regionList = ra.FindMulti(RecognitionObject.Ocr(ra.Width * 0.25, ra.Height * 0.2, ra.Width * 0.5, ra.Height * 0.6));
            var res = regionList.FirstOrDefault(t => t.Text.Contains(Lang.S["GameTask_10407_38000b"]));
            if (res != null)
            {
                // 解决水龙王按下左键后没松开，然后后续点击按下就没反应了，界面上点一下
                res.Click();
                return true;
            }

            return false;
        }, _ct, 10, 500);
        Debug.WriteLine(Lang.S["GameTask_10406_b4bdc2"]);
        await Delay(800, _ct);

        // 再 OCR 一次，弹出框，确认当前是否有原粹树脂
        using var ra2 = CaptureToRectArea();
        var textListInPrompt = ra2.FindMulti(RecognitionObject.Ocr(ra2.Width * 0.25, ra2.Height * 0.2, ra2.Width * 0.5, ra2.Height * 0.6));
        if (textListInPrompt.Any(t => t.Text.Contains(Lang.S["GameTask_10404_d82611"]) || t.Text.Contains("补充原粹树脂")))
        {
            // 没有原粹树脂，直接退出秘境
            Logger.LogInformation(Lang.S["GameTask_10403_4c2100"]);
            await ExitDomain();
            return false;
        }

        if (chooseResinPrompt)
        {
            using var ra3 = CaptureToRectArea();

            if (!_taskParam.SpecifyResinUse)
            {
                // 自动刷干树脂
                // 识别树脂状况
                var resinStatus = ResinStatus.RecogniseFromRegion(ra3, TaskContext.Instance().SystemInfo, OcrFactory.Paddle);
                resinStatus.Print(Logger);

                if (resinStatus is { CondensedResinCount: <= 0, OriginalResinCount: < 20 })
                {
                    Logger.LogWarning(Lang.S["GameTask_10401_dd437f"]);
                    await ExitDomain();
                    return false;
                }

                bool resinUsed = false;
                if (resinStatus.CondensedResinCount > 0)
                {
                    (resinUsed, _) = PressUseResin(ra3, Lang.S["GameTask_10385_a7b73a"]);
                    resinStatus.CondensedResinCount -= 1;
                }
                else if (resinStatus.OriginalResinCount >= 20)
                {
                    (resinUsed, var num) = PressUseResin(ra3, Lang.S["GameTask_10384_9fa864"]);
                    resinStatus.OriginalResinCount -= num;
                }

                if (!resinUsed)
                {
                    Logger.LogWarning(Lang.S["GameTask_10400_5d873d"], "树脂不足", "OCR 识别失败");
                    await ExitDomain();
                    return false;
                }

                if (resinStatus is { CondensedResinCount: <= 0, OriginalResinCount: < 20 })
                {
                    // 没树脂了就是最后一回合了
                    isLastTurn = true;
                }
            }
            else
            {
                // 指定使用树脂
                var textListInPrompt2 = ra3.FindMulti(RecognitionObject.Ocr(ra3.Width * 0.25, ra3.Height * 0.2, ra3.Width * 0.5, ra3.Height * 0.6));
                // 按优先级使用
                int successCount = 0;
                foreach (var record in _resinPriorityListWhenSpecifyUse)
                {
                    if (record.RemainCount > 0)
                    {
                        var (success, _) = PressUseResin(textListInPrompt2, record.Name);
                        if (success)
                        {
                            record.RemainCount -= 1;
                            Logger.LogInformation(Lang.S["GameTask_10399_7babb1"], record.Name, record.MaxCount - record.RemainCount, record.MaxCount);
                            successCount++;
                            break;
                        }
                    }
                }

                if (_resinPriorityListWhenSpecifyUse.Sum(o => o.RemainCount) <= 0)
                {
                    // 全部刷完
                    isLastTurn = true;
                }

                if (successCount == 0)
                {
                    // 没有找到对应的树脂
                    Logger.LogWarning(Lang.S["GameTask_10398_2bbfc5"]);
                    Logger.LogInformation(Lang.S["GameTask_10397_51e265"], string.Join(", ", _resinPriorityListWhenSpecifyUse.Select(o => $"{o.Name}({o.MaxCount - o.RemainCount}/{o.MaxCount})")));
                    await ExitDomain();
                    return false;
                }
            }
        }
        else
        {
            // 如果没有选择树脂的提示，说明只有原粹树脂
            // 继续向下执行
        }

        Sleep(1000, _ct);

        for (var i = 0; i < 30; i++)
        {
            using var ra = CaptureToRectArea();
            // 优先点击继续
            using var confirmRectArea = ra.Find(AutoFightAssets.Instance.ConfirmRa);
            if (!confirmRectArea.IsEmpty())
            {
                if (isLastTurn)
                {
                    // 最后一回合 退出
                    var exitRectArea = ra.Find(AutoFightAssets.Instance.ExitRa);
                    if (!exitRectArea.IsEmpty())
                    {
                        exitRectArea.Click();
                        return false;
                    }
                }
                else
                {
                    if (!chooseResinPrompt)
                    {
                        // TODO 前面没有弹框的情况下，意味着只有原粹树脂，要再识别一次右上角确认树脂余量，没有余量直接退出
                    }

                    // 有体力继续
                    confirmRectArea.Click();
                    await Delay(60, _ct); // 双击
                    confirmRectArea.Click();

                    if (!chooseResinPrompt)
                    {
                        // 真没树脂了还有提示兜底
                        await Delay(900, _ct);
                        var textListInNoResinPrompt = CaptureToRectArea().FindMulti(RecognitionObject.Ocr(ra2.Width * 0.25, ra2.Height * 0.2, ra2.Width * 0.5, ra2.Height * 0.6));
                        if (textListInNoResinPrompt.Any(t => t.Text.Contains(Lang.S["GameTask_10394_d42a1f"]) && t.Text.Contains("挑战") && t.Text.Contains("秘境")))
                        {
                            var cancelBtn = textListInNoResinPrompt.FirstOrDefault(t => t.Text.Contains(Lang.S["Btn_Cancel"]));
                            if (cancelBtn != null)
                            {
                                cancelBtn.Click();
                                return false;
                            }
                        }
                    }

                    return true;
                }
            }

            Sleep(300, _ct);
        }

        throw new NormalEndException(Lang.S["GameTask_10393_a00fd0"]);
    }

    private async Task ExitDomain()
    {
        Simulation.SendInput.Keyboard.KeyPress(VK.VK_ESCAPE);
        await Delay(500, _ct);
        Simulation.SendInput.Keyboard.KeyPress(VK.VK_ESCAPE);
        await Delay(800, _ct);
        Bv.ClickBlackConfirmButton(CaptureToRectArea());
    }

    public static (bool, int) PressUseResin(ImageRegion ra, string resinName)
    {
        var regionList = ra.FindMulti(RecognitionObject.Ocr(ra.Width * 0.25, ra.Height * 0.2, ra.Width * 0.5, ra.Height * 0.6));
        return PressUseResin(regionList, resinName);
    }

    public static (bool, int) PressUseResin(List<Region> regionList, string resinName)
    {
        var resinKey = regionList.FirstOrDefault(t => t.Text.Contains(resinName));
        if (resinKey != null)
        {
            // 找到树脂名称对应的按键，关键词为使用，是同一行的（高度相交）
            var useList = regionList.Where(t => t.Text.Contains(Lang.S["GameTask_10392_ecff77"])).ToList();
            if (useList.Count != 0)
            {
                // 找到使用按键
                var useKey = useList.FirstOrDefault(t => t.X > TaskContext.Instance().SystemInfo.ScaleMax1080PCaptureRect.Width / 2
                                                         && IsHeightOverlap(t, resinKey));
                if (useKey != null)
                {
                    // 点击使用
                    useKey.Click();
                    // 解决水龙王按下左键后没松开，然后后续点击按下就没反应了。使用双击
                    Sleep(60);
                    useKey.Click();
                    var num = GetResinNum(resinKey, resinName);
                    Logger.LogInformation(Lang.S["GameTask_10391_225cef"], resinName, num);
                    return (true, num);
                }
                else
                {
                    Logger.LogWarning(Lang.S["GameTask_10390_685db5"], resinName);
                }
            }
            else
            {
                Logger.LogWarning(Lang.S["GameTask_10390_685db5"], resinName);
            }
        }

        return (false, 0);
    }

    private static int GetResinNum(Region region, string resinName)
    {
        if (resinName == Lang.S["GameTask_10384_9fa864"])
        {
            if (region.Text.Contains("20"))
            {
                return 20;
            }
            else if (region.Text.Contains("40"))
            {
                return 40;
            }
            else
            {
                Logger.LogWarning(Lang.S["GameTask_10389_a6decf"]);
                return 20;
            }
        }
        else if (resinName == Lang.S["GameTask_10385_a7b73a"] || resinName == "脆弱树脂" || resinName == "须臾树脂")
        {
            return 1;
        }
        else
        {
            throw new ArgumentException(Lang.S["GameTask_10387_ad5a2e"]);
        }
    }

    /// <summary>
    /// 判断两个区域在垂直方向上是否有重叠
    /// </summary>
    private static bool IsHeightOverlap(Region region1, Region region2)
    {
        int region1Top = region1.Y;
        int region1Bottom = region1.Y + region1.Height;
        int region2Top = region2.Y;
        int region2Bottom = region2.Y + region2.Height;

        // 检查区域是否在垂直方向上重叠
        return (region1Top <= region2Bottom && region1Bottom >= region2Top);
    }

    private  async Task ArtifactSalvage()
    {
        if (!_taskParam.AutoArtifactSalvage)
        {
            return;
        }

        if (!int.TryParse(_taskParam.MaxArtifactStar, out var star))
        {
            star = 4;
        }

        await new AutoArtifactSalvageTask(new AutoArtifactSalvageTaskParam(star, javaScript: null, artifactSetFilter: null, maxNumToCheck: null, recognitionFailurePolicy: null)).Start(_ct);
    }
}