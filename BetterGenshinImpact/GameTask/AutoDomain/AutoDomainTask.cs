using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.GameTask.Common.MiniMap;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.View.Drawable;
using BetterGenshinImpact.ViewModel.Pages;
using Compunet.YoloV8;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using static Vanara.PInvoke.User32;

namespace BetterGenshinImpact.GameTask.AutoDomain;

public class AutoDomainTask
{
    private readonly AutoPickAssets _autoPickAssets = new();

    private readonly AutoDomainParam _taskParam;

    private readonly PostMessageSimulator _simulator;

    private readonly YoloV8 _predictor = new(Global.Absolute("Assets\\Model\\Domain\\bgi_tree.onnx"));

    private readonly ClickOffset _clickOffset;

    public AutoDomainTask(AutoDomainParam taskParam)
    {
        _taskParam = taskParam;
        _simulator = AutoFightContext.Instance().Simulator;

        var captureArea = TaskContext.Instance().SystemInfo.CaptureAreaRect;
        var assetScale = TaskContext.Instance().SystemInfo.AssetScale;
        _clickOffset = new ClickOffset(captureArea.X, captureArea.Y, assetScale);
    }

    public async void Start()
    {
        try
        {
            Init();
            var combatScenes = new CombatScenes().InitializeTeam(GetContentFromDispatcher());

            // 前置进入秘境
            EnterDomain();

            for (var i = 0; i < _taskParam.DomainRoundNum; i++)
            {
                // 0. 关闭秘境提示
                CloseDomainTip();

                // 队伍没初始化成功则重试
                RetryTeamInit(combatScenes);

                // 1. 走到钥匙处启动
                Logger.LogInformation("自动秘境：{Text}", "1. 走到钥匙处启动");
                await WalkToPressF();

                // 2. 执行战斗（战斗线程、视角线程、检测战斗完成线程）
                Logger.LogInformation("自动秘境：{Text}", "2. 执行战斗策略");
                await StartFight(combatScenes);

                // 3. 寻找石化古树 并左右移动直到石化古树位于屏幕中心
                Logger.LogInformation("自动秘境：{Text}", "3. 寻找石化古树");
                await FindPetrifiedTree();

                // 4. 走到石化古树处
                Logger.LogInformation("自动秘境：{Text}", "4. 走到石化古树处");
                await WalkToPressF();

                // 5. 快速领取奖励并判断是否有下一轮
                Logger.LogInformation("自动秘境：{Text}", "5. 领取奖励");
                GettingTreasure();
            }
        }
        catch (NormalEndException)
        {
            Logger.LogInformation("手动中断自动秘境");
        }
        catch (Exception e)
        {
            Logger.LogInformation(e.Message);
        }
        finally
        {
            VisionContext.Instance().DrawContent.ClearAll();
            TaskTriggerDispatcher.Instance().SetOnlyCaptureMode(false);
            TaskSettingsPageViewModel.SetSwitchAutoDomainButtonText(false);
            Logger.LogInformation("→ {Text}", "自动秘境结束");
        }
    }

    private void Init()
    {
        if (_taskParam.DomainRoundNum == 9999)
        {
            Logger.LogInformation("→ {Text} 用尽所有体力后结束", "自动秘境，启动！");
        }
        else
        {
            Logger.LogInformation("→ {Text} 设置总次数：{Cnt}", "自动秘境，启动！", _taskParam.DomainRoundNum);
        }

        SystemControl.ActivateWindow();
        TaskTriggerDispatcher.Instance().SetOnlyCaptureMode(true);
    }

    private void RetryTeamInit(CombatScenes combatScenes)
    {
        if (!combatScenes.CheckTeamInitialized())
        {
            combatScenes.InitializeTeam(GetContentFromDispatcher());
            if (!combatScenes.CheckTeamInitialized())
            {
                throw new Exception("识别队伍角色并初始化失败");
            }
        }
    }

    private void EnterDomain()
    {
        var fightAssets = AutoFightContext.Instance().FightAssets;

        var fRectArea = GetContentFromDispatcher().CaptureRectArea.Find(_autoPickAssets.FRo);
        if (!fRectArea.IsEmpty())
        {
            Simulation.SendInputEx.Keyboard.KeyDown(VK.VK_F);
            Logger.LogInformation("自动秘境：{Text}", "进入秘境");
            // 秘境开门动画 5s
            Sleep(5000, _taskParam.Cts);
        }

        int retryTimes = 0, clickCount = 0;
        while (retryTimes < 10 && clickCount < 2)
        {
            retryTimes++;
            var confirmRectArea = GetContentFromDispatcher().CaptureRectArea.Find(fightAssets.ConfirmRa);
            if (!confirmRectArea.IsEmpty())
            {
                confirmRectArea.ClickCenter();
                clickCount++;
            }

            Sleep(1000, _taskParam.Cts);
        }

        // 载入动画
        Sleep(3000, _taskParam.Cts);
    }

    private void CloseDomainTip()
    {
        // 2min的载入时间总够了吧
        var retryTimes = 0;
        while (retryTimes < 120)
        {
            retryTimes++;
            var cactRectArea = GetContentFromDispatcher().CaptureRectArea.Find(AutoFightContext.Instance().FightAssets.ClickAnyCloseTipRa);
            if (!cactRectArea.IsEmpty())
            {
                Sleep(1000, _taskParam.Cts);
                cactRectArea.ClickCenter();
                break;
            }

            // todo 添加小地图角标位置检测 防止有人手点了
            Sleep(1000, _taskParam.Cts);
        }

        Sleep(1500, _taskParam.Cts);
    }

    /// <summary>
    /// 走到钥匙处启动
    /// </summary>
    private async Task WalkToPressF()
    {
        if (_taskParam.Cts.Token.IsCancellationRequested)
        {
            return;
        }

        await Task.Run(() =>
        {
            _simulator.KeyDown(VK.VK_W);
            Sleep(20);
            // 组合键好像不能直接用 postmessage
            Simulation.SendInputEx.Keyboard.KeyDown(VK.VK_SHIFT);
            try
            {
                while (!_taskParam.Cts.Token.IsCancellationRequested)
                {
                    var content = GetContentFromDispatcher();
                    var fRectArea = content.CaptureRectArea.Find(_autoPickAssets.FRo);
                    if (fRectArea.IsEmpty())
                    {
                        Sleep(100, _taskParam.Cts);
                    }
                    else
                    {
                        Logger.LogInformation("检测到交互键");
                        Simulation.SendInputEx.Keyboard.KeyPress(VK.VK_F);
                        break;
                    }
                }
            }
            finally
            {
                _simulator.KeyUp(VK.VK_W);
                Sleep(50);
                Simulation.SendInputEx.Keyboard.KeyUp(VK.VK_SHIFT);
            }
        });
    }

    private Task StartFight(CombatScenes combatScenes)
    {
        CancellationTokenSource cts = new CancellationTokenSource();
        _taskParam.Cts.Token.Register(cts.Cancel);
        combatScenes.BeforeTask(cts);
        // 战斗操作
        var combatTask = new Task(() =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    //TODO 通用化战斗策略
                    // 钟离
                    combatScenes.Avatars[2].Switch();
                    combatScenes.Avatars[2].Walk("s", 200);
                    combatScenes.Avatars[2].UseSkill(hold: true);
                    Sleep(800);
                    combatScenes.Avatars[2].Walk("w", 200);
                    combatScenes.Avatars[2].UseBurst();


                    // 4号位
                    combatScenes.Avatars[3].Switch();
                    combatScenes.Avatars[3].UseSkill();
                    combatScenes.Avatars[3].UseBurst();

                    // 夜兰
                    combatScenes.Avatars[1].Switch();
                    combatScenes.Avatars[1].UseSkill();
                    combatScenes.Avatars[1].UseSkill();
                    Sleep(800);
                    combatScenes.Avatars[1].UseSkill();
                    combatScenes.Avatars[1].UseSkill();
                    Sleep(1900); // 等待元素球
                    combatScenes.Avatars[1].UseBurst();

                    // 钟离
                    combatScenes.Avatars[2].Switch();
                    combatScenes.Avatars[2].Walk("s", 200);
                    combatScenes.Avatars[2].UseSkill(hold: true);
                    Sleep(800);
                    combatScenes.Avatars[2].Walk("w", 200);
                    combatScenes.Avatars[2].UseBurst();

                    // 宵宫
                    combatScenes.Avatars[0].Switch();
                    combatScenes.Avatars[0].UseSkill();
                    combatScenes.Avatars[0].Attack(6000);
                }
            }
            catch (NormalEndException)
            {
                Logger.LogInformation("战斗操作结束");
            }
            catch (Exception e)
            {
                Logger.LogWarning(e.Message);
                throw;
            }
        }, cts.Token);

        // 视角操作

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
        return new Task(() =>
        {
            while (!_taskParam.Cts.Token.IsCancellationRequested)
            {
                if (IsDomainEnd())
                {
                    cts.Cancel();
                    break;
                }

                Sleep(1000);
            }
        });
    }

    private bool IsDomainEnd()
    {
        var content = GetContentFromDispatcher();
        var endTipsRect = content.CaptureRectArea.Crop(AutoFightContext.Instance().FightAssets.EndTipsRect);
        var result = OcrFactory.Paddle.OcrResult(endTipsRect.SrcGreyMat);
        var rect = result.FindRectByText("自动退出");
        if (rect == Rect.Empty)
        {
            return false;
        }

        Logger.LogInformation("检测到秘境结束提示，结束秘境");
        return true;
    }


    /// <summary>
    /// 旋转视角后寻找石化古树
    /// </summary>
    private Task FindPetrifiedTree()
    {
        CancellationTokenSource treeCts = new CancellationTokenSource();
        _taskParam.Cts.Token.Register(treeCts.Cancel);
        // 中键回正视角
        Simulation.SendInput.Mouse.MiddleButtonClick();
        Sleep(900, _taskParam.Cts);

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
            var captureArea = TaskContext.Instance().SystemInfo.CaptureAreaRect;
            var middleX = captureArea.Width / 2;
            var leftKeyDown = false;
            var rightKeyDown = false;
            var noDetectCount = 0;
            while (!_taskParam.Cts.Token.IsCancellationRequested)
            {
                var treeRect = DetectTree(GetContentFromDispatcher());
                if (treeRect != Rect.Empty)
                {
                    var treeMiddleX = treeRect.X + treeRect.Width / 2;
                    var distance = Math.Abs(middleX - treeMiddleX);
                    if (treeMiddleX < middleX && distance > treeRect.Width / 2)
                    {
                        // 树在左边 往左走
                        Debug.WriteLine($"树在左边 往左走 {treeMiddleX}  {middleX}");
                        if (rightKeyDown)
                        {
                            // 先松开D键
                            _simulator.KeyUp(VK.VK_D);
                            rightKeyDown = false;
                        }

                        if (!leftKeyDown)
                        {
                            _simulator.KeyDown(VK.VK_A);
                            leftKeyDown = true;
                        }
                    }
                    else if (treeMiddleX > middleX && distance > treeRect.Width / 2)
                    {
                        // 树在右边 往右走
                        Debug.WriteLine($"树在右边 往右走 {treeMiddleX}  {middleX}");
                        if (leftKeyDown)
                        {
                            // 先松开A键
                            _simulator.KeyUp(VK.VK_A);
                            leftKeyDown = false;
                        }

                        if (!rightKeyDown)
                        {
                            _simulator.KeyDown(VK.VK_D);
                            rightKeyDown = true;
                        }
                    }
                    else
                    {
                        // 树在中间 松开所有键
                        if (rightKeyDown)
                        {
                            _simulator.KeyUp(VK.VK_D);
                            rightKeyDown = false;
                        }

                        if (leftKeyDown)
                        {
                            _simulator.KeyUp(VK.VK_A);
                            leftKeyDown = false;
                        }

                        treeCts.Cancel();
                        break;
                    }
                }
                else
                {
                    // 左右巡逻
                    noDetectCount++;
                    if (noDetectCount > 40)
                    {
                        if (leftKeyDown)
                        {
                            _simulator.KeyUp(VK.VK_A);
                            leftKeyDown = false;
                        }

                        if (!rightKeyDown)
                        {
                            _simulator.KeyDown(VK.VK_D);
                            rightKeyDown = true;
                        }
                    }
                    else
                    {
                        if (rightKeyDown)
                        {
                            _simulator.KeyUp(VK.VK_D);
                            rightKeyDown = false;
                        }

                        if (!leftKeyDown)
                        {
                            _simulator.KeyDown(VK.VK_A);
                            leftKeyDown = true;
                        }
                    }
                }

                Sleep(150);
            }

            VisionContext.Instance().DrawContent.ClearAll();
        });
    }

    private Rect DetectTree(CaptureContent content)
    {
        using var memoryStream = new MemoryStream();
        content.CaptureRectArea.SrcBitmap.Save(memoryStream, ImageFormat.Bmp);
        memoryStream.Seek(0, SeekOrigin.Begin);
        var result = _predictor.Detect(memoryStream);
        var list = new List<RectDrawable>();
        foreach (var box in result.Boxes)
        {
            var rect = new System.Windows.Rect(box.Bounds.X, box.Bounds.Y, box.Bounds.Width, box.Bounds.Height);
            list.Add(new RectDrawable(rect));
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
                var angle = CameraOrientation.Compute(GetContentFromDispatcher());
                if (angle is >= 356 or <= 4)
                {
                    // 算作对准了
                    continuousCount++;
                }

                if (angle < 180)
                {
                    // 左移视角
                    var moveAngle = angle;
                    if (moveAngle > 2)
                    {
                        moveAngle *= 2;
                    }

                    Simulation.SendInputEx.Mouse.MoveMouseBy(-moveAngle, 0);
                    continuousCount = 0;
                }
                else if (angle is > 180 and < 360)
                {
                    // 右移视角
                    var moveAngle = 360 - angle;
                    if (moveAngle > 2)
                    {
                        moveAngle *= 2;
                    }

                    Simulation.SendInputEx.Mouse.MoveMouseBy(moveAngle, 0);
                    continuousCount = 0;
                }
                else
                {
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

                Sleep(100);
            }

            Logger.LogInformation("锁定东方向视角线程结束");
            VisionContext.Instance().DrawContent.ClearAll();
        });
    }

    /// <summary>
    /// 领取奖励
    /// </summary>
    private void GettingTreasure()
    {
        // 等待窗口弹出
        Sleep(1000, _taskParam.Cts);

        // 优先使用浓缩树脂
        GetContentFromDispatcher().CaptureRectArea.Find(AutoFightContext.Instance().FightAssets.UseCondensedResinRa, area => { area.ClickCenter(); });

        Sleep(500, _taskParam.Cts);


        var captureArea = TaskContext.Instance().SystemInfo.CaptureAreaRect;
        while (true)
        {
            // 跳过领取动画
            _clickOffset.ClickWithoutScale(captureArea.Width - (int)(140 * _clickOffset.AssetScale), (int)(53 * _clickOffset.AssetScale));
            Sleep(200, _taskParam.Cts);
            _clickOffset.ClickWithoutScale(captureArea.Width - (int)(140 * _clickOffset.AssetScale), (int)(53 * _clickOffset.AssetScale));

            // 优先点击继续
            var content = GetContentFromDispatcher();
            var confirmRectArea = content.CaptureRectArea.Find(AutoFightContext.Instance().FightAssets.ConfirmRa);
            if (!confirmRectArea.IsEmpty())
            {
                var (condensedResinCount, fragileResinCount) = GetRemainResinStatus();
                if (condensedResinCount == 0 && fragileResinCount < 20)
                {
                    // 没有体力了退出
                    var exitRectArea = content.CaptureRectArea.Find(AutoFightContext.Instance().FightAssets.ExitRa);
                    if (!exitRectArea.IsEmpty())
                    {
                        exitRectArea.ClickCenter();
                        break;
                    }
                }
                else
                {
                    // 有体力继续
                    confirmRectArea.ClickCenter();
                    break;
                }
            }

            Sleep(300, _taskParam.Cts);
        }
    }

    /// <summary>
    /// 获取剩余树脂状态
    /// </summary>
    private (int, int) GetRemainResinStatus()
    {
        var condensedResinCount = 0;
        var fragileResinCount = 0;

        var content = GetContentFromDispatcher();
        // 浓缩树脂
        var condensedResinCountRa = content.CaptureRectArea.Find(AutoFightContext.Instance().FightAssets.CondensedResinCountRa);
        if (!condensedResinCountRa.IsEmpty())
        {
            // 图像右侧就是浓缩树脂数量
            var countArea = content.CaptureRectArea.Crop(new Rect(condensedResinCountRa.X + condensedResinCountRa.Width, condensedResinCountRa.Y, condensedResinCountRa.Width, condensedResinCountRa.Height));
            var count = OcrFactory.Paddle.Ocr(countArea.SrcGreyMat);
            condensedResinCount = StringUtils.TryParseInt(count);
        }

        // 脆弱树脂
        var fragileResinCountRa = content.CaptureRectArea.Find(AutoFightContext.Instance().FightAssets.FragileResinCountRa);
        if (!fragileResinCountRa.IsEmpty())
        {
            // 图像右侧就是脆弱树脂数量
            var countArea = content.CaptureRectArea.Crop(new Rect(fragileResinCountRa.X + fragileResinCountRa.Width, fragileResinCountRa.Y, (int)(fragileResinCountRa.Width * 2.5), fragileResinCountRa.Height));
            var count = OcrFactory.Paddle.Ocr(countArea.SrcGreyMat);
            fragileResinCount = StringUtils.TryParseInt(count);
        }

        Logger.LogInformation("剩余：浓缩树脂 {CondensedResinCount} 脆弱树脂 {FragileResinCount}", condensedResinCount, fragileResinCount);
        return (condensedResinCount, fragileResinCount);
    }
}