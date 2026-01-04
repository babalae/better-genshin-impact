using BetterGenshinImpact.Core.BgiVision;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
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
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoDomain;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using static Vanara.PInvoke.User32;
using BetterGenshinImpact.GameTask.AutoFight;
using OpenCvSharp;

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

        try
        {
            await DoDomain();
        }
        catch (TaskCanceledException)
        {
            // do nothing
        }
        catch (Exception e)
        {
            _logger.LogInformation(e.Message);
        }

        await Delay(3000, ct);

        await ArtifactSalvage();
        Notify.Event(NotificationEvent.DomainEnd).Success($"{Name}结束");
    }

    private async Task DoDomain()
    {
        var page = new BvPage(_ct);

        // 前置进入秘境
        await TpToDomain(page);
        await SelectDifficulty(page);
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
            await Delay(2500, _ct); // 等待载入完成
            // 走一步防止在地脉花上
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
            await Delay(200, _ct);
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
            await Delay(3400, _ct); // 等待载入完成

            // 4. 寻找地脉花
            _logger.LogInformation($"{Name}：{{Text}}", "3. 寻找地脉花");
            await _lowerHeadThenWalkToTask!.Start(_ct);
            Simulation.SendInput.Keyboard.KeyPress(AutoPickAssets.Instance.PickVk);


            // 5. 快速领取奖励并判断是否有下一轮
            _logger.LogInformation($"{Name}：{{Text}}", "5. 领取奖励");
            if (!await GettingTreasure())
            {
                _logger.LogInformation($"{Name}：体力耗尽或者设置轮次已达标");
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

    private async Task TpToDomain(BvPage page, bool isRetry = false)
    {
        await new ReturnMainUiTask().Start(_ct);
        await Delay(100, _ct);

        // 检查是否当前已经在秘境前面，如果已经在了就直接进入
        if (page.Locator(AutoPickAssets.Instance.PickRo).WithRoi(r => r.CutRight(0.5)).IsExist())
        {
            if (page.GetByText("幽境危战").WithRoi(r => r.CutRight(0.5)).IsExist())
            {
                await Delay(500, _ct);
                Simulation.SendInput.Keyboard.KeyPress(AutoPickAssets.Instance.PickVk);
                _logger.LogInformation($"{Name}：交互秘境");
                return;
            }
        }

        // 使用传送方式进入

        // F5 打开活动
        Simulation.SendInput.SimulateAction(GIActions.OpenTheEventsMenu);
        await page.GetByText("活动一览").WithRoi(r => r.CutLeftTop(0.3, 0.2)).WaitFor();
        await Delay(500, _ct);

        // 查找并点击幽境危战 - 前往挑战
        if (page.GetByText("幽境危战").WithRoi(r => r.CutRight(0.5)).IsExist())
        {
            await page.GetByText("前往挑战").WithRoi(r => r.CutRight(0.5)).Click();
        }
        else if (page.GetByText("幽境危战").WithRoi(r => r.CutLeft(0.3)).IsExist())
        {
            await page.GetByText("幽境危战").WithRoi(r => r.CutLeft(0.3)).Click();
            await Delay(1500, _ct);
            await page.GetByText("前往挑战").WithRoi(r => r.CutRight(0.5)).Click();
        }
        else
        {
            throw new Exception("未找到幽境危战选项");
        }

        _logger.LogInformation($"{Name}：点击前往挑战");

        // 传送
        await Delay(1000, _ct);

        try
        {
            await page.Locator(QuickTeleportAssets.Instance.TeleportButtonRo)
                .WaitFor();
        }
        catch
        {
            if (!isRetry)
            {
                // 未找到传送按钮，先返回主界面再重新进入
                _logger.LogWarning($"{Name}：未找到传送按钮，返回七天神像重新开始");
                await new TpTask(_ct).TpToStatueOfTheSeven();
                // 重新执行从打开活动界面开始的流程
                await TpToDomain(page, isRetry: true);
            }

            return;
        }

        await page.Locator(QuickTeleportAssets.Instance.TeleportButtonRo).Click();
        _logger.LogInformation($"{Name}：点击传送");
        await Delay(800, _ct);

        // 等待传送完成
        await page.Locator(ElementAssets.Instance.PaimonMenuRo).WaitFor(60000);
        _logger.LogInformation($"{Name}：传送完成");

        await Delay(2000, _ct);
        Simulation.SendInput.Keyboard.KeyPress(AutoPickAssets.Instance.PickVk);
        _logger.LogInformation($"{Name}：交互秘境");
    }

    private async Task EnterDomain(BvPage page)
    {
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
        if (ocrList.Any(o => o.Text.Contains("角色预览")) && ocrList.Any(o => o.Text.Contains("开始挑战")))
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

            // 切换战斗队伍
            await SwitchTeam(page);

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
                AutoFightTask.FightStatusFlag = true;
                while (!cts.Token.IsCancellationRequested)
                {
                    // 通用化战斗策略
                    for (var i = 0; i < combatCommands.Count; i++)
                    {
                        var command = combatCommands[i];
                        var lastCommand = i == 0 ? command : combatCommands[i - 1];
                        command.Execute(combatScenes, lastCommand);
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
                AutoFightTask.FightStatusFlag = false;
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
        return new Task(async void () =>
        {
            try
            {
                var captureRect = TaskContext.Instance().SystemInfo.ScaleMax1080PCaptureRect;
                var assetScale = TaskContext.Instance().SystemInfo.AssetScale;
                RecognitionObject whiteCancelRo = new RecognitionObject
                {
                    Name = "BtnWhiteCancel",
                    RecognitionType = RecognitionTypes.TemplateMatch,
                    TemplateImageMat = ElementAssets.Instance.BtnWhiteCancel.TemplateImageMat,
                    RegionOfInterest = new Rect(captureRect.Width / 3, captureRect.Height - (int)(captureRect.Height * 0.22), captureRect.Width / 3, (int)(captureRect.Height * 0.22)),
                    Use3Channels = true
                }.InitTemplate();

                await NewRetry.WaitForAction(() =>
                {
                    using var ra = CaptureToRectArea();
                    using var ret = ra.Find(whiteCancelRo);
                    if (ret.IsExist())
                    {
                        // OCR 保证识别不出错
                        var list = ra.FindMulti(RecognitionObject.Ocr(ret.X + 40 * assetScale, ret.Y - 20 * assetScale, 270 * assetScale, ret.Height * 2));
                        if (list.Any(o => o.Text.Contains("返回")))
                        {
                            return true;
                        }
                    }

                    return false;
                }, cts.Token, 300, 1000);
                _logger.LogInformation("检测到战斗结束，结束战斗操作线程");
                await cts.CancelAsync();
            }
            catch (Exception e)
            {
                _logger.LogInformation("对局结束检测线程异常结束：{Msg}", e.Message);
                _logger.LogDebug(e, "对局结束检测线程异常结束");
            }
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
                var resinStatus = ResinStatus.RecogniseFromRegion(ra3, TaskContext.Instance().SystemInfo, OcrFactory.Paddle);
                resinStatus.Print(_logger);

                if (resinStatus is { CondensedResinCount: <= 0, OriginalResinCount: < 20 })
                {
                    _logger.LogWarning("树脂不足");
                    return false;
                }

                bool resinUsed = false;
                if (resinStatus.CondensedResinCount > 0)
                {
                    (resinUsed, _) = AutoDomainTask.PressUseResin(ra3, "浓缩树脂");
                    resinStatus.CondensedResinCount -= 1;
                }
                else if (resinStatus.OriginalResinCount >= 20)
                {
                    (resinUsed, var num) = AutoDomainTask.PressUseResin(ra3, "原粹树脂");
                    resinStatus.OriginalResinCount -= num;
                }

                if (!resinUsed)
                {
                    _logger.LogWarning("自动秘境：未找到可用的树脂，可能是{Msg1} 或者 {Msg2}。", "树脂不足", "OCR 识别失败");
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
                        var (success, _) = AutoDomainTask.PressUseResin(textListInPrompt2, record.Name);
                        if (success)
                        {
                            record.RemainCount -= 1;
                            Logger.LogInformation("自动秘境：{Name} 刷取 {Re}/{Max}", record.Name, record.MaxCount - record.RemainCount, record.MaxCount);
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
                    _logger.LogWarning("自动秘境：指定树脂领取次数时，当前可用树脂选项无法满足配置。你可能设置的刷取次数过多！退出秘境。");
                    _logger.LogInformation("当前刷取情况：{ResinList}", string.Join(", ", _resinPriorityListWhenSpecifyUse.Select(o => $"{o.Name}({o.MaxCount - o.RemainCount}/{o.MaxCount})")));
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
        await ExitDomain(new BvPage(_ct));
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

        await new AutoArtifactSalvageTask(new AutoArtifactSalvageTaskParam(star, javaScript: null, artifactSetFilter: null, maxNumToCheck: null, recognitionFailurePolicy: null)).Start(_ct);
    }

    private async Task SelectDifficulty(BvPage page)
    {
        await Delay(4000, _ct); // 等待动画完成

        // 检查是否需要从至危挑战切换到困难
        if (page.GetByText("至危挑战").WithRoi(r => r.CutLeftTop(0.5, 0.2)).IsExist())
        {
            _logger.LogInformation($"{Name}：找到至危挑战，尝试切换到困难模式");
            await Delay(500, _ct);
            await page.GetByText("至危挑战").WithRoi(r => r.CutLeftTop(0.5, 0.2)).Click();
            await Delay(500, _ct);
        }

        // 检查困难模式是否已选中
        var hardMode = page.GetByText("困难").WithRoi(r => r.CutRightTop(0.5, 0.2)).IsExist();

        if (hardMode)
        {
            _logger.LogInformation($"{Name}：确认困难模式");
        }
        else
        {
            _logger.LogWarning("未找到困难模式，尝试切换");
            await Delay(500, _ct);
            page.Click(1096, 186);
            await Delay(500, _ct);
            page.Click(1093, 399);
        }
    }

    private async Task SwitchTeam(BvPage page)
    {
        var fightTeamName = _taskParam.FightTeamName;
        if (string.IsNullOrEmpty(fightTeamName))
        {
            _logger.LogInformation($"{Name}：不更换战斗队伍");
            return;
        }

        _logger.LogInformation($"{Name}：配置战斗队伍为：{fightTeamName}");

        // 查找预设队伍按钮并点击它打开面板
        var teamButton = page.GetByText("预设队伍").WithRoi(r => r.CutRightBottom(0.3, 0.1)).FindAll().FirstOrDefault();

        if (teamButton == null)
        {
            // 如果按钮未找到，检查列表面板是否已打开
            var panelTitle = page.GetByText("预设队伍").WithRoi(r => r.CutLeftTop(0.15, 0.075)).FindAll().FirstOrDefault();
            if (panelTitle == null)
            {
                _logger.LogWarning("未找到预设队伍按钮，不执行切换操作");
                return;
            }
            // 列表面板已打开，跳过点击按钮步骤
        }
        else
        {
            // 点击预设队伍按钮打开队伍选择面板
            teamButton.Click();
            await Delay(100, _ct);
        }

        // 此时面板已打开，点击滚动条准备拖动
        page.Click(936, 150);
        await Delay(100, _ct);

        // 滚轮预操作 - 按住左键准备拖动列表
        Simulation.SendInput.Mouse.LeftButtonDown();
        await Delay(100, _ct);
        // 向上移动一点开始滚动
        GameCaptureRegion.GameRegion1080PPosMove(936, 140);
        await Delay(100, _ct);

        int yOffset = 0;
        const int maxRetries = 30;

        for (int retries = 0; retries < maxRetries; retries++)
        {
            // 查找队伍名称，OCR区域: 左侧竖列队伍列表
            var teamRegionList = page.GetByText(fightTeamName).WithRoi(r => r.CutLeft(0.18)).FindAll();
            var foundTeam = teamRegionList.FirstOrDefault();
            if (foundTeam != null)
            {
                Simulation.SendInput.Mouse.LeftButtonUp();
                await Delay(300, _ct);
                foundTeam.Click();
                await Delay(500, _ct);
                return;
            }

            // 滚轮操作 - 在滚动条(936, y)位置拖动
            yOffset += 100;
            if (130 + yOffset > 1080)
            {
                Simulation.SendInput.Mouse.LeftButtonUp();
                await Delay(100, _ct);
                _logger.LogWarning("未找到预设战斗队伍名称，保持原有队伍");
                Simulation.SendInput.Keyboard.KeyPress(VK.VK_ESCAPE);
                await Delay(500, _ct);
                return;
            }

            // 移动滚动条
            GameCaptureRegion.GameRegion1080PPosMove(936, 130 + yOffset);
            await Delay(200, _ct);
        }
    }

    private async Task ExitDomain(BvPage page)
    {
        var exitDoor = await NewRetry.WaitForElementAppear(
            ElementAssets.Instance.BtnExitDoor.Value,
            () => Simulation.SendInput.Keyboard.KeyPress(VK.VK_ESCAPE), // 点击队伍选择按钮
            _ct,
            4,
            1000
        );
        if (exitDoor)
        {
            await page.Locator(ElementAssets.Instance.BtnExitDoor.Value).Click();
            // 等待传送完成
            await page.Locator(ElementAssets.Instance.PaimonMenuRo).WaitFor(60000);

            await Delay(3000, _ct);
        }
        else
        {
            Logger.LogWarning("未能找到退出秘境按钮，可能已经退出秘境");
        }
    }
}