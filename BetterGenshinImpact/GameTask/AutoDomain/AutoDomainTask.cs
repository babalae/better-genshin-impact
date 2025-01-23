using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoFight;
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
using Compunet.YoloV8;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Job;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using static Vanara.PInvoke.Kernel32;
using static Vanara.PInvoke.User32;

namespace BetterGenshinImpact.GameTask.AutoDomain;

public class AutoDomainTask : ISoloTask
{
    public string Name => "自动秘境";

    private readonly AutoDomainParam _taskParam;

    private readonly PostMessageSimulator _simulator;

    private readonly YoloV8Predictor _predictor;

    private readonly AutoDomainConfig _config;

    private readonly CombatScriptBag _combatScriptBag;

    private CancellationToken _ct;

    public AutoDomainTask(AutoDomainParam taskParam)
    {
        _taskParam = taskParam;
        AutoFightAssets.DestroyInstance();
        _simulator = TaskContext.Instance().PostMessageSimulator;

        _predictor = YoloV8Builder.CreateDefaultBuilder()
            .UseOnnxModel(Global.Absolute(@"Assets\Model\Domain\bgi_tree.onnx"))
            .WithSessionOptions(BgiSessionOption.Instance.Options)
            .Build();

        _config = TaskContext.Instance().Config.AutoDomainConfig;

        _combatScriptBag = CombatScriptParser.ReadAndParse(_taskParam.CombatStrategyPath);
    }

    public async Task Start(CancellationToken ct)
    {
        _ct = ct;
        
        Init();
        NotificationHelper.SendTaskNotificationWithScreenshotUsing(b => b.Domain().Started().Build()); // TODO: 通知后续需要删除迁移

        // 3次复活重试
        for (int i = 0; i < 3; i++)
        {
            try
            {
                await DoDomain();
                // 其他场景不重试
                break;
            }
            catch (Exception e)
            {
                if (e.Message.Contains("复活") && !string.IsNullOrEmpty(_taskParam.DomainName))
                {
                    Logger.LogWarning("自动秘境：{Text}", "复活后重试秘境...");
                    await Delay(2000, ct);
                    continue;
                }
                else
                {
                    throw;
                }
            }
        }


        await Delay(2000, ct);
        await Bv.WaitForMainUi(_ct, 30);
        await Delay(2000, ct);

        await ArtifactSalvage();
    }

    private async Task DoDomain()
    {
        // 传送到秘境
        await TpDomain();
        // 切换队伍
        await SwitchParty(_taskParam.PartyName);

        var combatScenes = new CombatScenes().InitializeTeam(CaptureToRectArea());

        // 前置进入秘境
        await EnterDomain();

        for (var i = 0; i < _taskParam.DomainRoundNum; i++)
        {
            // 0. 关闭秘境提示
            Logger.LogDebug("0. 关闭秘境提示");
            await CloseDomainTip();

            // 队伍没初始化成功则重试
            RetryTeamInit(combatScenes);

            // 0. 切换到第一个角色
            var combatCommands = FindCombatScriptAndSwitchAvatar(combatScenes);

            // 1. 走到钥匙处启动
            Logger.LogInformation("自动秘境：{Text}", "1. 走到钥匙处启动");
            await WalkToPressF();

            // 2. 执行战斗（战斗线程、视角线程、检测战斗完成线程）
            Logger.LogInformation("自动秘境：{Text}", "2. 执行战斗策略");
            await StartFight(combatScenes, combatCommands);
            EndFightWait();

            // 3. 寻找石化古树 并左右移动直到石化古树位于屏幕中心
            Logger.LogInformation("自动秘境：{Text}", "3. 寻找石化古树");
            await FindPetrifiedTree();

            // 4. 走到石化古树处
            Logger.LogInformation("自动秘境：{Text}", "4. 走到石化古树处");
            await WalkToPressF();

            // 5. 快速领取奖励并判断是否有下一轮
            Logger.LogInformation("自动秘境：{Text}", "5. 领取奖励");
            if (!GettingTreasure(_taskParam.DomainRoundNum == 9999, i == _taskParam.DomainRoundNum - 1))
            {
                if (i == _taskParam.DomainRoundNum - 1)
                {
                    Logger.LogInformation("配置的{Cnt}轮秘境已经完成，结束自动秘境", _taskParam.DomainRoundNum);
                }
                else
                {
                    Logger.LogInformation("体力已经耗尽，结束自动秘境");
                }

                NotificationHelper.SendTaskNotificationWithScreenshotUsing(b => b.Domain().Success().Build());
                break;
            }

            NotificationHelper.SendTaskNotificationWithScreenshotUsing(b => b.Domain().Progress().Build());
        }
    }

    private void Init()
    {
        LogScreenResolution();
        if (_taskParam.DomainRoundNum == 9999)
        {
            Logger.LogInformation("→ {Text} 用尽所有体力后结束", "自动秘境，");
        }
        else
        {
            Logger.LogInformation("→ {Text} 设置总次数：{Cnt}", "自动秘境，", _taskParam.DomainRoundNum);
        }
    }

    private void LogScreenResolution()
    {
        var gameScreenSize = SystemControl.GetGameScreenRect(TaskContext.Instance().GameHandle);
        if (gameScreenSize.Width * 9 != gameScreenSize.Height * 16)
        {
            Logger.LogError("游戏窗口分辨率不是 16:9 ！当前分辨率为 {Width}x{Height} , 非 16:9 分辨率的游戏无法正常使用自动秘境功能 !", gameScreenSize.Width, gameScreenSize.Height);
            throw new Exception("游戏窗口分辨率不是 16:9");
        }

        if (gameScreenSize.Width < 1920 || gameScreenSize.Height < 1080)
        {
            Logger.LogWarning("游戏窗口分辨率小于 1920x1080 ！当前分辨率为 {Width}x{Height} , 小于 1920x1080 的分辨率的游戏可能无法正常使用自动秘境功能 !", gameScreenSize.Width, gameScreenSize.Height);
        }
    }

    private void RetryTeamInit(CombatScenes combatScenes)
    {
        if (!combatScenes.CheckTeamInitialized())
        {
            combatScenes.InitializeTeam(CaptureToRectArea());
            if (!combatScenes.CheckTeamInitialized())
            {
                throw new Exception("识别队伍角色失败，请在较暗背景下重试，比如游戏时间调整成夜晚。或者直接使用强制指定当前队伍角色的功能。");
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
                Logger.LogInformation("自动秘境：传送到秘境{Text}", _taskParam.DomainName);
                await new TpTask(_ct).Tp(domainPosition.X, domainPosition.Y);
                await Delay(1000, _ct);
                await Bv.WaitForMainUi(_ct);
                await Delay(1000, _ct);

                if ("芬德尼尔之顶".Equals(_taskParam.DomainName))
                {
                    Simulation.SendInput.SimulateAction(GIActions.MoveBackward, KeyType.KeyDown);
                    Thread.Sleep(3000);
                    Simulation.SendInput.SimulateAction(GIActions.MoveBackward, KeyType.KeyUp);
                }
                else if ("无妄引咎密宫".Equals(_taskParam.DomainName))
                {
                    Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
                    Thread.Sleep(500);
                    Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                    Thread.Sleep(100);
                    Simulation.SendInput.SimulateAction(GIActions.MoveLeft, KeyType.KeyDown);
                    Thread.Sleep(1600);
                    Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                }
                else if ("苍白的遗荣".Equals(_taskParam.DomainName))
                {
                    Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
                    Thread.Sleep(1000);
                    Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                }
                else if ("太山府".Equals(_taskParam.DomainName))
                {
                    // 直接F即可
                    // nothing to do
                }
                else
                {
                    Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
                    Thread.Sleep(2000);
                    Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                }

                Simulation.SendInput.SimulateAction(GIActions.Drop, KeyType.KeyUp); // 可能爬上去了，X键下来
                await Delay(3000, _ct); // 站稳
            }
            else
            {
                Logger.LogError("自动秘境：未找到对应的秘境{Text}的传送点", _taskParam.DomainName);
                throw new Exception($"未找到对应的秘境{_taskParam.DomainName}的传送点");
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
        var fightAssets = AutoFightContext.Instance.FightAssets;

        // 进入秘境
        for (int i = 0; i < 3; i++)  // 3次重试 有时候会拾取晶蝶
        {
            using var fRectArea = CaptureToRectArea().Find(AutoPickAssets.Instance.PickRo);
            if (!fRectArea.IsEmpty())
            {
                Simulation.SendInput.Keyboard.KeyPress(AutoPickAssets.Instance.PickVk);
                Logger.LogInformation("自动秘境：{Text}", "进入秘境");
                // 秘境开门动画 5s
                await Delay(5000, _ct);
            } 
            else
            { 
                await Delay(800, _ct);
            }
        }


        int retryTimes = 0, clickCount = 0;
        while (retryTimes < 20 && clickCount < 2)
        {
            retryTimes++;
            using var confirmRectArea = CaptureToRectArea().Find(fightAssets.ConfirmRa);
            if (!confirmRectArea.IsEmpty())
            {
                confirmRectArea.Click();
                clickCount++;
            }

            await Delay(1500, _ct);
        }

        // 载入动画
        await Delay(3000, _ct);
    }

    private async Task CloseDomainTip()
    {
        // 2min的载入时间总够了吧
        var retryTimes = 0;
        while (retryTimes < 120)
        {
            retryTimes++;
            using var cactRectArea = CaptureToRectArea().Find(AutoFightContext.Instance.FightAssets.ClickAnyCloseTipRa);
            if (!cactRectArea.IsEmpty())
            {
                await Delay(1000, _ct);
                cactRectArea.Click();
                break;
            }

            // todo 添加小地图角标位置检测 防止有人手点了
            await Delay(1000, _ct);
        }

        await Delay(2000, _ct);
    }

    private List<CombatCommand> FindCombatScriptAndSwitchAvatar(CombatScenes combatScenes)
    {
        var combatCommands = _combatScriptBag.FindCombatScript(combatScenes.Avatars);
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
            _simulator.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
            Sleep(30, _ct);
            // 组合键好像不能直接用 postmessage
            if (!_config.WalkToF)
            {
                Simulation.SendInput.SimulateAction(GIActions.SprintKeyboard, KeyType.KeyDown);
            }

            try
            {
                while (!_ct.IsCancellationRequested)
                {
                    using var fRectArea = Common.TaskControl.CaptureToRectArea().Find(AutoPickAssets.Instance.PickRo);
                    if (fRectArea.IsEmpty())
                    {
                        Sleep(100, _ct);
                    }
                    else
                    {
                        Logger.LogInformation("检测到交互键");
                        Simulation.SendInput.Keyboard.KeyPress(AutoPickAssets.Instance.PickVk);
                        break;
                    }
                }
            }
            finally
            {
                _simulator.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
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
                Logger.LogInformation("战斗操作中断：{Msg}", e.Message);
            }
            catch (Exception e)
            {
                Logger.LogWarning(e.Message);
                throw;
            }
            finally
            {
                Logger.LogInformation("自动战斗线程结束");
                combatScenes.AfterTask();
            }
        }, cts.Token);

        // 对局结束检测
        var domainEndTask = DomainEndDetectionTask(cts);
        // 自动吃药
        var autoEatRecoveryHpTask = AutoEatRecoveryHpTask(cts.Token);
        combatTask.Start();
        domainEndTask.Start();
        autoEatRecoveryHpTask.Start();
        return Task.WhenAll(combatTask, domainEndTask, autoEatRecoveryHpTask);
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
            Logger.LogInformation("战斗结束后等待 {Second} 秒", s);
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

        var endTipsRect = ra.DeriveCrop(AutoFightContext.Instance.FightAssets.EndTipsUpperRect);
        var text = OcrFactory.Paddle.Ocr(endTipsRect.SrcGreyMat);
        if (text.Contains("挑战") || text.Contains("达成"))
        {
            Logger.LogInformation("检测到秘境结束提示(挑战达成)，结束秘境");
            return true;
        }

        endTipsRect = ra.DeriveCrop(AutoFightContext.Instance.FightAssets.EndTipsRect);
        text = OcrFactory.Paddle.Ocr(endTipsRect.SrcGreyMat);
        if (text.Contains("自动") || text.Contains("退出"))
        {
            Logger.LogInformation("检测到秘境结束提示(xxx秒后自动退出)，结束秘境");
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
                Logger.LogInformation("未装备 “{Tool}”，不启用红血自动吃药功能", "便携营养袋");
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
                        Logger.LogInformation("检测到红血，按Z吃药");
                        // TODO 吃饱了会一直吃
                    }

                    await Delay(500, ct);
                }
            }
            catch (Exception e)
            {
                Logger.LogDebug(e, "红血自动吃药检测时发生异常");
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
        var count = OcrFactory.Paddle.OcrWithoutDetector(countArea.SrcGreyMat);
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
                if (treeRect != Rect.Empty)
                {
                    var treeMiddleX = treeRect.X + treeRect.Width / 2;
                    if (treeRect.X + treeRect.Width < middleX && !_config.ShortMovement)
                    {
                        backwardsAndForwardsCount = 0;
                        // 树在左边 往左走
                        Debug.WriteLine($"树在左边 往左走 {treeMiddleX}  {middleX}");
                        if (rightKeyDown)
                        {
                            // 先松开D键
                            _simulator.KeyUp(moveRightKey);
                            rightKeyDown = false;
                        }

                        if (!leftKeyDown)
                        {
                            _simulator.KeyDown(moveLeftKey);
                            leftKeyDown = true;
                        }
                    }
                    else if (treeRect.X > middleX && !_config.ShortMovement)
                    {
                        backwardsAndForwardsCount = 0;
                        // 树在右边 往右走
                        Debug.WriteLine($"树在右边 往右走 {treeMiddleX}  {middleX}");
                        if (leftKeyDown)
                        {
                            // 先松开A键
                            _simulator.KeyUp(moveLeftKey);
                            leftKeyDown = false;
                        }

                        if (!rightKeyDown)
                        {
                            _simulator.KeyDown(moveRightKey);
                            rightKeyDown = true;
                        }
                    }
                    else
                    {
                        // 树在中间 松开所有键
                        if (rightKeyDown)
                        {
                            _simulator.KeyUp(moveRightKey);
                            prevKey = moveRightKey;
                            rightKeyDown = false;
                        }

                        if (leftKeyDown)
                        {
                            _simulator.KeyUp(moveLeftKey);
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

                            _simulator.KeyPress(moveLeftKey, 60);
                            prevKey = moveLeftKey;
                        }
                        else if (treeMiddleX > middleX)
                        {
                            if (prevKey == moveLeftKey)
                            {
                                backwardsAndForwardsCount++;
                            }

                            _simulator.KeyPress(moveRightKey, 60);
                            prevKey = moveRightKey;
                        }
                        else
                        {
                            _simulator.KeyPress(moveForwardKey, 60);
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
                            _simulator.KeyUp(moveLeftKey);
                            leftKeyDown = false;
                        }

                        if (!rightKeyDown)
                        {
                            _simulator.KeyDown(moveRightKey);
                            rightKeyDown = true;
                        }
                    }
                    else
                    {
                        if (rightKeyDown)
                        {
                            _simulator.KeyUp(moveRightKey);
                            rightKeyDown = false;
                        }

                        if (!leftKeyDown)
                        {
                            _simulator.KeyDown(moveLeftKey);
                            leftKeyDown = true;
                        }
                    }
                }

                if (backwardsAndForwardsCount >= _config.LeftRightMoveTimes)
                {
                    // 左右移动5次说明已经在树中心了
                    _simulator.KeyPress(moveForwardKey, 60);
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
        using var memoryStream = new MemoryStream();
        region.SrcBitmap.Save(memoryStream, ImageFormat.Bmp);
        memoryStream.Seek(0, SeekOrigin.Begin);
        var result = _predictor.Detect(memoryStream);
        var list = new List<RectDrawable>();
        foreach (var box in result.Boxes)
        {
            var rect = new Rect(box.Bounds.X, box.Bounds.Y, box.Bounds.Width, box.Bounds.Height);
            list.Add(region.ToRectDrawable(rect, "tree"));
        }

        VisionContext.Instance().DrawContent.PutOrRemoveRectList("TreeBox", list);

        if (list.Count > 0)
        {
            var box = result.Boxes[0];
            return new Rect(box.Bounds.X, box.Bounds.Y, box.Bounds.Width, box.Bounds.Height);
        }

        return Rect.Empty;
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

                Sleep(100);
            }

            Logger.LogInformation("锁定东方向视角线程结束");
            VisionContext.Instance().DrawContent.ClearAll();
        });
    }

    /// <summary>
    /// 领取奖励
    /// </summary>
    /// <param name="recognizeResin">是否识别树脂</param>
    /// <param name="isLastTurn">是否最后一轮</param>
    private bool GettingTreasure(bool recognizeResin, bool isLastTurn)
    {
        // 等待窗口弹出
        Sleep(1500, _ct);

        // 优先使用浓缩树脂
        var retryTimes = 0;
        while (true)
        {
            retryTimes++;
            if (retryTimes > 3)
            {
                Logger.LogInformation("没有浓缩树脂了");
                break;
            }

            var useCondensedResinRa = CaptureToRectArea().Find(AutoFightContext.Instance.FightAssets.UseCondensedResinRa);
            if (!useCondensedResinRa.IsEmpty())
            {
                useCondensedResinRa.Click();
                // 点两下 #224 #218
                // 解决水龙王按下左键后没松开，然后后续点击按下就没反应了
                Sleep(400, _ct);
                useCondensedResinRa.Click();
                break;
            }

            Sleep(800, _ct);
        }

        Sleep(1000, _ct);

        var hasSkip = false;
        var captureArea = TaskContext.Instance().SystemInfo.ScaleMax1080PCaptureRect;
        var assetScale = TaskContext.Instance().SystemInfo.AssetScale;
        for (var i = 0; i < 30; i++)
        {
            // 跳过领取动画
            if (!hasSkip)
            {
                TaskContext.Instance().PostMessageSimulator.LeftButtonClick(); // 先随便点一个地方使得跳过出现
            }

            using var ra = CaptureToRectArea();

            // OCR识别是否有跳过
            var ocrList = ra.FindMulti(RecognitionObject.Ocr(captureArea.Width - 230 * assetScale, 0, 230 * assetScale - 5, 80 * assetScale));
            var skipTextRa = ocrList.FirstOrDefault(t => t.Text.Contains("跳过"));
            if (skipTextRa != null)
            {
                hasSkip = true;
                skipTextRa.Click(); // 有则点击
            }


            // 优先点击继续
            using var confirmRectArea = ra.Find(AutoFightContext.Instance.FightAssets.ConfirmRa);
            if (!confirmRectArea.IsEmpty())
            {
                if (isLastTurn)
                {
                    // 最后一回合 退出
                    var exitRectArea = ra.Find(AutoFightContext.Instance.FightAssets.ExitRa);
                    if (!exitRectArea.IsEmpty())
                    {
                        exitRectArea.Click();
                        return false;
                    }
                }

                if (!recognizeResin)
                {
                    confirmRectArea.Click();
                    return true;
                }

                var (condensedResinCount, fragileResinCount) = GetRemainResinStatus();
                if (condensedResinCount == 0 && fragileResinCount < 20)
                {
                    // 没有体力了退出
                    var exitRectArea = ra.Find(AutoFightContext.Instance.FightAssets.ExitRa);
                    if (!exitRectArea.IsEmpty())
                    {
                        exitRectArea.Click();
                        return false;
                    }
                }
                else
                {
                    // 有体力继续
                    confirmRectArea.Click();
                    return true;
                }
            }

            Sleep(300, _ct);
        }

        throw new NormalEndException("未检测到秘境结束，可能是背包物品已满。");
    }

    /// <summary>
    /// 获取剩余树脂状态
    /// </summary>
    private (int, int) GetRemainResinStatus()
    {
        var condensedResinCount = 0;
        var fragileResinCount = 0;

        var ra = CaptureToRectArea();
        // 浓缩树脂
        var condensedResinCountRa = ra.Find(AutoFightContext.Instance.FightAssets.CondensedResinCountRa);
        if (!condensedResinCountRa.IsEmpty())
        {
            // 图像右侧就是浓缩树脂数量
            var countArea = ra.DeriveCrop(condensedResinCountRa.X + condensedResinCountRa.Width, condensedResinCountRa.Y, condensedResinCountRa.Width, condensedResinCountRa.Height);
            // Cv2.ImWrite($"log/resin_{DateTime.Now.ToString("yyyy-MM-dd HH：mm：ss：ffff")}.png", countArea.SrcGreyMat);
            var count = OcrFactory.Paddle.OcrWithoutDetector(countArea.SrcGreyMat);
            condensedResinCount = StringUtils.TryParseInt(count);
        }

        // 脆弱树脂
        var fragileResinCountRa = ra.Find(AutoFightContext.Instance.FightAssets.FragileResinCountRa);
        if (!fragileResinCountRa.IsEmpty())
        {
            // 图像右侧就是脆弱树脂数量
            var countArea = ra.DeriveCrop(fragileResinCountRa.X + fragileResinCountRa.Width, fragileResinCountRa.Y, (int)(fragileResinCountRa.Width * 3), fragileResinCountRa.Height);
            var count = OcrFactory.Paddle.Ocr(countArea.SrcGreyMat);
            fragileResinCount = StringUtils.TryParseInt(count);
        }

        Logger.LogInformation("剩余：浓缩树脂 {CondensedResinCount} 脆弱树脂 {FragileResinCount}", condensedResinCount, fragileResinCount);
        return (condensedResinCount, fragileResinCount);
    }

    private async Task ArtifactSalvage()
    {
        if (!_taskParam.AutoArtifactSalvage)
        {
            return;
        }

        if (!int.TryParse(_taskParam.MaxArtifactStar, out var star))
        {
            star = 4;
        }

        await new ArtifactSalvageTask().Start(star, _ct);
    }
}