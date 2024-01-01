using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.GameTask.Common.MiniMap;
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
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using static Vanara.PInvoke.Gdi32;
using static Vanara.PInvoke.User32;

namespace BetterGenshinImpact.GameTask.AutoDomain;

public class AutoDomainTask
{
    private readonly AutoPickAssets _autoPickAssets = new();

    private readonly AutoDomainParam _taskParam;

    private readonly PostMessageSimulator _simulator;

    private readonly YoloV8 _predictor = new(Global.Absolute("Assets\\Model\\Domain\\bgi_tree.onnx"));

    public AutoDomainTask(AutoDomainParam taskParam)
    {
        _taskParam = taskParam;
        _simulator = AutoFightContext.Instance().Simulator;
    }

    public async void Start()
    {
        try
        {
            Init();
            var combatScenes = new CombatScenes().InitializeTeam(GetContentFromDispatcher());

            // 前置进入秘境
            EnterDomain();

            Sleep(1000, _taskParam.Cts);

            // 队伍没初始化成功则重试
            if (!combatScenes.CheckTeamInitialized())
            {
                combatScenes.InitializeTeam(GetContentFromDispatcher());
                if (!combatScenes.CheckTeamInitialized())
                {
                    Logger.LogError("识别队伍角色并初始化失败");
                    return;
                }
            }

            for (var i = 0; i < _taskParam.DomainRoundNum; i++)
            {
                // 1. 走到钥匙处启动
                await WalkToPressF();

                // 2. 执行战斗（战斗线程、视角线程、检测战斗完成线程）
                await StartFight(combatScenes);

                // 3. 寻找石化古树 并左右移动直到石化古树位于屏幕中心
                await FindPetrifiedTree();

                // 4. 走到石化古树处
                await WalkToPressF();

                // 快速领取奖励并判断是否有下一轮
            }
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
        Logger.LogInformation("→ {Text} 设置总次数：{Cnt}", "自动秘境，启动！", _taskParam.DomainRoundNum);
        SystemControl.ActivateWindow();
        TaskTriggerDispatcher.Instance().SetOnlyCaptureMode(true);
    }

    private void EnterDomain()
    {
        var fightAssets = AutoFightContext.Instance().FightAssets;

        var fRectArea = GetContentFromDispatcher().CaptureRectArea.Find(_autoPickAssets.FRo);
        if (!fRectArea.IsEmpty())
        {
            Simulation.SendInputEx.Keyboard.KeyDown(VK.VK_F);
            Logger.LogInformation("进入秘境");
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

        Sleep(3000, _taskParam.Cts);
        retryTimes = 0;
        // 2min的载入时间总够了吧
        while (retryTimes < 120)
        {
            retryTimes++;
            var cactRectArea = GetContentFromDispatcher().CaptureRectArea.Find(fightAssets.ClickAnyCloseTipRa);
            if (!cactRectArea.IsEmpty())
            {
                cactRectArea.ClickCenter();
                break;
            }

            // todo 添加小地图角标位置检测 防止有人手点了
            Sleep(1000, _taskParam.Cts);
        }
    }

    /// <summary>
    /// 走到钥匙处启动
    /// </summary>
    private async Task WalkToPressF()
    {
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
        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(_taskParam.Cts.Token);
        combatScenes.BeforeTask(cts);
        // 战斗操作
        var combatTask = new Task(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                // 钟离
                combatScenes.Avatars[2].Switch();
                combatScenes.Avatars[2].Walk("s", 200);
                combatScenes.Avatars[2].UseSkill(hold: true);
                Sleep(800);
                combatScenes.Avatars[2].UseBurst();
                combatScenes.Avatars[2].Walk("w", 150);

                // 4号位
                combatScenes.Avatars[3].Switch();
                combatScenes.Avatars[3].UseSkill();
                Sleep(800);
                combatScenes.Avatars[3].UseBurst();

                // 夜兰
                combatScenes.Avatars[1].Switch();
                combatScenes.Avatars[1].UseSkill();
                combatScenes.Avatars[1].UseSkill();
                combatScenes.Avatars[1].UseSkill();
                combatScenes.Avatars[1].UseSkill();
                Sleep(800);
                combatScenes.Avatars[1].UseBurst();

                // 宵宫
                combatScenes.Avatars[0].Switch();
                combatScenes.Avatars[0].UseSkill();
                combatScenes.Avatars[0].Attack(6000);
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
        CancellationTokenSource treeCts = CancellationTokenSource.CreateLinkedTokenSource(_taskParam.Cts.Token);
        ;
        // 中键回正视角
        Simulation.SendInput.Mouse.MiddleButtonClick();
        Sleep(900);

        // 左右移动直到石化古树位于屏幕中心任务
        var moveAvatarTask = MoveAvatarHorizontallyTask(treeCts);

        // 锁定东方向视角线程
        var lockCameraToEastTask = LockCameraToEastTask(treeCts, moveAvatarTask);
        lockCameraToEastTask.Start();
        return Task.WhenAll(moveAvatarTask, lockCameraToEastTask);
    }

    private Task MoveAvatarHorizontallyTask(CancellationTokenSource cts)
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

                        cts.Cancel();
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
                    if (moveAngle > 0)
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
                    if (moveAngle > 0)
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

            VisionContext.Instance().DrawContent.ClearAll();
        });
    }

    private void WalkToPetrifiedTree()
    {
    }
}