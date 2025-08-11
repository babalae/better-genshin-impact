using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.BgiVision;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoDomain.Model;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.QuickTeleport.Assets;
using BetterGenshinImpact.Helpers.Extensions;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using static Vanara.PInvoke.User32;

namespace BetterGenshinImpact.GameTask.AutoStygianOnslaught;

public class AutoStygianOnslaughtTask : ISoloTask
{
    private readonly ILogger<AutoStygianOnslaughtTask> _logger = App.GetLogger<AutoStygianOnslaughtTask>();


    public string Name => "自动幽境危战";

    private readonly AutoStygianOnslaughtConfig _taskParam;

    private readonly CombatScriptBag _combatScriptBag;

    private CancellationToken _ct;


    private List<ResinUseRecord> _resinPriorityListWhenSpecifyUse;

    private LowerHeadThenWalkToTask? _lowerHeadThenWalkToTask;

    public AutoStygianOnslaughtTask(AutoStygianOnslaughtConfig taskParam, string path)
    {
        AutoFightAssets.DestroyInstance();
        _taskParam = taskParam;

        _combatScriptBag = CombatScriptParser.ReadAndParse(path);

        _resinPriorityListWhenSpecifyUse = ResinUseRecord.BuildFromDomainParam(taskParam);
    }

    public async Task Start(CancellationToken ct)
    {
        _lowerHeadThenWalkToTask = new LowerHeadThenWalkToTask("chest_tip.png", 20000);
        _ct = ct;

        Init();
        Notify.Event(NotificationEvent.DomainStart).Success($"{Name}启动");

        await DoDomain();

        await Delay(3000, ct);

        await ArtifactSalvage();
        Notify.Event(NotificationEvent.DomainEnd).Success($"{Name}结束");
    }

    private async Task DoDomain()
    {
        var page = new BvPage(_ct);

        // 前置进入秘境
        await TpToDomain(page);
        await EnterDomain(page);
        await ChooseBoss(page);

        for (var i = 0; i < 9999; i++)
        {
            // 确认载入
            bool res = await Bv.WaitUntilFound(ElementAssets.Instance.LeylineDisorderIconRo, _ct, 60);
            if (!res)
            {
                throw new Exception("幽境危战进入秘境失败！");
            }
            
            await Delay(1500, _ct); // 开始的三秒计时

            // 队伍没初始化成功则重试
            var combatScenes = new CombatScenes().InitializeTeam(CaptureToRectArea());
            if (!combatScenes.CheckTeamInitialized())
            {
                throw new Exception("识别队伍角色失败！");
            }

            // 0. 切换到第一个角色
            var combatCommands = FindCombatScriptAndSwitchAvatar(combatScenes);

            await Delay(1500, _ct); // 开始的三秒计时
            // 走到boss前面
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
            await Delay(1200, _ct);
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);


            // 2. 执行战斗（战斗线程、视角线程、检测战斗完成线程）
            _logger.LogInformation($"{Name}：{{Text}}", "2. 执行战斗策略");
            await StartFight(combatScenes, combatCommands);
            await Delay(500, _ct);

            // 3.判断是否成功（存在确认按钮就是失败）
            using var ra = CaptureToRectArea();
            if (ra.Find(ElementAssets.Instance.BtnWhiteConfirm).IsExist())
            {
                _logger.LogWarning($"{Name}：{{Text}}", "挑战失败，重试！");
                Bv.ClickWhiteCancelButton(ra);
                continue;
            }

            Bv.ClickWhiteCancelButton(ra); // 点击返回后是主角
            await Bv.WaitUntilFound(ElementAssets.Instance.LeylineDisorderIconRo, _ct);
            await Delay(6000, _ct); // 等待载入完成

            // 4. 寻找地脉花
            _logger.LogInformation($"{Name}：{{Text}}", "3. 寻找地脉花");
            await _lowerHeadThenWalkToTask!.Start(_ct);
            Simulation.SendInput.Keyboard.KeyPress(AutoPickAssets.Instance.PickVk);


            // 5. 快速领取奖励并判断是否有下一轮
            _logger.LogInformation($"{Name}：{{Text}}", "5. 领取奖励");
            if (!await GettingTreasure())
            {
                _logger.LogInformation($"体力耗尽或者设置轮次已达标，{Name}");
                break;
            }

            Notify.Event(NotificationEvent.DomainReward).Success($"{Name}奖励领取");
        }

        await ExitDomain(page);
    }

    private void Init()
    {
        LogScreenResolution();
        if (_taskParam.SpecifyResinUse)
        {
            _logger.LogInformation("→ {Text} 指定使用树脂", $"{Name}，");
        }
        else
        {
            _logger.LogInformation("→ {Text} 用尽所有浓缩树脂和原粹树脂后结束", $"{Name}，");
        }
    }

    private void LogScreenResolution()
    {
        var gameScreenSize = SystemControl.GetGameScreenRect(TaskContext.Instance().GameHandle);
        if (gameScreenSize.Width * 9 != gameScreenSize.Height * 16)
        {
            _logger.LogError($"游戏窗口分辨率不是 16:9 ！当前分辨率为 {{Width}}x{{Height}} , 非 16:9 分辨率的游戏无法正常使用{Name}功能 !",
                gameScreenSize.Width, gameScreenSize.Height);
            throw new Exception("游戏窗口分辨率不是 16:9");
        }

        if (gameScreenSize.Width < 1920 || gameScreenSize.Height < 1080)
        {
            _logger.LogWarning($"游戏窗口分辨率小于 1920x1080 ！当前分辨率为 {{Width}}x{{Height}} , 小于 1920x1080 的分辨率的游戏可能无法正常使用{Name}功能 !",
                gameScreenSize.Width, gameScreenSize.Height);
        }
    }

    private async Task TpToDomain(BvPage page)
    {
        await new ReturnMainUiTask().Start(_ct);
        await Delay(100, _ct);

        // 检查是否当前已经在秘境前面，如果已经在了就直接进入
        if (page.Locator(AutoPickAssets.Instance.PickRo).WithRoi(r => r.CutRight(0.5)).IsExist())
        {
            if (page.GetByText("幽境危战").WithRoi(r => r.CutRight(0.5)).IsExist())
            {
                Simulation.SendInput.Keyboard.KeyPress(AutoPickAssets.Instance.PickVk);
                _logger.LogInformation($"{Name}：交互秘境");
                return;
            }
        }

        // 使用传送方式进入

        // F5 打开活动
        Simulation.SendInput.SimulateAction(GIActions.OpenTheEventsMenu);
        await page.GetByText("活动一览").WithRoi(r => r.CutLeftTop(0.3,0.2)).WaitFor();
        await Delay(500, _ct);

        if (page.GetByText("幽境危战").WithRoi(r => r.CutRight(0.5)).IsExist())
        {
            await page.GetByText("前往挑战").WithRoi(r => r.CutRight(0.5)).Click();
        }
        else if (page.GetByText("幽境危战").WithRoi(r => r.CutRight(0.3)).IsExist())
        {
            await page.GetByText("幽境危战").WithRoi(r => r.CutRight(0.3)).Click();
            await Delay(1500, _ct);
            await page.GetByText("前往挑战").WithRoi(r => r.CutRight(0.5)).Click();
        }

        _logger.LogInformation($"{Name}：点击前往挑战");

        // 传送
        await Delay(1000, _ct);
        await page.Locator(QuickTeleportAssets.Instance.TeleportButtonRo).Click();
        _logger.LogInformation($"{Name}：点击传送");
        await Delay(800, _ct);

        // 等待传送完成
        await page.Locator(ElementAssets.Instance.PaimonMenuRo).WaitFor();
        _logger.LogInformation($"{Name}：传送完成");

        await Delay(2000, _ct);
        Simulation.SendInput.Keyboard.KeyPress(AutoPickAssets.Instance.PickVk);
        _logger.LogInformation($"{Name}：交互秘境");
    }

    private async Task EnterDomain(BvPage page)
    {
        await Delay(4000, _ct); // 等待动画完成
        await page.Locator(ElementAssets.Instance.BtnWhiteConfirm)
            .WithRoi(r => r.CutRight(0.5))
            .ClickUntilDisappears();
        _logger.LogInformation($"{Name}：进入秘境");

        await Delay(2000, _ct);
        await page.Locator(ElementAssets.Instance.LeylineDisorderIconRo).WaitFor(60000);
        await Delay(1000, _ct);

        _logger.LogInformation($"{Name}：步行前往钥匙");
        await new WalkToFTask().Start(_ct);
        await Delay(100, _ct);
        await page.GetByText("开始挑战").WithRoi(r => r.CutRight(0.3)).WaitFor();
    }

    private async Task ChooseBoss(BvPage page)
    {
        await Delay(300, _ct);
        var ra = CaptureToRectArea();

        var ocrList = ra.FindMulti(RecognitionObject.OcrThis);
        if (ocrList.Any(o => o.Text.Contains("好友挑战")) && ocrList.Any(o => o.Text.Contains("开始挑战")))
        {
            // 选择boss
            _logger.LogInformation($"{Name}：选择BOSS编号{{Text}}", _taskParam.BossNum);
            if (_taskParam.BossNum == 1)
            {
                page.Click(196, 346);
            }
            else if (_taskParam.BossNum == 2)
            {
                page.Click(237, 541);
            }
            else if (_taskParam.BossNum == 3)
            {
                page.Click(203, 728);
            }

            await Delay(120, _ct);

            // 幽境危战确认界面
            Bv.ClickWhiteConfirmButton(ra);

            // 载入动画
            await Delay(3000, _ct);
        }
        else
        {
            _logger.LogWarning("当前界面不是幽境危战{Msg1}界面，请注意是旅行者打开钥匙后弹出的界面！", "开始挑战");
            throw new NormalEndException("当前界面不是幽境危战开始挑战界面，请注意是旅行者打开钥匙后弹出的界面！");
        }
    }


    private List<CombatCommand> FindCombatScriptAndSwitchAvatar(CombatScenes combatScenes)
    {
        var combatCommands = _combatScriptBag.FindCombatScript(combatScenes.GetAvatars());
        var avatar = combatScenes.SelectAvatar(combatCommands[0].Name);
        avatar?.SwitchWithoutCts();
        Sleep(200);
        return combatCommands;
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
                _logger.LogInformation("战斗操作中断：{Msg}", e.Message);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e.Message);
                throw;
            }
            finally
            {
                _logger.LogInformation("自动战斗线程结束");
                Simulation.ReleaseAllKey();
                Simulation.SendInput.Mouse.LeftButtonUp();
            }
        }, cts.Token);

        // 对局结束检测
        var domainEndTask = DomainEndDetectionTask(cts);
        combatTask.Start();
        domainEndTask.Start();
        return Task.WhenAll(combatTask, domainEndTask);
    }

    /// <summary>
    /// 对局结束检测
    /// </summary>
    private Task DomainEndDetectionTask(CancellationTokenSource cts)
    {
        return new Task(async () =>
        {
            await Bv.WaitUntilFound(ElementAssets.Instance.BtnWhiteCancel, cts.Token, 150, 1000);
            await cts.CancelAsync();
        }, cts.Token);
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
            var res = regionList.FirstOrDefault(t => t.Text.Contains("地脉之花"));
            if (res != null)
            {
                // 解决水龙王按下左键后没松开，然后后续点击按下就没反应了，界面上点一下
                res.Click();
                return true;
            }

            return false;
        }, _ct, 10, 500);
        Debug.WriteLine("识别到选择树脂页");
        await Delay(800, _ct);

        // 再 OCR 一次，弹出框，确认当前是否有原粹树脂
        using var ra2 = CaptureToRectArea();
        var textListInPrompt = ra2.FindMulti(RecognitionObject.Ocr(ra2.Width * 0.25, ra2.Height * 0.2, ra2.Width * 0.5, ra2.Height * 0.6));
        if (textListInPrompt.Any(t => t.Text.Contains("数量不足") || t.Text.Contains("补充原粹树脂")))
        {
            // 没有原粹树脂，直接退出秘境
            _logger.LogInformation("自动秘境：原粹树脂已用尽，退出秘境");
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
                var resinStatus = ResinStatus.RecogniseFromRegion(ra3);
                resinStatus.Print(_logger);

                if (resinStatus is { CondensedResinCount: <= 0, OriginalResinCount: < 20 })
                {
                    _logger.LogWarning("树脂不足");
                    await ExitDomain();
                    return false;
                }

                bool resinUsed = false;
                if (resinStatus.CondensedResinCount > 0)
                {
                    resinUsed = PressUseResin(ra3, "浓缩树脂");
                    resinStatus.CondensedResinCount -= 1;
                }
                else if (resinStatus.OriginalResinCount >= 20)
                {
                    resinUsed = PressUseResin(ra3, "原粹树脂");
                    resinStatus.OriginalResinCount -= 20;
                }

                if (!resinUsed)
                {
                    _logger.LogWarning("自动秘境：未找到可用的树脂，可能是{Msg1} 或者 {Msg2}。", "树脂不足", "OCR 识别失败");
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
                var failCount = 0;
                foreach (var record in _resinPriorityListWhenSpecifyUse)
                {
                    if (record.RemainCount > 0 && PressUseResin(textListInPrompt2, record.Name))
                    {
                        record.RemainCount -= 1;
                        _logger.LogInformation("自动秘境：{Name} 刷取 {Re}/{Max}", record.Name, record.MaxCount - record.RemainCount, record.MaxCount);
                        break;
                    }
                    else
                    {
                        failCount++;
                    }
                }

                if (_resinPriorityListWhenSpecifyUse.Sum(o => o.RemainCount) <= 0)
                {
                    // 全部刷完
                    isLastTurn = true;
                }

                if (failCount == _resinPriorityListWhenSpecifyUse.Count)
                {
                    // 没有找到对应的树脂
                    _logger.LogWarning("自动秘境：指定树脂领取次数时，当前可用树脂选项无法满足配置。你可能设置的刷取次数过多！退出秘境。");
                    _logger.LogInformation("当前刷取情况：{ResinList}", string.Join(", ", _resinPriorityListWhenSpecifyUse.Select(o => $"{o.Name}({o.MaxCount - o.RemainCount}/{o.MaxCount})")));
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
                        if (textListInNoResinPrompt.Any(t => t.Text.Contains("是否仍要") && t.Text.Contains("挑战") && t.Text.Contains("秘境")))
                        {
                            var cancelBtn = textListInNoResinPrompt.FirstOrDefault(t => t.Text.Contains("取消"));
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

        throw new NormalEndException("未检测到秘境结束，可能是背包物品已满。");
    }

    private async Task ExitDomain()
    {
        Simulation.SendInput.Keyboard.KeyPress(VK.VK_ESCAPE);
        await Delay(500, _ct);
        Simulation.SendInput.Keyboard.KeyPress(VK.VK_ESCAPE);
        await Delay(800, _ct);
        Bv.ClickBlackConfirmButton(CaptureToRectArea());
    }

    private bool PressUseResin(ImageRegion ra, string resinName)
    {
        var regionList = ra.FindMulti(RecognitionObject.Ocr(ra.Width * 0.25, ra.Height * 0.2, ra.Width * 0.5, ra.Height * 0.6));
        return PressUseResin(regionList, resinName);
    }

    private bool PressUseResin(List<Region> regionList, string resinName)
    {
        var resinKey = regionList.FirstOrDefault(t => t.Text.Contains(resinName));
        if (resinKey != null)
        {
            // 找到树脂名称对应的按键，关键词为使用，是同一行的（高度相交）
            var useList = regionList.Where(t => t.Text.Contains("使用")).ToList();
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
                    Sleep(60, _ct);
                    useKey.Click();
                    _logger.LogInformation("自动秘境：使用 {ResinName}", resinName);
                    return true;
                }
                else
                {
                    _logger.LogWarning("自动秘境：未找到 {ResinName} 的使用按键", resinName);
                }
            }
            else
            {
                _logger.LogWarning("自动秘境：未找到 {ResinName} 的使用按键", resinName);
            }
        }

        return false;
    }

    /// <summary>
    /// 判断两个区域在垂直方向上是否有重叠
    /// </summary>
    private bool IsHeightOverlap(Region region1, Region region2)
    {
        int region1Top = region1.Y;
        int region1Bottom = region1.Y + region1.Height;
        int region2Top = region2.Y;
        int region2Bottom = region2.Y + region2.Height;

        // 检查区域是否在垂直方向上重叠
        return (region1Top <= region2Bottom && region1Bottom >= region2Top);
    }

    private async Task ArtifactSalvage()
    {
        if (!_taskParam.AutoArtifactSalvage)
        {
            return;
        }

        if (!int.TryParse(TaskContext.Instance().Config.AutoArtifactSalvageConfig.MaxArtifactStar, out var star))
        {
            star = 4;
        }

        await new AutoArtifactSalvageTask(star, false).Start(_ct);
    }


    private async Task ExitDomain(BvPage page)
    {
        await Delay(1000, _ct);

        Simulation.SendInput.Keyboard.KeyPress(VK.VK_ESCAPE);
        await Delay(1000, _ct);

        await page.Locator(ElementAssets.Instance.BtnExitDoor.Value).Click();

        // 等待传送完成
        await page.Locator(ElementAssets.Instance.PaimonMenuRo).WaitFor();

        await Delay(3000, _ct);
    }
}